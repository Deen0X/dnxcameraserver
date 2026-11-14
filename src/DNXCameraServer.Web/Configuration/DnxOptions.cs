using DNXCameraServer.Domain;

namespace DNXCameraServer.Web.Configuration;

/// <summary>Configuración del servicio, leída de variables de entorno.</summary>
public sealed class DnxOptions
{
    public string DataDir { get; init; } = "/data";
    public string RecordingsDir { get; init; } = "/recordings";
    public string ModelsDir { get; init; } = "";
    public string Go2RtcApi { get; init; } = "http://127.0.0.1:1984";
    public string Go2RtcRtsp { get; init; } = "127.0.0.1:18554";
    public string Go2RtcBinary { get; init; } = "/opt/go2rtc/go2rtc";
    public string Go2RtcConfig { get; init; } = "";
    public string WebRtcCandidate { get; init; } = "";
    public string LanCidr { get; init; } = "192.168.0.0/16";
    public int RtspListenPort { get; init; } = 8554;
    public string SsoHeader { get; init; } = "Remote-User";
    public string SsoTrustedProxies { get; init; } = "";
    public bool SsoEnabled { get; init; }
    public string CpuMode { get; init; } = CpuModes.Auto;
    public string Realm { get; init; } = "dnxcameraserver";
    public string AdminUser { get; init; } = "admin";
    public string AdminPassword { get; init; } = "";
    public string Version { get; init; } = "0.1.0";

    public string EffectiveGo2RtcConfig =>
        string.IsNullOrWhiteSpace(Go2RtcConfig) ? Path.Combine(DataDir, "go2rtc.yaml") : Go2RtcConfig;

    public string EffectiveModelsDir =>
        string.IsNullOrWhiteSpace(ModelsDir)
            ? Path.Combine(AppContext.BaseDirectory, "data", "models")
            : ModelsDir;

    public IReadOnlyList<string> TrustedProxies =>
        SsoTrustedProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static DnxOptions FromEnvironment()
    {
        static string E(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

        static bool B(string name) =>
            bool.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v;

        static int I(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

        return new DnxOptions
        {
            DataDir = E("DATA_DIR", "/data"),
            RecordingsDir = E("RECORDINGS_DIR", "/recordings"),
            ModelsDir = E("MODELS_DIR", ""),
            Go2RtcApi = E("GO2RTC_API", "http://127.0.0.1:1984"),
            Go2RtcRtsp = E("GO2RTC_RTSP", "127.0.0.1:18554"),
            Go2RtcBinary = E("GO2RTC_BINARY", "/opt/go2rtc/go2rtc"),
            Go2RtcConfig = E("GO2RTC_CONFIG", ""),
            WebRtcCandidate = E("WEBRTC_CANDIDATE", ""),
            LanCidr = E("LAN_CIDR", "192.168.0.0/16"),
            RtspListenPort = I("RTSP_LISTEN_PORT", 8554),
            SsoHeader = E("SSO_HEADER", "Remote-User"),
            SsoTrustedProxies = E("SSO_TRUSTED_PROXIES", ""),
            SsoEnabled = B("SSO_ENABLED"),
            CpuMode = CpuModes.Normalize(E("CPU_MODE", CpuModes.Auto)),
            Realm = E("AUTH_REALM", "dnxcameraserver"),
            AdminUser = E("DNX_ADMIN_USER", "admin"),
            AdminPassword = E("DNX_ADMIN_PASSWORD", ""),
            Version = E("DNX_VERSION", "0.1.0")
        };
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(RecordingsDir);
        Directory.CreateDirectory(Path.Combine(DataDir, "certs"));
    }

    public void Validate()
    {
        if (RtspListenPort is < 1 or > 65535)
            throw new InvalidOperationException("RTSP_LISTEN_PORT inválido.");
        if (SsoEnabled && TrustedProxies.Count == 0)
            throw new InvalidOperationException(
                "SSO_ENABLED=true requiere SSO_TRUSTED_PROXIES con la IP del proxy.");
        if (!string.IsNullOrWhiteSpace(WebRtcCandidate) && !WebRtcCandidate.Contains(':'))
            throw new InvalidOperationException(
                "WEBRTC_CANDIDATE debe tener formato ip:puerto (ej. 192.168.1.10:8555).");
    }
}
