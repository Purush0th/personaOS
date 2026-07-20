namespace PersonaOS.Application.Auth;

public record AuthResult(string AccessToken, DateTime ExpiresAtUtc, string Username);

public interface IAuthService
{
    /// <summary>Validates credentials and returns a signed token, or null if invalid.</summary>
    Task<AuthResult?> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Whether an admin account exists yet (false before Setup Wizard runs).</summary>
    Task<bool> AdminExistsAsync(CancellationToken ct = default);

    /// <summary>Creates the single admin account. Used by the Setup Wizard.</summary>
    Task CreateAdminAsync(string username, string password, CancellationToken ct = default);
}
