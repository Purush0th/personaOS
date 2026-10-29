using System.Reflection;

namespace PersonaOS.Api;

/// <summary>
/// Server API version and the minimum client (mobile/dashboard) version the API
/// still supports. Surfaced via /api/branding so an out-of-date app can show a
/// "please update" screen instead of breaking. Bump MinSupportedClient only on a
/// genuinely breaking client contract change.
/// </summary>
public static class ApiVersionInfo
{
    /// <summary>
    /// The release this build is: the image is built with <c>-p:Version=</c> from the release tag
    /// (src/backend/Dockerfile). A build from a checkout without one reports "0.0.0-dev", which no
    /// release is older than. It was a constant "0.1.0" in every build, so the update check could
    /// never tell one release from another.
    /// </summary>
    public static readonly string ApiVersion =
        typeof(ApiVersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] is { Length: > 0 } version && version != "1.0.0"
            ? version
            : "0.0.0-dev";

    /// <summary>
    /// The oldest phone app this server works with; the app warns when it is older. A plain "0.1.0"
    /// would have counted every alpha as too old. alpha.13 is the last app released together with
    /// the server; older ones were never checked against later servers.
    /// </summary>
    public const string MinSupportedClient = "0.1.0-alpha.13";
}
