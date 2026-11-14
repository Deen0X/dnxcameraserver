using DNXCameraServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DNXCameraServer.Data;

/// <summary>Fábrica para `dotnet ef` sin arrancar la aplicación completa.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CameraDbContext>
{
    public CameraDbContext CreateDbContext(string[] args)
    {
        var dir = Environment.GetEnvironmentVariable("DATA_DIR")
                  ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<CameraDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dir, "camera.db")}")
            .Options;

        return new CameraDbContext(options);
    }
}
