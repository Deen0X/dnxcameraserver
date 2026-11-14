using System.Text.Json;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Go2Rtc;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Workers;

/// <summary>
/// Sincroniza el estado de las cámaras con go2rtc y actualiza LastSeenAt.
/// go2rtc ya detiene el productor cuando no quedan consumidores; este worker
/// solo refleja ese estado en la base de datos (F1-14).
/// </summary>
public sealed class StatusSyncWorker(
    IServiceScopeFactory scopes,
    Go2RtcClient client,
    ILogger<StatusSyncWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Sincronización de estado fallida.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        using var doc = await client.GetStreamsAsync(ct);
        if (doc is null) return;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CameraDbContext>();
        var cameras = await db.Cameras.Where(c => c.Enabled).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        foreach (var camera in cameras)
        {
            var live = doc.RootElement.TryGetProperty(camera.StreamName, out var node) &&
                       node.TryGetProperty("producers", out var producers) &&
                       producers.ValueKind == JsonValueKind.Array &&
                       producers.GetArrayLength() > 0;

            string state;
            if (camera.ReleasedUntil is { } until && until > now)
                state = CameraStates.Released;
            else if (live)
                state = CameraStates.Live;
            else if (camera.State is CameraStates.Error)
                state = CameraStates.Error;
            else
                state = CameraStates.Idle;

            if (camera.State != state)
            {
                camera.State = state;
                changed = true;
            }

            if (live)
            {
                camera.LastSeenAt = now;
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync(ct);
    }
}
