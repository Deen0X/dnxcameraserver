using System.Text.Json;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Cameras;
using DNXCameraServer.Web.Contracts;
using DNXCameraServer.Web.Go2Rtc;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Xiaomi;

public static class XiaomiEndpoints
{
    public static void MapXiaomiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/xiaomi").RequireAuthorization("Admin");

        // Login con contraseña, captcha o verificación 2FA (proxya a go2rtc).
        group.MapPost("/login", async (
            XiaomiLoginRequest request,
            Go2RtcClient client,
            CameraDbContext db,
            AuditService audit,
            XiaomiLoginGate gate,
            ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("Xiaomi");
            var form = new List<KeyValuePair<string, string>>();

            void Add(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) form.Add(new(key, value));
            }

            Add("username", request.Username);
            Add("password", request.Password);
            Add("captcha", request.Captcha);
            Add("verify", request.Verify);

            if (form.Count == 0)
                return Results.BadRequest(new { error = "empty_request" });

            string body;
            int statusCode;

            // go2rtc usa estado global sin sincronizar en su módulo Xiaomi: dos
            // logins concurrentes pueden tumbarlo ("concurrent map read and map write").
            using (await gate.AcquireAsync())
            {
                using var res = await client.XiaomiLoginAsync(form);
                statusCode = (int)res.StatusCode;
                body = await res.Content.ReadAsStringAsync();

                if (statusCode == StatusCodes.Status401Unauthorized)
                    return Results.Content(body, "application/json", statusCode: statusCode);

                if (statusCode is < 200 or > 299)
                {
                    var parsed = XiaomiErrors.Parse(body);
                    log.LogWarning("Login Xiaomi falló ({Status}, code={Code}): {Body}",
                        statusCode, parsed.Code, parsed.Raw);

                    return Results.Json(new
                    {
                        error = "xiaomi_login_failed",
                        code = parsed.Code,
                        message = XiaomiErrors.FriendlyMessage(parsed),
                        detail = parsed.Raw
                    }, statusCode: StatusCodes.Status502BadGateway);
                }

                using var accountsRes = await client.XiaomiAccountsAsync();
                var ids = JsonSerializer.Deserialize<List<string>>(await accountsRes.Content.ReadAsStringAsync())
                          ?? [];

                var region = Regions.Normalize(request.Region);
                foreach (var id in ids)
                {
                    if (!await db.Accounts.AnyAsync(a => a.XiaomiUserId == id))
                        db.Accounts.Add(new XiaomiAccount { XiaomiUserId = id, Region = region });
                }
                await db.SaveChangesAsync();

                await audit.WriteAsync(AuditActions.AccountCreate, detail: $"login ok; cuentas={ids.Count}");
                log.LogInformation("Login Xiaomi correcto. Cuentas en go2rtc: {Count}", ids.Count);

                return Results.Ok(new
                {
                    ok = true,
                    accounts = await db.Accounts.OrderBy(a => a.CreatedAt).ToListAsync()
                });
            }
        });

        // Alta directa con passToken: evita el login por contraseña cuando Xiaomi
        // lo bloquea (código 70016). El token lo custodia go2rtc (ADR-011).
        group.MapPost("/token", async (
            XiaomiTokenRequest request,
            Go2RtcClient client,
            CameraDbContext db,
            AuditService audit,
            XiaomiLoginGate gate,
            ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("Xiaomi");
            var userId = (request.UserId ?? "").Trim();
            var token = (request.Token ?? "").Trim();
            var region = Regions.Normalize(request.Region);

            if (userId.Length < 5 || !userId.All(char.IsAsciiDigit))
                return Results.BadRequest(new { error = "invalid_user_id", message = "El userId de Xiaomi es numérico (aparece en la cookie 'userId')." });
            if (token.Length < 16)
                return Results.BadRequest(new { error = "invalid_token", message = "El passToken no parece válido (cookie 'passToken' de account.xiaomi.com)." });

            var escaped = token.Replace("\\", "\\\\").Replace("\"", "\\\"");

            using (await gate.AcquireAsync())
            {
                await client.PatchConfigAsync($"xiaomi:\n  \"{userId}\": \"{escaped}\"\n");

                // go2rtc solo lee los tokens al arrancar: hay que reiniciarlo para
                // que LoginWithToken use el que acabamos de escribir.
                await client.RestartAsync();

                using var probe = await client.XiaomiDevicesAsync(userId, region);
                var body = await probe.Content.ReadAsStringAsync();

                if (!probe.IsSuccessStatusCode)
                {
                    // El token no sirve: se retira para no dejar basura en la config.
                    try
                    {
                        await client.PatchConfigAsync($"xiaomi:\n  \"{userId}\": null\n");
                        await client.RestartAsync();
                    }
                    catch (Exception ex)
                    {
                        log.LogDebug(ex, "No se pudo limpiar el token rechazado.");
                    }

                    var parsed = XiaomiErrors.Parse(body);
                    log.LogWarning(
                        "Token Xiaomi rechazado ({Status}, code={Code}): {Body}",
                        (int)probe.StatusCode, parsed.Code, body);

                    return Results.Json(new
                    {
                        error = "token_rejected",
                        code = parsed.Code,
                        message = "Xiaomi no aceptó el token. Comprueba que 'userId' y 'passToken' son de " +
                                  "una sesión iniciada en account.xiaomi.com desde esta misma red, que la " +
                                  "región es la correcta y que la cuenta no está bloqueada por seguridad.",
                        detail = parsed.Raw ?? body
                    }, statusCode: StatusCodes.Status400BadRequest);
                }
            }

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.XiaomiUserId == userId);
            if (account is null)
            {
                account = new XiaomiAccount
                {
                    XiaomiUserId = userId,
                    Region = region,
                    Alias = string.IsNullOrWhiteSpace(request.Alias) ? null : request.Alias.Trim()
                };
                db.Accounts.Add(account);
            }
            else
            {
                account.Region = region;
                if (!string.IsNullOrWhiteSpace(request.Alias)) account.Alias = request.Alias.Trim();
            }

            await db.SaveChangesAsync();
            await audit.WriteAsync(AuditActions.AccountCreate, detail: $"token; userId={userId}");

            return Results.Ok(new { ok = true, account = new { account.Id, account.XiaomiUserId, account.Region, account.Alias } });
        });

        group.MapGet("/accounts", async (CameraDbContext db) =>
            Results.Ok(await db.Accounts.OrderBy(a => a.CreatedAt).ToListAsync()));

        group.MapPatch("/accounts/{id:guid}", async (
            Guid id, AccountUpdateRequest request, CameraDbContext db) =>
        {
            var account = await db.Accounts.FindAsync(id);
            if (account is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(request.Region)) account.Region = Regions.Normalize(request.Region);
            if (request.Alias is not null) account.Alias = request.Alias;
            await db.SaveChangesAsync();
            return Results.Ok(account);
        });

        group.MapDelete("/accounts/{id:guid}", async (
            Guid id, CameraDbContext db, StreamManager streams, AuditService audit) =>
        {
            var account = await db.Accounts.FindAsync(id);
            if (account is null) return Results.NotFound();

            var cameras = await db.Cameras.Where(c => c.AccountId == id).ToListAsync();
            foreach (var camera in cameras)
                await streams.RemoveAsync(camera);

            db.Cameras.RemoveRange(cameras);
            db.Accounts.Remove(account);
            await db.SaveChangesAsync();

            await audit.WriteAsync(AuditActions.AccountDelete, detail: account.XiaomiUserId);
            return Results.Ok(new { ok = true, removedCameras = cameras.Count });
        });

        // Listado de dispositivos de la cuenta (pass-through de go2rtc).
        group.MapGet("/devices", async (Guid accountId, CameraDbContext db, Go2RtcClient client) =>
        {
            var account = await db.Accounts.FindAsync(accountId);
            if (account is null) return Results.NotFound(new { error = "account_not_found" });

            using var res = await client.XiaomiDevicesAsync(account.XiaomiUserId, account.Region);
            var body = await res.Content.ReadAsStringAsync();
            return Results.Content(body,
                res.Content.Headers.ContentType?.MediaType ?? "application/json",
                statusCode: (int)res.StatusCode);
        });
    }
}
