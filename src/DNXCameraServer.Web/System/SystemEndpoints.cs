using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Configuration;
using DNXCameraServer.Web.Contracts;
using DNXCameraServer.Web.Go2Rtc;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.AppSystem;

public static class SystemEndpoints
{
    private static readonly HashSet<string> WritableSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "cpu.mode", "release.minutes", "producer.idle.seconds", "sso.enabled", "rtsp.enabled"
    };

    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (DnxOptions options, Go2RtcClient client, CameraDbContext db) =>
        {
            var go2rtc = await client.PingAsync();
            var dbOk = await db.Database.CanConnectAsync();
            var disk = SafeDisk(options.DataDir);
            var status = go2rtc && dbOk ? "ok" : "degraded";

            return Results.Json(new
            {
                status,
                version = options.Version,
                app = "ok",
                db = dbOk ? "ok" : "error",
                go2rtc = go2rtc ? "ok" : "unavailable",
                disk = disk,
                at = DateTimeOffset.UtcNow
            }, statusCode: go2rtc ? 200 : 503);
        });

        app.MapGet("/readyz", async (CameraDbContext db) =>
            await db.Database.CanConnectAsync()
                ? Results.Ok(new { ready = true })
                : Results.StatusCode(503));

        var settings = app.MapGroup("/api/settings").RequireAuthorization("Admin");

        settings.MapGet("", async (CameraDbContext db) => Results.Ok(
            await db.Settings.OrderBy(s => s.Key).ToDictionaryAsync(s => s.Key, s => s.Value)));

        settings.MapPut("", async (
            SettingsRequest request, CameraDbContext db, AuditService audit,
            IConfiguration config) =>
        {
            foreach (var (key, value) in request.Values)
            {
                if (!WritableSettings.Contains(key)) continue;

                var row = await db.Settings.FindAsync(key);
                if (row is null) db.Settings.Add(new Setting { Key = key, Value = value });
                else row.Value = value;
            }

            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.SettingsUpdate,
                detail: string.Join(",", request.Values.Keys));
            return Results.Ok(new { ok = true });
        });

        var auditGroup = app.MapGroup("/api/audit").RequireAuthorization("Admin");

        auditGroup.MapGet("", async (int? limit, Guid? cameraId, CameraDbContext db) =>
        {
            var take = Math.Clamp(limit ?? 100, 1, 500);
            var query = db.Audit.AsQueryable();
            if (cameraId is not null) query = query.Where(a => a.CameraId == cameraId);

            var events = await query
                .OrderByDescending(a => a.At)
                .Take(take)
                .ToListAsync();

            return Results.Ok(events);
        });

        app.MapGet("/api/status", async (
            ClaimsPrincipal user, CameraDbContext db, Go2RtcClient client, DnxOptions options) =>
        {
            var cameras = await db.Cameras.CountAsync();
            var online = 0;
            using var streams = await client.GetStreamsAsync();
            if (streams is not null)
            {
                foreach (var camera in await db.Cameras.Where(c => c.Enabled).ToListAsync())
                {
                    if (streams.RootElement.TryGetProperty(camera.StreamName, out var node) &&
                        node.TryGetProperty("producers", out var producers) &&
                        producers.GetArrayLength() > 0)
                        online++;
                }
            }

            return Results.Ok(new
            {
                options.Version,
                cpuMode = options.CpuMode,
                cameras,
                online,
                go2rtc = await client.PingAsync(),
                disk = SafeDisk(options.DataDir)
            });
        }).RequireAuthorization();
    }

    private static object SafeDisk(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "/";
            var drive = new DriveInfo(root);
            return new
            {
                totalGb = Math.Round(drive.TotalSize / 1024d / 1024 / 1024, 1),
                freeGb = Math.Round(drive.AvailableFreeSpace / 1024d / 1024 / 1024, 1)
            };
        }
        catch
        {
            return new { totalGb = 0d, freeGb = 0d };
        }
    }
}
