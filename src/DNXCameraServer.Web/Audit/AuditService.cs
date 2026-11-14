using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Audit;

public sealed class AuditService(CameraDbContext db, IHttpContextAccessor accessor)
{
    public async Task WriteAsync(
        string action,
        ClaimsPrincipal? user = null,
        string actorType = "user",
        string? actorId = null,
        Guid? cameraId = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        var entry = new AuditEvent
        {
            At = DateTimeOffset.UtcNow,
            Action = action,
            ActorType = actorType,
            ActorId = actorId ?? user?.Identity?.Name,
            CameraId = cameraId,
            Ip = http?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http?.Request.Headers.UserAgent.ToString(),
            DetailJson = detail
        };
        db.Audit.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class CameraAccess(CameraDbContext db)
{
    public async Task<bool> CanAsync(
        ClaimsPrincipal user, Guid cameraId, string permission, CancellationToken ct = default)
    {
        if (user.IsAdmin()) return true;

        var userId = user.GetUserId();
        if (userId is null) return false;

        var query = db.Grants.Where(g => g.UserId == userId && g.CameraId == cameraId);
        return permission switch
        {
            "view" => await query.AnyAsync(g => g.CanView, ct),
            "listen" => await query.AnyAsync(g => g.CanListen, ct),
            "talk" => await query.AnyAsync(g => g.CanTalk, ct),
            _ => false
        };
    }

    public async Task<List<Guid>> VisibleCameraIdsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.GetUserId();
        if (userId is null) return [];
        return await db.Grants
            .Where(g => g.UserId == userId && g.CanView)
            .Select(g => g.CameraId)
            .ToListAsync(ct);
    }
}
