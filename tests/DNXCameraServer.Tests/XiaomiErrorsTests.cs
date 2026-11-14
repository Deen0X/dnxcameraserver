using DNXCameraServer.Domain;
using DNXCameraServer.Web.Xiaomi;

namespace DNXCameraServer.Tests;

public class XiaomiErrorsTests
{
    // Cuerpo real devuelto por Xiaomi (go2rtc) al bloquear el login por seguridad.
    private const string Body70016 =
        "xiaomi: {\"qs\":\"%3F_json%3Dtrue%26sid%3Dxiaomiio\",\"code\":70016," +
        "\"description\":\"登录验证失败\",\"securityStatus\":0,\"_sign\":\"7RoEnWWgpjzsTGA0t/VgVXgydtc=\"," +
        "\"sid\":\"xiaomiio\",\"result\":\"error\",\"miDemo\":0,\"captchaUrl\":null," +
        "\"callback\":\"https://sts.api.io.mi.com/sts\",\"location\":\"\",\"pwd\":0," +
        "\"child\":0,\"desc\":\"登录验证失败\"}";

    [Fact]
    public void Parse_ExtraeCodigoYDescripcion()
    {
        var parsed = XiaomiErrors.Parse(Body70016);

        Assert.Equal(70016, parsed.Code);
        Assert.Equal("登录验证失败", parsed.Description);
    }

    [Fact]
    public void Friendly_70016_ExplicaLaAlternativaDelToken()
    {
        var message = XiaomiErrors.FriendlyMessage(XiaomiErrors.Parse(Body70016));

        Assert.Contains("70016", message);
        Assert.Contains("account.xiaomi.com", message);
        Assert.Contains("token", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CuerpoSinJson_NoLanzaYDevuelveRaw()
    {
        var parsed = XiaomiErrors.Parse("Connection refused");

        Assert.Null(parsed.Code);
        Assert.Equal("Connection refused", parsed.Raw);
    }

    [Fact]
    public void Parse_CuerpoVacio_EsSeguro()
    {
        var parsed = XiaomiErrors.Parse("");
        Assert.Null(parsed.Code);
        Assert.Null(parsed.Raw);
    }

    [Fact]
    public void Friendly_OtrosCodigos_IncluyeDescripcion()
    {
        var parsed = XiaomiErrors.Parse("xiaomi: {\"code\":70022,\"description\":\"账号或密码错误\"}");
        var message = XiaomiErrors.FriendlyMessage(parsed);

        Assert.Contains("70022", message);
        Assert.Contains("contraseña", message, StringComparison.OrdinalIgnoreCase);
    }
}

public class RegionsTests
{
    [Theory]
    [InlineData("cn", "cn")]
    [InlineData("CN", "cn")]
    [InlineData(" de ", "de")]
    [InlineData("xx", "cn")]
    [InlineData(null, "cn")]
    public void Normalize_DevuelveRegionValida(string? input, string expected)
    {
        Assert.Equal(expected, Regions.Normalize(input));
    }

    [Theory]
    [InlineData("cn", true)]
    [InlineData("sg", true)]
    [InlineData("tw", false)]
    [InlineData("", false)]
    public void IsValid_ReconoceLasSoportadas(string input, bool expected)
    {
        Assert.Equal(expected, Regions.IsValid(input));
    }
}
