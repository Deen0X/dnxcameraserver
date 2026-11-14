using System.Net;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Security;

/// <summary>
/// Autenticación SSO por cabecera (ADR-016): si la petición viene del proxy de
/// confianza y trae la cabecera, se firma sesión; si no, se continúa y la app
/// muestra su login local.
/// </summary>
public sealed class SsoMiddleware(RequestDelegate next, ILogger<SsoMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context, DnxOptions options, CameraDbContext db)
    {
        if (options.SsoEnabled &&
            context.User.Identity?.IsAuthenticated != true &&
            IsTrustedProxy(context, options) &&
            context.Request.Headers.TryGetValue(options.SsoHeader, out var header))
        {
            var username = header.ToString().Trim();
            if (username.Length is > 0 and <= 64)
            {
                var user = await db.Users.FirstOrDefaultAsync(x => x.Username == username);
                if (user is { IsActive: true })
                {
                    await context.SignInAsync(
                        Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                        AuthClaims.Build(user, "sso"));
                    logger.LogInformation("Sesión SSO para {User} desde {Ip}", username, context.Connection.RemoteIpAddress);
                }
                else
                {
                    logger.LogWarning("SSO rechazado: {User} no existe o está inactivo.", username);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "sso_user_not_provisioned",
                        message = "El usuario autenticado por el proxy no existe en DNXCameraServer."
                    });
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool IsTrustedProxy(HttpContext context, DnxOptions options)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return false;

        foreach (var entry in options.TrustedProxies)
        {
            if (IPAddress.TryParse(entry, out var ip) && ip.Equals(remote)) return true;
            if (entry.Contains('/') && IsInCidr(remote, entry)) return true;
        }
        return false;
    }

    private static bool IsInCidr(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) ||
            !int.TryParse(parts[1], out var prefix)) return false;

        var addrBytes = address.MapToIPv4().GetAddressBytes();
        var netBytes = network.MapToIPv4().GetAddressBytes();
        if (addrBytes.Length != netBytes.Length) return false;

        var fullBytes = prefix / 8;
        var remainder = prefix % 8;

        for (var i = 0; i < fullBytes; i++)
            if (addrBytes[i] != netBytes[i]) return false;

        if (remainder == 0) return true;
        var mask = (byte)(0xFF << (8 - remainder));
        return (addrBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
    }
}

public static class AuthClaims
{
    public static System.Security.Claims.ClaimsPrincipal Build(AppUser user, string origin)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, user.Username),
            new(System.Security.Claims.ClaimTypes.Role, user.Role),
            new(UserClaims.UserId, user.Id.ToString()),
            new("origin", origin)
        };
        return new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims,
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
