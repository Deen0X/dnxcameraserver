using System.Security.Claims;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Cameras;
using DNXCameraServer.Web.Configuration;
using DNXCameraServer.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DNXCameraServer.Tests;

public class DigestAuthTests
{
    private const string Realm = "dnxcameraserver";
    private const string Key = "test-nonce-key";
    private const string User = "abcd1234";
    private const string Secret = "super-secreto";

    [Fact]
    public void Nonce_Roundtrip_EsValido()
    {
        var nonce = DigestAuth.CreateNonce(Key);
        Assert.True(DigestAuth.ValidateNonce(nonce, Key, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Nonce_ConClaveDistinta_EsRechazado()
    {
        var nonce = DigestAuth.CreateNonce(Key);
        Assert.False(DigestAuth.ValidateNonce(nonce, "otra-clave", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void RespuestaDigest_Valida_ConQop()
    {
        var ha1 = DigestAuth.Ha1(User, Realm, Secret);
        var nonce = DigestAuth.CreateNonce(Key);
        const string uri = "rtsp://192.168.1.10:8554/cam_abc";
        const string nc = "00000001";
        const string cnonce = "0a1b2c3d";
        const string qop = "auth";
        var ha2 = DigestAuth.Md5Hex($"DESCRIBE:{uri}");
        var response = DigestAuth.Md5Hex($"{ha1}:{nonce}:{nc}:{cnonce}:{qop}:{ha2}");

        var parameters = new Dictionary<string, string>
        {
            ["username"] = User,
            ["realm"] = Realm,
            ["nonce"] = nonce,
            ["uri"] = uri,
            ["response"] = response,
            ["qop"] = qop,
            ["nc"] = nc,
            ["cnonce"] = cnonce
        };

        Assert.True(DigestAuth.VerifyResponse(parameters, "DESCRIBE", ha1, Key));
    }

    [Fact]
    public void RespuestaDigest_Invalida_EsRechazada()
    {
        var ha1 = DigestAuth.Ha1(User, Realm, Secret);
        var nonce = DigestAuth.CreateNonce(Key);

        var parameters = new Dictionary<string, string>
        {
            ["username"] = User,
            ["realm"] = Realm,
            ["nonce"] = nonce,
            ["uri"] = "/cam_abc",
            ["response"] = "00000000000000000000000000000000"
        };

        Assert.False(DigestAuth.VerifyResponse(parameters, "DESCRIBE", ha1, Key));
    }

    [Fact]
    public void ParseParameters_ExtraeCampos()
    {
        var header = "Digest username=\"abc\", realm=\"r\", nonce=\"n\", uri=\"/x\", response=\"deadbeef\"";
        var p = DigestAuth.ParseParameters(header);
        Assert.Equal("abc", p["username"]);
        Assert.Equal("deadbeef", p["response"]);
    }
}

public class PasswordServiceTests
{
    private readonly PasswordService _service = new(new PasswordHasher<AppUser>());

    [Fact]
    public void Hash_Verifica_Correctamente()
    {
        var hash = _service.Hash("clave-larga-123");
        Assert.True(_service.Verify(hash, "clave-larga-123"));
        Assert.False(_service.Verify(hash, "otra"));
    }

    [Fact]
    public void Hash_EsDistintoEnCadaLlamada()
    {
        Assert.NotEqual(_service.Hash("clave-larga-123"), _service.Hash("clave-larga-123"));
    }
}

public class CapabilityServiceTests
{
    private static CapabilityService Create(string modelsDir) =>
        new(new DnxOptions { ModelsDir = modelsDir }, NullLogger<CapabilityService>.Instance);

    [Fact]
    public void ModeloDesconocido_UsaHeuristicaMiss()
    {
        var service = Create(Path.Combine(Path.GetTempPath(), "dnx-models-none"));
        var caps = service.Resolve("chuangmi.camera.046c04");

        Assert.True(caps.Audio.Talk);
        Assert.Equal(3, caps.Quality.HdIndex);
        Assert.False(caps.Ptz);
        Assert.True(caps.Zoom.DigitalInViewer);
    }

    [Fact]
    public void ModeloLegacy_NoAnunciaTalk()
    {
        var service = Create(Path.Combine(Path.GetTempPath(), "dnx-models-none"));
        var caps = service.Resolve("isa.camera.isc5");

        Assert.False(caps.Audio.Talk);
        Assert.Equal("legacy", service.ProtocolFor("isa.camera.isc5"));
    }

    [Fact]
    public void ModeloCateye_AnunciaWakeup()
    {
        var service = Create(Path.Combine(Path.GetTempPath(), "dnx-models-none"));
        var caps = service.Resolve("loock.cateye.v99");
        Assert.True(caps.Wakeup);
    }

    [Fact]
    public void FichaDeModelo_TienePrioridad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dnx-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "toy.json"), """
        {
          "model": "toy.camera.1",
          "protocol": "miss",
          "capabilities": {
            "audio": { "out": true, "talk": false },
            "quality": { "profiles": ["hd"], "native": "hd", "hdIndex": 5 }
          }
        }
        """);

        var service = Create(dir);
        var caps = service.Resolve("toy.camera.1");

        Assert.False(caps.Audio.Talk);
        Assert.Equal(5, caps.Quality.HdIndex);
        Assert.Equal("model_db", caps.Source);
    }
}

public class CameraAccessTests
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

    private static ClaimsPrincipal Viewer(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "visor"),
            new Claim(ClaimTypes.Role, Roles.Viewer),
            new Claim(UserClaims.UserId, userId.ToString())
        ], "test"));

    private static async Task<Camera> AddCameraAsync(CameraDbContext db, string streamName)
    {
        var account = new XiaomiAccount { XiaomiUserId = "u1", Region = "cn" };
        var camera = new Camera
        {
            AccountId = account.Id,
            Name = "cam",
            StreamName = streamName,
            Model = "isa.camera.df3",
            Ip = "192.168.1.50",
            Did = "did1"
        };
        db.Accounts.Add(account);
        db.Cameras.Add(camera);
        await db.SaveChangesAsync();
        return camera;
    }

    [Fact]
    public async Task Viewer_SinPermiso_NoVeLaCamara()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            var user = new AppUser { Username = "visor", Role = Roles.Viewer };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var camera = await AddCameraAsync(db, "cam_x");

            var access = new CameraAccess(db);
            Assert.False(await access.CanAsync(Viewer(user.Id), camera.Id, "view"));
        }
    }

    [Fact]
    public async Task Viewer_ConPermiso_VeLaCamara()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            var user = new AppUser { Username = "visor", Role = Roles.Viewer };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var camera = await AddCameraAsync(db, "cam_y");

            db.Grants.Add(new CameraGrant
            {
                UserId = user.Id,
                CameraId = camera.Id,
                CanView = true
            });
            await db.SaveChangesAsync();

            var access = new CameraAccess(db);
            Assert.True(await access.CanAsync(Viewer(user.Id), camera.Id, "view"));
            Assert.False(await access.CanAsync(Viewer(user.Id), camera.Id, "talk"));
            Assert.Single(await access.VisibleCameraIdsAsync(Viewer(user.Id)));
        }
    }

    [Fact]
    public async Task Admin_VeTodo()
    {
        var (db, conn) = NewDb();
        using (conn)
        {
            var camera = await AddCameraAsync(db, "cam_z");

            var admin = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, Roles.Admin)
            ], "test"));

            var access = new CameraAccess(db);
            Assert.True(await access.CanAsync(admin, camera.Id, "talk"));
        }
    }
}
