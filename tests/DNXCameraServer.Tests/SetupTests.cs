using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Security;
using DNXCameraServer.Web.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Tests;

public class AdminSetupServiceTests
{
    private static (AdminSetupService Setup, CameraDbContext Db, SqliteConnection Conn) New()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<CameraDbContext>().UseSqlite(conn).Options;
        var db = new CameraDbContext(options);
        db.Database.EnsureCreated();
        var service = new AdminSetupService(
            db, new PasswordService(new PasswordHasher<AppUser>()));
        return (service, db, conn);
    }

    [Fact]
    public async Task InstalacionLimpia_RequiereSetup()
    {
        var (setup, _, conn) = New();
        using (conn)
        {
            Assert.True(await setup.NeedsSetupAsync());
        }
    }

    [Fact]
    public async Task CrearAdmin_CompletaLaInstalacion()
    {
        var (setup, db, conn) = New();
        using (conn)
        {
            var (ok, error) = await setup.CreateAdminAsync("admin", "clave-larga-123");

            Assert.True(ok);
            Assert.Null(error);
            Assert.False(await setup.NeedsSetupAsync());

            var user = await db.Users.SingleAsync();
            Assert.Equal(Roles.Admin, user.Role);
            Assert.True(user.IsActive);
            Assert.NotEqual("clave-larga-123", user.PasswordHash);
        }
    }

    [Fact]
    public async Task CrearAdmin_RechazaPasswordCorta()
    {
        var (setup, _, conn) = New();
        using (conn)
        {
            var (ok, error) = await setup.CreateAdminAsync("admin", "corta");
            Assert.False(ok);
            Assert.Equal("weak_password", error);
        }
    }

    [Fact]
    public async Task CrearAdmin_RechazaUsuarioDuplicado()
    {
        var (setup, db, conn) = New();
        using (conn)
        {
            db.Users.Add(new AppUser { Username = "visor", Role = Roles.Viewer });
            await db.SaveChangesAsync();

            var (ok, error) = await setup.CreateAdminAsync("visor", "clave-larga-123");
            Assert.False(ok);
            Assert.Equal("username_taken", error);
        }
    }

    [Fact]
    public async Task Reset_EliminaAdminsYConservaViewers()
    {
        var (setup, db, conn) = New();
        using (conn)
        {
            Assert.True((await setup.CreateAdminAsync("admin", "clave-larga-123")).Ok);

            var viewer = new AppUser { Username = "visor", Role = Roles.Viewer };
            db.Users.Add(viewer);
            await db.SaveChangesAsync();

            var removed = await setup.ResetAdminsAsync();

            Assert.Equal(1, removed);
            Assert.True(await setup.NeedsSetupAsync());
            Assert.Equal("visor", (await db.Users.SingleAsync()).Username);
        }
    }
}
