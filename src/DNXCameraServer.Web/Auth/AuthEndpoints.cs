using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Contracts;
using DNXCameraServer.Web.Security;
using DNXCameraServer.Web.Setup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            CameraDbContext db,
            PasswordService passwords,
            AdminSetupService setup,
            AuditService audit,
            HttpContext http,
            ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("Auth");

            if (await setup.NeedsSetupAsync())
                return Results.Json(new { error = "setup_required" },
                    statusCode: StatusCodes.Status409Conflict);

            var user = await db.Users.FirstOrDefaultAsync(x => x.Username == request.Username);

            if (user is null || !user.IsActive || !passwords.Verify(user.PasswordHash, request.Password))
            {
                await audit.WriteAsync(AuditActions.LoginFailed, actorId: request.Username,
                    detail: "credenciales inválidas");
                log.LogWarning("Login fallido para {User} desde {Ip}", request.Username,
                    http.Connection.RemoteIpAddress);
                return Results.Json(new { error = "invalid_credentials" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                AuthClaims.Build(user, "local"),
                new AuthenticationProperties { IsPersistent = false });

            await audit.WriteAsync(AuditActions.Login, actorId: user.Username);
            log.LogInformation("Login correcto de {User}", user.Username);

            return Results.Ok(new { user.Username, user.Role, origin = "local" });
        }).RequireRateLimiting("login");

        group.MapPost("/logout", async (HttpContext http, AuditService audit) =>
        {
            await audit.WriteAsync(AuditActions.Logout);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var origin = user.FindFirst("origin")?.Value ?? "local";
            return Results.Ok(new
            {
                username = user.Identity?.Name,
                role = user.IsInRole(Roles.Admin) ? Roles.Admin : Roles.Viewer,
                origin
            });
        }).RequireAuthorization();

        group.MapPost("/password", async (
            ChangePasswordRequest request, ClaimsPrincipal principal, CameraDbContext db,
            PasswordService passwords, AuditService audit) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users.FindAsync(userId.Value);
            if (user is null) return Results.Unauthorized();

            if (!passwords.Verify(user.PasswordHash, request.CurrentPassword))
                return Results.BadRequest(new { error = "wrong_current_password" });

            if (request.NewPassword.Length < 8)
                return Results.BadRequest(new { error = "weak_password" });

            user.PasswordHash = passwords.Hash(request.NewPassword);
            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.UserUpdate, detail: "cambio de contraseña propio");
            return Results.Ok(new { ok = true });
        }).RequireAuthorization();
    }
}
