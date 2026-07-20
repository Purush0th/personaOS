namespace PersonaOS.Api;

/// <summary>
/// Server API version and the minimum client (mobile/dashboard) version the API
/// still supports. Surfaced via /api/branding so an out-of-date app can show a
/// "please update" screen instead of breaking. Bump MinSupportedClient only on a
/// genuinely breaking client contract change.
/// </summary>
public static class ApiVersionInfo
{
    public const string ApiVersion = "0.1.0";
    public const string MinSupportedClient = "0.1.0";
}
