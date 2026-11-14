using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Contracts;

namespace DNXCameraServer.Web.Setup;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/setup/status", async (AdminSetupService setup) =>
            Results.Ok(new { needsSetup = await setup.NeedsSetupAsync() }));

        // Solo funciona mientras no exista ningún administrador.
        app.MapPost("/api/setup/admin", async (
            SetupRequest request, AdminSetupService setup, AuditService audit,
            ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("Setup");

            if (!await setup.NeedsSetupAsync())
                return Results.Conflict(new { error = "setup_completed" });

            var (ok, error) = await setup.CreateAdminAsync(request.Username, request.Password);
            if (!ok) return Results.BadRequest(new { error });

            var username = request.Username.Trim();
            await audit.WriteAsync(AuditActions.UserCreate, actorId: username,
                detail: "primer administrador (instalación)");
            log.LogWarning("Primer administrador creado: {User}", username);

            return Results.Ok(new { ok = true, username, role = Roles.Admin });
        }).RequireRateLimiting("login");
    }
}
