using System.Security.Cryptography;
using System.Text;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Configuration;
using DNXCameraServer.Web.Contracts;
using DNXCameraServer.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Devices;

public static class DeviceTokenEndpoints
{
    public static void MapDeviceTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/device-tokens").RequireAuthorization("Admin");

        group.MapGet("", async (CameraDbContext db) =>
        {
            var tokens = await db.DeviceTokens
                .Include(t => t.Camera)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return Results.Ok(tokens.Select(t => new
            {
                t.Id,
                t.TokenId,
                t.Name,
                Camera = t.Camera?.Name,
                CameraId = t.CameraId,
                t.CreatedAt,
                t.ExpiresAt,
                t.RevokedAt,
                t.LastUsedAt,
                t.LastIp
            }));
        });

        group.MapPost("", async (
            TokenCreateRequest request, CameraDbContext db, DnxOptions options,
            PasswordService passwords, HttpRequest http, AuditService audit) =>
        {
            var camera = await db.Cameras.FindAsync(request.CameraId);
            if (camera is null) return Results.BadRequest(new { error = "camera_not_found" });

            var name = string.IsNullOrWhiteSpace(request.Name) ? camera.Name : request.Name.Trim();
            var tokenId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var secret = Base64Url(RandomNumberGenerator.GetBytes(24));

            var token = new DeviceToken
            {
                CameraId = camera.Id,
                TokenId = tokenId,
                Name = name,
                SecretHash = passwords.Hash(secret),
                DigestHa1 = DigestAuth.Ha1(tokenId, options.Realm, secret),
                ExpiresAt = request.ExpiresInDays is > 0
                    ? DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays.Value)
                    : null
            };

            db.DeviceTokens.Add(token);
            await db.SaveChangesAsync();

            var host = http.Host.Host;
            var url = $"rtsp://{tokenId}:{secret}@{host}:{options.RtspListenPort}/{camera.StreamName}";

            await audit.WriteAsync(AuditActions.TokenCreate, cameraId: camera.Id, detail: tokenId);

            // El secreto solo se devuelve aquí; no se persiste en claro.
            return Results.Ok(new { token.Id, token.TokenId, secret, name, camera = camera.Name, rtspUrl = url });
        });

        group.MapDelete("/{id:guid}", async (
            Guid id, CameraDbContext db, AuditService audit) =>
        {
            var token = await db.DeviceTokens.FindAsync(id);
            if (token is null) return Results.NotFound();

            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.TokenRevoke, cameraId: token.CameraId, detail: token.TokenId);
            return Results.Ok(new { ok = true });
        });
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
