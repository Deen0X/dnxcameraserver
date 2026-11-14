using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Contracts;
using DNXCameraServer.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization("Admin");

        group.MapGet("", async (CameraDbContext db) =>
        {
            var users = await db.Users.OrderBy(u => u.Username).ToListAsync();
            return Results.Ok(users.Select(u => new
            {
                u.Id,
                u.Username,
                u.Role,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt
            }));
        });

        group.MapPost("", async (
            UserCreateRequest request, CameraDbContext db, PasswordService passwords,
            AuditService audit) =>
        {
            var username = request.Username.Trim();
            if (username.Length is < 3 or > 64)
                return Results.BadRequest(new { error = "invalid_username" });
            if (request.Password.Length < 8)
                return Results.BadRequest(new { error = "weak_password" });
            if (request.Role is not (Roles.Admin or Roles.Viewer))
                return Results.BadRequest(new { error = "invalid_role" });
            if (await db.Users.AnyAsync(u => u.Username == username))
                return Results.Conflict(new { error = "username_taken" });

            var user = new AppUser
            {
                Username = username,
                Role = request.Role,
                PasswordHash = passwords.Hash(request.Password)
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.UserCreate, detail: $"{username} ({request.Role})");
            return Results.Ok(new { user.Id, user.Username, user.Role, user.IsActive });
        });

        group.MapPatch("/{id:guid}", async (
            Guid id, UserUpdateRequest request, CameraDbContext db, PasswordService passwords,
            AuditService audit) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                if (request.Password.Length < 8)
                    return Results.BadRequest(new { error = "weak_password" });
                user.PasswordHash = passwords.Hash(request.Password);
            }

            if (request.Role is not null)
            {
                if (request.Role is not (Roles.Admin or Roles.Viewer))
                    return Results.BadRequest(new { error = "invalid_role" });
                user.Role = request.Role;
            }

            if (request.IsActive is { } active) user.IsActive = active;

            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.UserUpdate, detail: user.Username);
            return Results.Ok(new { user.Id, user.Username, user.Role, user.IsActive });
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal caller, CameraDbContext db, AuditService audit) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound();

            if (caller.GetUserId() == id)
                return Results.BadRequest(new { error = "cannot_delete_self" });

            if (user.Role == Roles.Admin)
            {
                var admins = await db.Users.CountAsync(u => u.Role == Roles.Admin && u.IsActive);
                if (admins <= 1) return Results.BadRequest(new { error = "last_admin" });
            }

            // Baja lógica: conserva la auditoría y los permisos.
            user.IsActive = false;
            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.UserUpdate, detail: $"desactivado: {user.Username}");
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/{id:guid}/grants", async (Guid id, CameraDbContext db) =>
        {
            var grants = await db.Grants
                .Where(g => g.UserId == id)
                .Select(g => new { g.CameraId, g.CanView, g.CanListen, g.CanTalk })
                .ToListAsync();
            return Results.Ok(grants);
        });

        group.MapPut("/{id:guid}/grants", async (
            Guid id, List<GrantRequest> request, CameraDbContext db, AuditService audit) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == id)) return Results.NotFound();

            var existing = await db.Grants.Where(g => g.UserId == id).ToListAsync();
            db.Grants.RemoveRange(existing);

            var cameraIds = await db.Cameras.Select(c => c.Id).ToListAsync();
            foreach (var item in request)
            {
                if (!cameraIds.Contains(item.CameraId)) continue;
                db.Grants.Add(new CameraGrant
                {
                    UserId = id,
                    CameraId = item.CameraId,
                    CanView = item.CanView,
                    CanListen = item.CanListen,
                    CanTalk = item.CanTalk
                });
            }

            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.UserUpdate, detail: $"permisos de {id}");
            return Results.Ok(new { ok = true, count = request.Count });
        });
    }
}
