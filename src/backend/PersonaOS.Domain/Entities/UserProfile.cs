namespace PersonaOS.Domain.Entities;

/// <summary>
/// Singleton profile of the instance's user (always Id == 1). Free-form facts
/// and preferences injected into the assistant's system prompt so it behaves
/// as *their* assistant. Editable via API; grows richer over time.
/// </summary>
public class UserProfile
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Free-form facts/preferences about the user ("I'm a .NET developer, vegetarian, in Chennai…").</summary>
    public string AboutMe { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
