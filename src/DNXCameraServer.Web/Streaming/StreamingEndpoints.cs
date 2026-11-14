using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Go2Rtc;

namespace DNXCameraServer.Web.Streaming;

public static class StreamingEndpoints
{
    public static void MapStreamingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cameras").RequireAuthorization();

        group.MapGet("/{id:guid}/snapshot", async (
            Guid id, int? w, ClaimsPrincipal user, CameraDbContext db,
            CameraAccess access, Go2RtcClient client) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();

            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            var bytes = await client.SnapshotAsync(camera.StreamName, w);
            if (bytes is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.File(bytes, "image/jpeg");
        });

        group.MapPost("/{id:guid}/webrtc/offer", async (
            Guid id, HttpRequest request, ClaimsPrincipal user, CameraDbContext db,
            CameraAccess access, Go2RtcClient client, AuditService audit) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();

            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            using var reader = new StreamReader(request.Body);
            var sdp = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(sdp))
                return Results.BadRequest(new { error = "empty_sdp" });

            using var upstream = await client.WebrtcOfferAsync(camera.StreamName, sdp);
            var body = await upstream.Content.ReadAsStringAsync();
            var contentType = upstream.Content.Headers.ContentType?.MediaType ?? "application/sdp";

            if (upstream.IsSuccessStatusCode)
                await audit.WriteAsync(AuditActions.CameraView, user, cameraId: id, detail: "webrtc");

            return Results.Content(body, contentType, statusCode: (int)upstream.StatusCode);
        });

        group.MapGet("/{id:guid}/mjpeg", async (
            Guid id, ClaimsPrincipal user, CameraDbContext db, CameraAccess access,
            Go2RtcClient client, HttpContext http, AuditService audit) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();

            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            using var upstream = await client.MjpegAsync(camera.StreamName, http.RequestAborted);
            if (!upstream.IsSuccessStatusCode)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            http.Response.ContentType =
                upstream.Content.Headers.ContentType?.MediaType ?? "multipart/x-mixed-replace";
            http.Response.Headers.CacheControl = "no-store";

            await audit.WriteAsync(AuditActions.CameraView, user, cameraId: id, detail: "mjpeg");
            try
            {
                await upstream.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // El cliente cerró la pestaña.
            }
            return Results.Empty;
        });

        group.MapGet("/{id:guid}/hls.m3u8", async (
            Guid id, ClaimsPrincipal user, CameraDbContext db, CameraAccess access,
            Go2RtcClient client) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();

            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            using var upstream = await client.M3u8Async(camera.StreamName);
            if (!upstream.IsSuccessStatusCode)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var playlist = await upstream.Content.ReadAsStringAsync();
            var prefix = $"/api/cameras/{id}/hls/";
            playlist = playlist.Replace("/api/hls/", prefix);
            if (!playlist.Contains(prefix, StringComparison.Ordinal))
                playlist = playlist.Replace("hls/", prefix);

            return Results.Content(playlist, "application/vnd.apple.mpegurl");
        });

        group.MapGet("/{id:guid}/hls/{**path}", async (
            Guid id, string path, HttpRequest http, ClaimsPrincipal user, CameraDbContext db,
            CameraAccess access, Go2RtcClient client) =>
        {
            if (!await access.CanAsync(user, id, "view")) return Results.Forbid();

            var camera = await db.Cameras.FindAsync(id);
            if (camera is null) return Results.NotFound();

            // Conserva la query (los segmentos de go2rtc llevan ?id=...).
            var relative = path + http.QueryString.Value;
            using var upstream = await client.HlsAsync(relative);
            if (!upstream.IsSuccessStatusCode)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var bytes = await upstream.Content.ReadAsByteArrayAsync();
            var contentType = upstream.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return Results.File(bytes, contentType);
        });
    }
}
