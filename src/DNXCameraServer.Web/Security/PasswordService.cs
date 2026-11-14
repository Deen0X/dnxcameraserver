using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DNXCameraServer.Domain;
using Microsoft.AspNetCore.Identity;

namespace DNXCameraServer.Web.Security;

public sealed class PasswordService(IPasswordHasher<AppUser> hasher)
{
    private static readonly AppUser Dummy = new();

    public string Hash(string password) => hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password) =>
        hasher.VerifyHashedPassword(Dummy, hash, password) != PasswordVerificationResult.Failed;
}

public static class DigestAuth
{
    public static string Md5Hex(string value) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string Ha1(string user, string realm, string password) => Md5Hex($"{user}:{realm}:{password}");

    /// <summary>Nonce sin estado: ts:hmac(ts). Caduca a los 5 minutos.</summary>
    public static string CreateNonce(string key)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"{ts}:{Sign(ts, key)}";
    }

    public static bool ValidateNonce(string nonce, string key, TimeSpan maxAge)
    {
        var parts = nonce.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ts))
            return false;
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[1]),
                Encoding.UTF8.GetBytes(Sign(parts[0], key))))
            return false;
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts <= (long)maxAge.TotalSeconds;
    }

    private static string Sign(string value, string key) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    public static Dictionary<string, string> ParseParameters(string header)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var idx = header.IndexOf(' ');
        if (idx < 0) return result;
        var body = header[(idx + 1)..];

        foreach (var raw in SplitParameters(body))
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            var name = raw[..eq].Trim();
            var value = raw[(eq + 1)..].Trim().Trim('"');
            result[name] = value;
        }
        return result;
    }

    private static IEnumerable<string> SplitParameters(string body)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in body)
        {
            if (c == '"') inQuotes = !inQuotes;
            if (c == ',' && !inQuotes)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    /// <summary>Verifica la respuesta Digest (qop=auth o sin qop).</summary>
    public static bool VerifyResponse(
        Dictionary<string, string> p, string method, string expectedHa1, string key)
    {
        if (!p.TryGetValue("username", out var user) ||
            !p.TryGetValue("realm", out var realm) ||
            !p.TryGetValue("nonce", out var nonce) ||
            !p.TryGetValue("uri", out var uri) ||
            !p.TryGetValue("response", out var response))
            return false;

        if (!ValidateNonce(nonce, key, TimeSpan.FromMinutes(5)))
            return false;

        var ha2 = Md5Hex($"{method}:{uri}");
        string expected;

        if (p.TryGetValue("qop", out var qop) && qop.Equals("auth", StringComparison.OrdinalIgnoreCase))
        {
            if (!p.TryGetValue("nc", out var nc) || !p.TryGetValue("cnonce", out var cnonce))
                return false;
            expected = Md5Hex($"{expectedHa1}:{nonce}:{nc}:{cnonce}:{qop}:{ha2}");
        }
        else
        {
            expected = Md5Hex($"{expectedHa1}:{nonce}:{ha2}");
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(response.ToLowerInvariant()));
    }

    public static string Challenge(string realm, string nonce) =>
        $"Digest realm=\"{realm}\", qop=\"auth\", nonce=\"{nonce}\", algorithm=MD5";
}

public static class UserClaims
{
    public const string UserId = "uid";

    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(UserId)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static string? GetUsername(this ClaimsPrincipal principal) =>
        principal.Identity?.Name;

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.IsInRole(Roles.Admin);
}
