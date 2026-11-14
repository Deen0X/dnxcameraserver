using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DNXCameraServer.Web.Configuration;

namespace DNXCameraServer.Web.Go2Rtc;

/// <summary>Cliente de la API local de go2rtc (solo loopback).</summary>
public sealed class Go2RtcClient
{
    private readonly HttpClient _http;
    private readonly ILogger<Go2RtcClient> _log;

    public Go2RtcClient(HttpClient http, DnxOptions options, ILogger<Go2RtcClient> log)
    {
        _http = http;
        _log = log;
        _http.BaseAddress = new Uri(options.Go2RtcApi);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await _http.GetAsync("/api", ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogDebug("go2rtc no responde: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<JsonDocument?> GetStreamsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync("/api/streams", ct);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning("No se pudo leer /api/streams: {Message}", ex.Message);
            return null;
        }
    }

    public async Task PutStreamAsync(string name, string source, CancellationToken ct = default)
    {
        var url = $"/api/streams?name={Uri.EscapeDataString(name)}&src={Uri.EscapeDataString(source)}";
        using var res = await _http.PutAsync(url, null, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task DeleteStreamAsync(string name, CancellationToken ct = default)
    {
        using var res = await _http.DeleteAsync($"/api/streams?src={Uri.EscapeDataString(name)}", ct);
        if (!res.IsSuccessStatusCode && res.StatusCode != System.Net.HttpStatusCode.NotFound)
            _log.LogWarning("Borrar stream {Name} devolvió {Status}", name, res.StatusCode);
    }

    public async Task<byte[]?> SnapshotAsync(string name, int? width, CancellationToken ct = default)
    {
        var url = $"/api/frame.jpeg?src={Uri.EscapeDataString(name)}";
        if (width is > 0) url += $"&width={width}";
        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public Task<HttpResponseMessage> WebrtcOfferAsync(string name, string sdp, CancellationToken ct = default)
    {
        var content = new StringContent(sdp, Encoding.UTF8, "application/sdp");
        return _http.PostAsync($"/api/webrtc?src={Uri.EscapeDataString(name)}", content, ct);
    }

    public Task<HttpResponseMessage> MjpegAsync(string name, CancellationToken ct = default) =>
        _http.GetAsync($"/api/stream.mjpeg?src={Uri.EscapeDataString(name)}",
            HttpCompletionOption.ResponseHeadersRead, ct);

    public Task<HttpResponseMessage> M3u8Async(string name, CancellationToken ct = default) =>
        _http.GetAsync($"/api/stream.m3u8?src={Uri.EscapeDataString(name)}", ct);

    public Task<HttpResponseMessage> HlsAsync(string relativePath, CancellationToken ct = default) =>
        _http.GetAsync($"/api/hls/{relativePath}", HttpCompletionOption.ResponseHeadersRead, ct);

    public Task<HttpResponseMessage> XiaomiLoginAsync(IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct = default) =>
        _http.PostAsync("/api/xiaomi", new FormUrlEncodedContent(form), ct);

    public Task<HttpResponseMessage> XiaomiAccountsAsync(CancellationToken ct = default) =>
        _http.GetAsync("/api/xiaomi", ct);

    public Task<HttpResponseMessage> XiaomiDevicesAsync(string userId, string region, CancellationToken ct = default) =>
        _http.GetAsync($"/api/xiaomi?id={Uri.EscapeDataString(userId)}&region={Uri.EscapeDataString(region)}", ct);

    /// <summary>
    /// Aplica un parche YAML a la config de go2rtc (se fusiona con la existente).
    /// Se usa para dar de alta cuentas por passToken.
    /// </summary>
    public async Task PatchConfigAsync(string yaml, CancellationToken ct = default)
    {
        var content = new StringContent(yaml, Encoding.UTF8, "application/yaml");
        using var res = await _http.PatchAsync("/api/config", content, ct);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Reinicia go2rtc (re-ejecuta el binario y recarga la config). Necesario tras
    /// escribir tokens: el módulo Xiaomi solo los lee al arrancar.
    /// </summary>
    public async Task RestartAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await _http.PostAsync("/api/restart", null, ct);
        }
        catch (HttpRequestException)
        {
            // El proceso se reemplaza a sí mismo: la conexión puede cortarse.
        }
        catch (TaskCanceledException)
        {
            // Idem.
        }

        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(250, ct);
            if (await PingAsync(ct)) return;
        }

        throw new InvalidOperationException("go2rtc no respondió tras reiniciarse.");
    }

    public async Task<bool> CameraOnlineAsync(string name, CancellationToken ct = default)
    {
        using var doc = await GetStreamsAsync(ct);
        if (doc is null) return false;
        if (!doc.RootElement.TryGetProperty(name, out var stream)) return false;
        if (!stream.TryGetProperty("producers", out var producers)) return false;
        return producers.GetArrayLength() > 0;
    }
}
