namespace PersonaOS.Domain.Entities;

/// <summary>
/// A push-notification token for one of the user's devices. Registered by the
/// mobile app after login and refreshed whenever the platform rotates it.
/// </summary>
public class DeviceToken
{
    public int Id { get; set; }

    /// <summary>The FCM registration token. Unique per device install.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>One of <see cref="DevicePlatforms"/>.</summary>
    public string Platform { get; set; } = DevicePlatforms.Android;

    /// <summary>Human-readable device label, for the user to identify it.</summary>
    public string? DeviceName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last time the app re-registered this token; stale tokens can be pruned.</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Canonical device platforms.</summary>
public static class DevicePlatforms
{
    public const string Android = "android";
    public const string Ios = "ios";

    public static readonly IReadOnlyList<string> All = [Android, Ios];
}
