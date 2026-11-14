using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Setup;

/// <summary>
/// Alta del primer administrador desde la web y reseteo por CLI.
/// El sistema está en "modo instalación" mientras no exista ningún Admin.
/// </summary>
public sealed class AdminSetupService(CameraDbContext db, PasswordService passwords)
{
    public async Task<bool> NeedsSetupAsync(CancellationToken ct = default) =>
        !await db.Users.AnyAsync(u => u.Role == Roles.Admin, ct);

    public async Task<(bool Ok, string? Error)> CreateAdminAsync(
        string? username, string? password, CancellationToken ct = default)
    {
        username = (username ?? "").Trim();
        var secret = password ?? "";

        if (username.Length is < 3 or > 64) return (false, "invalid_username");
        if (secret.Length < 8) return (false, "weak_password");
        if (await db.Users.AnyAsync(u => u.Username == username, ct)) return (false, "username_taken");

        var admin = new AppUser
        {
            Username = username,
            Role = Roles.Admin,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        admin.PasswordHash = passwords.Hash(secret);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Elimina los administradores y sus permisos. Deja visores y cámaras intactos.
    /// </summary>
    public async Task<int> ResetAdminsAsync(CancellationToken ct = default)
    {
        var admins = await db.Users.Where(u => u.Role == Roles.Admin).ToListAsync(ct);
        if (admins.Count == 0) return 0;

        var ids = admins.Select(a => a.Id).ToList();
        var grants = await db.Grants.Where(g => ids.Contains(g.UserId)).ToListAsync(ct);

        db.Grants.RemoveRange(grants);
        db.Users.RemoveRange(admins);
        await db.SaveChangesAsync(ct);
        return admins.Count;
    }
}
