using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain;

namespace PersonaOS.Infrastructure.Updates;

/// <summary>
/// Releases from GitHub. The list, not "latest": "latest" leaves prereleases out, and every
/// release so far is one. Read at most every ten minutes (the unauthenticated API allows 60
/// requests an hour), and an error reads as no releases rather than a failure.
/// </summary>
public sealed class GitHubReleaseFeed(HttpClient http, TimeProvider time, ILogger<GitHubReleaseFeed> logger) : IReleaseFeed, IDisposable
{
    public void Dispose() => _gate.Dispose();

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ReleaseInfo> _cached = [];
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (time.GetUtcNow() - _readAt < CacheFor) return _cached;
            _cached = await ReadAsync(ct) ?? _cached;
            _readAt = time.GetUtcNow();
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ReleaseInfo>?> ReadAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{PersonaOsProject.Repository}/releases?per_page=20");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PersonaOS", "1"));
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Could not read releases: GitHub answered {Status}.", (int)response.StatusCode);
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogInformation("Could not read releases: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>The published releases in a GitHub releases list; drafts are left out.</summary>
    public static IReadOnlyList<ReleaseInfo> Parse(JsonElement list)
    {
        var releases = new List<ReleaseInfo>();
        if (list.ValueKind != JsonValueKind.Array) return releases;

        foreach (var release in list.EnumerateArray())
        {
            if (Bool(release, "draft")) continue;
            var tag = Text(release, "tag_name");
            if (string.IsNullOrEmpty(tag)) continue;

            var apk = release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
                ? assets.EnumerateArray()
                    .Where(a => Text(a, "name")?.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(a => Text(a, "browser_download_url"))
                    .FirstOrDefault()
                : null;

            releases.Add(new ReleaseInfo(
                tag.TrimStart('v'),
                Text(release, "name") is { Length: > 0 } name ? name : tag,
                Text(release, "html_url") ?? PersonaOsProject.ReleasesUrl,
                Bool(release, "prerelease"),
                DateTimeOffset.TryParse(Text(release, "published_at"), out var published) ? published : DateTimeOffset.MinValue,
                apk));
        }
        return releases;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
