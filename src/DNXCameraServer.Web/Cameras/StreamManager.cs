using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Configuration;
using DNXCameraServer.Web.Go2Rtc;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Cameras;

/// <summary>
/// Ciclo de vida de los streams en go2rtc. La BD es la fuente de verdad; go2rtc
/// es el plano de medios y se reconcilia a partir de ella.
/// </summary>
public sealed class StreamManager(
    CameraDbContext db,
    Go2RtcClient client,
    CapabilityService capabilities,
    ILogger<StreamManager> log)
{
    public static string NewStreamName() =>
        "cam_" + Convert.ToHexString(Guid.NewGuid().ToByteArray())[..12].ToLowerInvariant();

    public string BuildSource(Camera camera, XiaomiAccount account)
    {
        var caps = capabilities.Resolve(camera.Model, camera.CapabilitiesOverrideJson);
        var q = new List<string>
        {
            $"did={Uri.EscapeDataString(camera.Did)}",
            $"model={Uri.EscapeDataString(camera.Model)}",
            $"subtype={Uri.EscapeDataString(camera.Profile)}"
        };

        if (camera.Channel > 0)
            q.Add($"channel={camera.Channel}");
        if (!caps.Audio.Out)
            q.Add("audio=0");

        return $"xiaomi://{Uri.EscapeDataString(account.XiaomiUserId)}:{Uri.EscapeDataString(account.Region)}" +
               $"@{camera.Ip}?{string.Join('&', q)}";
    }

    public async Task EnsureAsync(Camera camera, CancellationToken ct = default)
    {
        if (!camera.Enabled)
        {
            await RemoveAsync(camera, ct);
            return;
        }

        var account = camera.Account ?? await db.Accounts.FindAsync([camera.AccountId], ct);
        if (account is null)
        {
            log.LogWarning("Cámara {Name} sin cuenta asociada.", camera.Name);
            camera.State = CameraStates.Error;
            return;
        }

        var source = BuildSource(camera, account);
        try
        {
            camera.State = CameraStates.Connecting;
            await client.PutStreamAsync(camera.StreamName, source, ct);
            camera.State = CameraStates.Idle;
            log.LogInformation("Stream {Stream} registrado en go2rtc.", camera.StreamName);
        }
        catch (Exception ex)
        {
            camera.State = CameraStates.Error;
            log.LogWarning(ex, "No se pudo registrar {Stream}.", camera.StreamName);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Camera camera, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(camera.StreamName)) return;
        try
        {
            await client.DeleteStreamAsync(camera.StreamName, ct);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Borrado de stream {Stream} falló.", camera.StreamName);
        }
    }

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var cameras = await db.Cameras
            .Include(c => c.Account)
            .Where(c => c.Enabled)
            .ToListAsync(ct);

        foreach (var camera in cameras)
        {
            var released = camera.ReleasedUntil is { } until && until > DateTimeOffset.UtcNow;
            if (released)
            {
                camera.State = CameraStates.Released;
                await RemoveAsync(camera, ct);
                continue;
            }

            if (camera.ReleasedUntil is not null)
                camera.ReleasedUntil = null;

            await EnsureAsync(camera, ct);
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Reconciliación completada: {Count} cámaras habilitadas.", cameras.Count);
    }

    public async Task ReleaseAsync(Camera camera, int minutes, CancellationToken ct = default)
    {
        camera.ReleasedUntil = DateTimeOffset.UtcNow.AddMinutes(minutes);
        camera.State = CameraStates.Released;
        await RemoveAsync(camera, ct);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Cámara {Name} liberada {Minutes} min.", camera.Name, minutes);
    }

    public async Task ReconnectAsync(Camera camera, CancellationToken ct = default)
    {
        camera.ReleasedUntil = null;
        await EnsureAsync(camera, ct);
    }

    /// <summary>Cambia el perfil de vídeo: reabre el productor (corte 2-5 s).</summary>
    public async Task SetProfileAsync(Camera camera, string profile, CancellationToken ct = default)
    {
        if (!StreamProfiles.IsValid(profile))
            throw new ArgumentException("Perfil inválido.", nameof(profile));

        camera.Profile = profile;
        await EnsureAsync(camera, ct);
    }
}
