using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Security;
using DNXCameraServer.Web.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Web.Startup;

public static class DbInitializer
{
    /// <summary>
    /// Aplica migraciones y siembra ajustes. No crea usuarios: el primer
    /// administrador se crea desde la web (modo instalación).
    /// </summary>
    public static async Task InitializeAsync(
        CameraDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        var added = 0;
        foreach (var (key, value) in Defaults())
        {
            if (await db.Settings.AnyAsync(x => x.Key == key, ct)) continue;
            db.Settings.Add(new Setting { Key = key, Value = value });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Ajustes iniciales creados: {Count}.", added);
        }
    }

    public static IEnumerable<(string Key, string Value)> Defaults()
    {
        yield return ("cpu.mode", CpuModes.Auto);
        yield return ("rtsp.enabled", "true");
        yield return ("release.minutes", "10");
        yield return ("producer.idle.seconds", "60");
        yield return ("sso.enabled", "false");
    }

    /// <summary>
    /// Modo `--reset-admin-password`: elimina los administradores para que la web
    /// vuelva a pedir crear uno. No arranca el servidor.
    /// </summary>
    public static async Task<int> ResetAdminsAsync(
        string dataDir, ILogger logger, CancellationToken ct = default)
    {
        Directory.CreateDirectory(dataDir);

        var options = new DbContextOptionsBuilder<CameraDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dataDir, "camera.db")}")
            .Options;

        await using var db = new CameraDbContext(options);
        await db.Database.MigrateAsync(ct);

        var service = new AdminSetupService(
            db, new PasswordService(new PasswordHasher<AppUser>()));

        var removed = await service.ResetAdminsAsync(ct);
        if (removed == 0)
        {
            logger.LogInformation("No hay administradores que eliminar.");
            return 0;
        }

        logger.LogWarning(
            "Eliminados {Count} administrador(es). Abre la web para crear uno nuevo.", removed);
        return 0;
    }
}
