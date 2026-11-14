using System.Text.Json;

namespace DNXCameraServer.Web.Xiaomi;

/// <summary>
/// Traduce los errores del gateway Xiaomi (go2rtc) a algo accionable.
/// El cuerpo suele venir como "xiaomi: {json}".
/// </summary>
public static class XiaomiErrors
{
    public sealed record Parsed(int? Code, string? Description, string? Raw);

    public static Parsed Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new Parsed(null, null, null);

        var start = body.IndexOf('{');
        if (start < 0) return new Parsed(null, null, body.Trim());

        try
        {
            using var doc = JsonDocument.Parse(body[start..]);
            var root = doc.RootElement;

            int? code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var parsed)
                ? parsed
                : null;

            string? description = null;
            if (root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                description = d.GetString();
            else if (root.TryGetProperty("desc", out var desc) && desc.ValueKind == JsonValueKind.String)
                description = desc.GetString();

            return new Parsed(code, description, body.Trim());
        }
        catch (JsonException)
        {
            return new Parsed(null, null, body.Trim());
        }
    }

    public static string FriendlyMessage(Parsed parsed) => parsed.Code switch
    {
        70016 =>
            "Xiaomi rechazó la verificación de seguridad (código 70016). Suele ocurrir cuando " +
            "el login por contraseña llega desde otra red o dispositivo: entra en " +
            "account.xiaomi.com desde este misma red, completa la verificación y reinténtalo; " +
            "si persiste, añade la cuenta con token.",
        70022 => "Usuario o contraseña incorrectos (código 70022).",
        70024 => "Demasiados intentos: Xiaomi pide captcha. Espera unos minutos y reinténtalo.",
        _ => parsed.Description is { Length: > 0 }
            ? $"Xiaomi devolvió un error: {parsed.Description}" +
              (parsed.Code is { } code ? $" (código {code})" : "")
            : "No se pudo autenticar con Xiaomi."
    };

    /// <summary>Mensaje corto para mostrar junto al error, sin duplicar el JSON.</summary>
    public static string ShortDetail(Parsed parsed) =>
        parsed.Code is { } code ? $"code={code}" : "sin código";
}
