namespace PersonaOS.Domain;

/// <summary>
/// Where PersonaOS itself lives. Fixed like the brand (see the PRD's brand model),
/// and deliberately in one place: the assistant's grounding prompt and the dashboard's
/// update check both need it, and when they drifted apart the update banner silently
/// polled a repository that does not exist.
/// </summary>
public static class PersonaOsProject
{
    /// <summary>GitHub "owner/name", as the releases API expects it.</summary>
    public const string Repository = "Purush0th/personaOS";

    public const string Url = "https://github.com/" + Repository;

    public const string ReleasesUrl = Url + "/releases";

    /// <summary>
    /// The tag prefix of a phone app release ("mobile-v0.1.0-alpha.14"). The server and web are
    /// tagged "v…" and the app "mobile-v…", each released when it changes (owner, 2026-10-29).
    /// </summary>
    public const string AppTagPrefix = "mobile-v";
}
