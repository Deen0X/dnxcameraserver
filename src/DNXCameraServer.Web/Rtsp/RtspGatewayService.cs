using System.Net;
using System.Net.Sockets;
using System.Text;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Configuration;
using DNXCameraServer.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Rtsp;

/// <summary>
/// Gateway RTSP (ADR-004): autentica credenciales por dispositivo, aplica ACL y
/// hace splice TCP hacia el RTSP interno de go2rtc (que exime a loopback).
/// No interpreta RTP.
/// </summary>
public sealed class RtspGatewayService(
    DnxOptions options,
    IServiceScopeFactory scopes,
    ILogger<RtspGatewayService> log) : BackgroundService
{
    private static readonly string[] PublicMethods =
        ["OPTIONS", "DESCRIBE", "SETUP", "PLAY", "PAUSE", "TEARDOWN", "GET_PARAMETER", "SET_PARAMETER"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, options.RtspListenPort);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo abrir el puerto RTSP {Port}.", options.RtspListenPort);
            return;
        }

        log.LogInformation("Gateway RTSP escuchando en :{Port}.", options.RtspListenPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // apagado
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        using var _ = client;

        try
        {
            using var stream = client.GetStream();

            // Varias peticiones pueden llegar por la misma conexión (OPTIONS →
            // DESCRIBE → SETUP → PLAY). Se autoriza en la primera que lo requiera
            // y, a partir de ahí, se hace splice transparente.
            for (var i = 0; i < 12; i++)
            {
                var request = await RtspRequest.ReadAsync(stream, ct);
                if (request is null) return;
                request.RemoteIp = remote;

                if (request.Method == "OPTIONS")
                {
                    await RtspRequest.WriteAsync(stream,
                        "RTSP/1.0 200 OK", request.CSeq, $"Public: {string.Join(", ", PublicMethods)}", ct);
                    continue;
                }

                var streamName = ExtractStreamName(request.Uri);
                var auth = await AuthenticateAsync(streamName, request, ct);

                if (auth is null)
                {
                    await RtspRequest.WriteAsync(stream, "RTSP/1.0 401 Unauthorized", request.CSeq,
                        $"WWW-Authenticate: {DigestAuth.Challenge(options.Realm, DigestAuth.CreateNonce(NonceKey()))}\r\n" +
                        $"WWW-Authenticate: Basic realm=\"{options.Realm}\"", ct);
                    await AuditAsync(AuditActions.RtspDenied, null, streamName, remote, "auth");
                    continue;
                }

                if (auth.Camera is null || auth.Camera.StreamName != streamName)
                {
                    await RtspRequest.WriteAsync(stream, "RTSP/1.0 404 Not Found", request.CSeq, null, ct);
                    await AuditAsync(AuditActions.RtspDenied, auth, streamName, remote, "stream");
                    return;
                }

                if (!auth.Camera.Enabled ||
                    auth.Camera.ReleasedUntil is { } until && until > DateTimeOffset.UtcNow)
                {
                    await RtspRequest.WriteAsync(stream, "RTSP/1.0 503 Service Unavailable", request.CSeq, null, ct);
                    await AuditAsync(AuditActions.RtspDenied, auth, streamName, remote, "released");
                    return;
                }

                var (host, port) = SplitEndpoint(options.Go2RtcRtsp);
                using var upstream = new TcpClient();
                try
                {
                    await upstream.ConnectAsync(host, port, ct);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "go2rtc no disponible para {Stream}.", streamName);
                    await RtspRequest.WriteAsync(stream, "RTSP/1.0 503 Service Unavailable", request.CSeq, null, ct);
                    return;
                }

                await upstream.GetStream().WriteAsync(request.Raw, ct);
                await AuditAsync(AuditActions.RtspConnect, auth, streamName, remote, "ok");
                await SpliceAsync(stream, upstream.GetStream(), ct);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            log.LogDebug("Conexión RTSP cerrada ({Remote}): {Message}", remote, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Error en gateway RTSP ({Remote}).", remote);
        }
    }

    private async Task<AuthContext?> AuthenticateAsync(string streamName, RtspRequest request, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue("Authorization", out var header) || string.IsNullOrWhiteSpace(header))
            return null;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CameraDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

        string? tokenId;
        DeviceToken? token;

        if (header.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
        {
            var p = DigestAuth.ParseParameters(header);
            if (!p.TryGetValue("username", out tokenId)) return null;

            token = await db.DeviceTokens.Include(t => t.Camera)
                .FirstOrDefaultAsync(t => t.TokenId == tokenId, ct);
            if (token is null || token.RevokedAt is not null) return null;
            if (token.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow) return null;
            if (!DigestAuth.VerifyResponse(p, request.Method, token.DigestHa1, NonceKey())) return null;
        }
        else if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            byte[] decoded;
            try { decoded = Convert.FromBase64String(header[6..].Trim()); }
            catch { return null; }

            var text = Encoding.UTF8.GetString(decoded);
            var idx = text.IndexOf(':');
            if (idx <= 0) return null;

            tokenId = text[..idx];
            var secret = text[(idx + 1)..];

            token = await db.DeviceTokens.Include(t => t.Camera)
                .FirstOrDefaultAsync(t => t.TokenId == tokenId, ct);
            if (token is null || token.RevokedAt is not null) return null;
            if (token.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow) return null;
            if (!passwords.Verify(token.SecretHash, secret)) return null;
        }
        else
        {
            return null;
        }

        token.LastUsedAt = DateTimeOffset.UtcNow;
        token.LastIp = request.RemoteIp;
        await db.SaveChangesAsync(ct);

        return new AuthContext(token, token.Camera);
    }

    private async Task AuditAsync(
        string action, AuthContext? auth, string streamName, string remote, string detail)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CameraDbContext>();
            db.Audit.Add(new AuditEvent
            {
                Action = action,
                ActorType = "device",
                ActorId = auth?.Token.TokenId,
                CameraId = auth?.Camera?.Id,
                Ip = remote,
                DetailJson = $"{{\"stream\":\"{streamName}\",\"result\":\"{detail}\"}}"
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "No se pudo auditar el acceso RTSP.");
        }
    }

    private string NonceKey() => options.Realm + "|" + options.AdminUser + "|dnx-nonce";

    private static string ExtractStreamName(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            var path = parsed.AbsolutePath.Trim('/');
            var idx = path.LastIndexOf('/');
            return idx >= 0 ? path[(idx + 1)..] : path;
        }

        var queryIdx = uri.IndexOf('?');
        if (queryIdx >= 0) uri = uri[..queryIdx];
        var trimmed = uri.Trim('/');
        var last = trimmed.LastIndexOf('/');
        return last >= 0 ? trimmed[(last + 1)..] : trimmed;
    }

    private static (string Host, int Port) SplitEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':');
        return parts.Length == 2 && int.TryParse(parts[1], out var port)
            ? (parts[0], port)
            : (endpoint, 18554);
    }

    private static async Task SpliceAsync(Stream client, Stream upstream, CancellationToken ct)
    {
        var c2u = client.CopyToAsync(upstream, 81920, ct);
        var u2c = upstream.CopyToAsync(client, 81920, ct);
        await Task.WhenAny(c2u, u2c);
        try { await Task.WhenAll(c2u, u2c); } catch { /* cierre normal */ }
    }

    private sealed record AuthContext(DeviceToken Token, Camera? Camera);
}

