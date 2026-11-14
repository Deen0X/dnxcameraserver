namespace DNXCameraServer.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";
}

public static class CameraStates
{
    public const string Disabled = "Disabled";
    public const string Idle = "Idle";
    public const string Connecting = "Connecting";
    public const string Live = "Live";
    public const string Retrying = "Retrying";
    public const string Released = "Released";
    public const string Error = "Error";
}

public static class StreamProfiles
{
    public const string Hd = "hd";
    public const string Sd = "sd";
    public const string Auto = "auto";

    public static bool IsValid(string? value) =>
        value is Hd or Sd or Auto;
}

public static class TranscodeModes
{
    public const string None = "none";
    public const string H264 = "h264";
}

public static class CpuModes
{
    public const string Lite = "Lite";
    public const string Auto = "Auto";
    public const string Quality = "Calidad";

    public static string Normalize(string? value) => value switch
    {
        Lite => Lite,
        Quality => Quality,
        _ => Auto
    };
}

public static class Regions
{
    public static readonly string[] All = ["cn", "de", "i2", "ru", "sg", "us"];

    public static string Normalize(string? value)
    {
        var region = (value ?? "").Trim().ToLowerInvariant();
        return All.Contains(region) ? region : "cn";
    }

    public static bool IsValid(string? value) =>
        All.Contains((value ?? "").Trim().ToLowerInvariant());
}

public static class AuditActions
{
    public const string Login = "login";
    public const string LoginFailed = "login_failed";
    public const string Logout = "logout";
    public const string CameraView = "camera_view";
    public const string CameraCreate = "camera_create";
    public const string CameraUpdate = "camera_update";
    public const string CameraDelete = "camera_delete";
    public const string CameraRelease = "camera_release";
    public const string CameraReconnect = "camera_reconnect";
    public const string TokenCreate = "token_create";
    public const string TokenRevoke = "token_revoke";
    public const string RtspConnect = "rtsp_connect";
    public const string RtspDenied = "rtsp_denied";
    public const string AccountCreate = "account_create";
    public const string AccountDelete = "account_delete";
    public const string UserCreate = "user_create";
    public const string UserUpdate = "user_update";
    public const string SettingsUpdate = "settings_update";
}
