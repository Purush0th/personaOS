namespace PersonaOS.Infrastructure.Auth;

/// <summary>
/// JWT settings bound from configuration section "Jwt". The signing key is a
/// secret supplied via env/user-secrets and is never committed.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "PersonaOS";
    public string Audience { get; set; } = "PersonaOS";
    public int AccessTokenMinutes { get; set; } = 60 * 12; // 12h; single-user personal app
}