/// <summary>Petición RTSP mínima: línea de petición + cabeceras + bytes originales.</summary>
public sealed class RtspRequest
{
    private const int MaxHeaderBytes = 32 * 1024;

    public string Method { get; private init; } = "";
    public string Uri { get; private init; } = "";
    public string CSeq { get; private init; } = "0";
    public string RemoteIp { get; set; } = "";
    public Dictionary<string, string> Headers { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Raw { get; private init; } = [];

    public static async Task<RtspRequest?> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[4096];

        while (buffer.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read <= 0) return null;
            buffer.Write(chunk, 0, read);

            var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            var end = data.IndexOf("\r\n\r\n"u8);
            if (end < 0) continue;

            var headerBytes = data[..(end + 4)].ToArray();
            var text = Encoding.UTF8.GetString(headerBytes);
            var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;

            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var idx = line.IndexOf(':');
                if (idx > 0) headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }

            return new RtspRequest
            {
                Method = parts[0].ToUpperInvariant(),
                Uri = parts[1],
                CSeq = headers.GetValueOrDefault("CSeq", "0"),
                Headers = headers,
                Raw = buffer.ToArray()
            };
        }

        return null;
    }

    public static async Task WriteAsync(
        NetworkStream stream, string status, string cseq, string? extra, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append(status).Append("\r\n");
        sb.Append("CSeq: ").Append(cseq).Append("\r\n");
        sb.Append("Server: DNXCameraServer\r\n");
        if (!string.IsNullOrEmpty(extra)) sb.Append(extra).Append("\r\n");
        sb.Append("Content-Length: 0\r\n\r\n");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), ct);
        await stream.FlushAsync(ct);
    }
}
