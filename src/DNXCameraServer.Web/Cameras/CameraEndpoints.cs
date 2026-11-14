using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Cameras;
using DNXCameraServer.Web.Contracts;
using DNXCameraServer.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Cameras;

public static class CameraMapper
{
    public static CameraDto ToDto(Camera camera, CapabilityService capabilities) =>
        new(
            camera.Id,
            camera.Name,
            camera.Model,
            camera.Ip,
            camera.Protocol,
            camera.StreamName,
            camera.Profile,
            camera.State,
            camera.Enabled,
            camera.AccountId,
            camera.Account?.Alias,
            camera.Account?.Region ?? "",
            camera.LastSeenAt,
            camera.ReleasedUntil,
            capabilities.Resolve(camera.Model, camera.CapabilitiesOverrideJson));
}

public static class CameraEndpoints
{
    public static void MapCameraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cameras").RequireAuthorization();

        group.MapGet("", async (
            ClaimsPrincipal user, CameraDbContext db, CameraAccess access,
            CapabilityService capabilities) =>
        {
            var query = db.Cameras.Include(c => c.Account).AsQueryable();

            if (!user.IsAdmin())
            {
                var ids = await access.VisibleCameraIdsAsync(user);
                query = query.Where(c => ids.Contains(c.Id));
            }

            var cameras = await query.OrderBy(c => c.Name).ToListAsync();
            return Results.Ok(cameras.Select(c => CameraMapper.ToDto(c, capabilities)));
        });

        group.MapGet("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, CameraDbContext db, CameraAccess access,
            CapabilityService capabilities) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();

