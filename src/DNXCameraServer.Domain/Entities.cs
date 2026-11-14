namespace DNXCameraServer.Domain;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public class XiaomiAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string XiaomiUserId { get; set; } = "";
    public string Region { get; set; } = "cn";
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public XiaomiAccount? Account { get; set; }

    public string Did { get; set; } = "";
    public string Model { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Protocol { get; set; } = "miss";
    public int Channel { get; set; }
    public string StreamName { get; set; } = "";
    public string Profile { get; set; } = StreamProfiles.Hd;
    public string Transcode { get; set; } = TranscodeModes.None;
    public bool Enabled { get; set; } = true;
    public string State { get; set; } = CameraStates.Idle;
    public string? CapabilitiesJson { get; set; }
    public string? CapabilitiesOverrideJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? ReleasedUntil { get; set; }
}

public class CameraGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid CameraId { get; set; }
    public Camera? Camera { get; set; }
    public bool CanView { get; set; } = true;
    public bool CanListen { get; set; }
    public bool CanTalk { get; set; }
}

public class DeviceToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CameraId { get; set; }
    public Camera? Camera { get; set; }
    public string TokenId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public string DigestHa1 { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastIp { get; set; }
}

public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string ActorType { get; set; } = "user";
    public string? ActorId { get; set; }
    public Guid? CameraId { get; set; }
    public string Action { get; set; } = "";
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? DetailJson { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
