using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Tests;

/// <summary>
/// Regresión: SQLite no soporta ORDER BY sobre DateTimeOffset (HTTP 500 en
/// /api/xiaomi/accounts, /api/device-tokens y /api/audit).
/// </summary>
public class SqliteOrderingTests
{
    private static (CameraDbContext Db, SqliteConnection Conn) NewDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<CameraDbContext>().UseSqlite(conn).Options;
        var db = new CameraDbContext(options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    [Fact]
    public async Task Cuentas_SeOrdenanPorCreatedAt()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            var older = new XiaomiAccount { XiaomiUserId = "1", Region = "cn" };
            var newer = new XiaomiAccount { XiaomiUserId = "2", Region = "de" };
            older.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
            newer.CreatedAt = DateTimeOffset.UtcNow;

            db.Accounts.AddRange(older, newer);
            await db.SaveChangesAsync();

            var list = await db.Accounts.OrderBy(a => a.CreatedAt).ToListAsync();

            Assert.Equal(2, list.Count);
            Assert.Equal("1", list[0].XiaomiUserId);
            Assert.Equal("2", list[1].XiaomiUserId);
        }
    }

    [Fact]
    public async Task Tokens_SeOrdenanPorCreatedAtDescendente()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            var account = new XiaomiAccount { XiaomiUserId = "u", Region = "cn" };
            var camera = new Camera
            {
                AccountId = account.Id,
                Name = "cam",
                StreamName = "cam_1",
                Model = "isa.camera.df3",
                Ip = "192.168.1.10",
                Did = "d1"
            };
            db.Accounts.Add(account);
            db.Cameras.Add(camera);
            await db.SaveChangesAsync();

            db.DeviceTokens.Add(new DeviceToken
            {
                CameraId = camera.Id,
                TokenId = "aaaaaaaa",
                Name = "viejo",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-5)
            });
            db.DeviceTokens.Add(new DeviceToken
            {
                CameraId = camera.Id,
                TokenId = "bbbbbbbb",
                Name = "nuevo",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var list = await db.DeviceTokens.OrderByDescending(t => t.CreatedAt).ToListAsync();

            Assert.Equal("bbbbbbbb", list[0].TokenId);
        }
    }

    [Fact]
    public async Task LeeFilasAntiguasConElFormatoPorDefectoDeEf()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            // Formato que EF Core escribe por defecto para DateTimeOffset en SQLite.
            var id = Guid.NewGuid().ToString();
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO Accounts (Id, XiaomiUserId, Region, Alias, CreatedAt) VALUES ({id}, 'legacy', 'cn', NULL, '2026-11-13 19:48:28.1209873+00:00')");

            var account = await db.Accounts.SingleAsync(a => a.XiaomiUserId == "legacy");

            Assert.Equal(2026, account.CreatedAt.Year);
            Assert.Equal(11, account.CreatedAt.Month);
            Assert.Equal(TimeSpan.Zero, account.CreatedAt.Offset);
        }
    }

    [Fact]
    public async Task Auditoria_SeOrdenaYFiltraPorFecha()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            db.Audit.Add(new AuditEvent { Action = "login", At = DateTimeOffset.UtcNow.AddMinutes(-10) });
            db.Audit.Add(new AuditEvent { Action = "rtsp_connect", At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            var list = await db.Audit.OrderByDescending(a => a.At).Take(10).ToListAsync();
            Assert.Equal("rtsp_connect", list[0].Action);

            var since = DateTimeOffset.UtcNow.AddMinutes(-5);
            var recent = await db.Audit.Where(a => a.At >= since).ToListAsync();
            Assert.Single(recent);
        }
    }
}