            var camera = await db.Cameras.Include(c => c.Account).FirstOrDefaultAsync(c => c.Id == id);
            return camera is null ? Results.NotFound() : Results.Ok(CameraMapper.ToDto(camera, capabilities));
        });

        group.MapGet("/{id:guid}/capabilities", async (
            Guid id, ClaimsPrincipal user, CameraDbContext db, CameraAccess access,
            CapabilityService capabilities) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();
            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();
            return Results.Ok(capabilities.Resolve(camera.Model, camera.CapabilitiesOverrideJson));
        });

        group.MapPost("", async (
            CameraImportRequest request, CameraDbContext db, StreamManager streams,
            CapabilityService capabilities, AuditService audit, ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("Cameras");

            if (string.IsNullOrWhiteSpace(request.Did) || string.IsNullOrWhiteSpace(request.Model) ||
                string.IsNullOrWhiteSpace(request.Ip))
                return Results.BadRequest(new { error = "missing_fields" });

            var account = await db.Accounts.FindAsync(request.AccountId);
            if (account is null) return Results.BadRequest(new { error = "account_not_found" });

            var duplicate = await db.Cameras.AnyAsync(c =>
                c.AccountId == request.AccountId && c.Did == request.Did && c.Channel == request.Channel);
            if (duplicate) return Results.Conflict(new { error = "camera_already_imported" });

            var camera = new Camera
            {
                AccountId = request.AccountId,
                Did = request.Did.Trim(),
                Model = request.Model.Trim(),
                Name = string.IsNullOrWhiteSpace(request.Name) ? request.Model : request.Name.Trim(),
                Ip = request.Ip.Trim(),
                Channel = request.Channel,
                StreamName = StreamManager.NewStreamName(),
                Protocol = capabilities.ProtocolFor(request.Model),
                Profile = StreamProfiles.Hd,
                Enabled = true,
                State = CameraStates.Idle
            };

            db.Cameras.Add(camera);
            await db.SaveChangesAsync();

            camera.Account = account;
            await streams.EnsureAsync(camera);
            await audit.WriteAsync(AuditActions.CameraCreate, cameraId: camera.Id, detail: camera.Name);
            log.LogInformation("Cámara importada: {Name} ({Model})", camera.Name, camera.Model);

            return Results.Ok(CameraMapper.ToDto(camera, capabilities));
        }).RequireAuthorization("Admin");

        group.MapPatch("/{id:guid}", async (
            Guid id, CameraUpdateRequest request, CameraDbContext db, StreamManager streams,
            CapabilityService capabilities, AuditService audit) =>
        {
            var camera = await db.Cameras.Include(c => c.Account).FirstOrDefaultAsync(c => c.Id == id);
            if (camera is null) return Results.NotFound();

            var reopen = false;

            if (!string.IsNullOrWhiteSpace(request.Name)) camera.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.Ip) && request.Ip != camera.Ip)
            {
                camera.Ip = request.Ip.Trim();
                reopen = true;
            }
            if (!string.IsNullOrWhiteSpace(request.Profile) && request.Profile != camera.Profile)
            {
                if (!StreamProfiles.IsValid(request.Profile))
                    return Results.BadRequest(new { error = "invalid_profile" });
                camera.Profile = request.Profile;
                reopen = true;
            }
            if (request.Transcode is not null) camera.Transcode = request.Transcode;
            if (request.CapabilitiesOverrideJson is not null)
                camera.CapabilitiesOverrideJson = string.IsNullOrWhiteSpace(request.CapabilitiesOverrideJson)
                    ? null : request.CapabilitiesOverrideJson;
            if (request.Enabled is { } enabled && enabled != camera.Enabled)
            {
                camera.Enabled = enabled;
                reopen = true;
            }

            await db.SaveChangesAsync();

            if (reopen)
            {
                if (camera.Enabled) await streams.EnsureAsync(camera);
                else await streams.RemoveAsync(camera);
            }

            await audit.WriteAsync(AuditActions.CameraUpdate, cameraId: camera.Id, detail: camera.Name);
            return Results.Ok(CameraMapper.ToDto(camera, capabilities));
        }).RequireAuthorization("Admin");

        group.MapDelete("/{id:guid}", async (
            Guid id, CameraDbContext db, StreamManager streams, AuditService audit) =>
        {
            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            await streams.RemoveAsync(camera);
            db.Cameras.Remove(camera);
            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.CameraDelete, cameraId: id, detail: camera.Name);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("Admin");

        group.MapPost("/{id:guid}/profile", async (
            Guid id, ProfileRequest request, CameraDbContext db, StreamManager streams,
            CapabilityService capabilities, AuditService audit) =>
        {
            var camera = await db.Cameras.Include(c => c.Account).FirstOrDefaultAsync(c => c.Id == id);
            if (camera is null) return Results.NotFound();

            await streams.SetProfileAsync(camera, request.Profile);
            await audit.WriteAsync(AuditActions.CameraUpdate, cameraId: id, detail: $"profile={request.Profile}");
            return Results.Ok(CameraMapper.ToDto(camera, capabilities));
        }).RequireAuthorization("Admin");

        group.MapPost("/{id:guid}/release", async (
            Guid id, ReleaseRequest request, CameraDbContext db, StreamManager streams,
            AuditService audit) =>
        {
            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            var minutes = request.Minutes is > 0 and <= 240 ? request.Minutes!.Value : 10;
            await streams.ReleaseAsync(camera, minutes);
            await audit.WriteAsync(AuditActions.CameraRelease, cameraId: id, detail: $"{minutes} min");
            return Results.Ok(new { ok = true, until = camera.ReleasedUntil });
        }).RequireAuthorization("Admin");

        group.MapPost("/{id:guid}/reconnect", async (
            Guid id, CameraDbContext db, StreamManager streams, AuditService audit) =>
        {
            var camera = await db.Cameras.Include(c => c.Account).FirstOrDefaultAsync(c => c.Id == id);
            if (camera is null) return Results.NotFound();

            await streams.ReconnectAsync(camera);
            await audit.WriteAsync(AuditActions.CameraReconnect, cameraId: id);
            return Results.Ok(new { ok = true, state = camera.State });
        }).RequireAuthorization("Admin");
    }
}
