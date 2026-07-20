namespace PersonaOS.Domain.Entities;

/// <summary>
/// The single admin/owner account for this install, created by the Setup Wizard.
/// PersonaOS is single-tenant single-user, so there is exactly one of these.
/// </summary>
public class AdminUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>Hashed password (never plaintext). Hashing lives in the auth service.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginUtc { get; set; }
}
