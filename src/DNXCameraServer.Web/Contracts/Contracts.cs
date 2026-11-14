namespace DNXCameraServer.Web.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record XiaomiLoginRequest(
    string? Username,
    string? Password,
    string? Captcha,
    string? Verify,
    string? Region);

public sealed record XiaomiTokenRequest(
    string? UserId,
    string? Token,
    string? Region,
    string? Alias);

public sealed record AccountUpdateRequest(string? Region, string? Alias);

public sealed record CameraImportRequest(
    Guid AccountId,
    string Did,
    string Model,
    string Name,
    string Ip,
    int Channel = 0);

public sealed record CameraUpdateRequest(
    string? Name,
    string? Ip,
    string? Profile,
    string? Transcode,
    bool? Enabled,
    string? CapabilitiesOverrideJson);

public sealed record ProfileRequest(string Profile);

public sealed record ReleaseRequest(int? Minutes);

public sealed record UserCreateRequest(string Username, string Password, string Role);

public sealed record UserUpdateRequest(string? Password, string? Role, bool? IsActive);

public sealed record GrantRequest(Guid CameraId, bool CanView, bool CanListen, bool CanTalk);

public sealed record TokenCreateRequest(Guid CameraId, string Name, int? ExpiresInDays);

public sealed record SettingsRequest(Dictionary<string, string> Values);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SetupRequest(string Username, string Password);

public sealed record CameraDto(
    Guid Id,
    string Name,
    string Model,
    string Ip,
    string Protocol,
    string StreamName,
    string Profile,
    string State,
    bool Enabled,
    Guid AccountId,
    string? AccountAlias,
    string Region,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? ReleasedUntil,
    Domain.CameraCapabilities Capabilities);
