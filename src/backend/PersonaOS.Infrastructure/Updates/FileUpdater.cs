using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Updates;

/// <summary>
/// Talks to the updater container through a folder both mount (<c>Updates:Path</c>, /app/updates
/// in the compose file): the API writes <c>request.json</c>, the updater writes
/// <c>status.json</c>. No port and no shared secret: only the two containers mount the folder,
/// and the updater accepts nothing but a version. Without the setting there is no updater.
/// </summary>
public sealed class FileUpdater(IConfiguration configuration, TimeProvider time) : IUpdater
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string? Folder => configuration["Updates:Path"] is { Length: > 0 } path && Directory.Exists(path) ? path : null;

    public async Task<UpdaterStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        if (Folder is not { } folder) return null;
        var status = await ReadAsync<StatusFile>(Path.Combine(folder, "status.json"), ct);
        var request = await ReadAsync<RequestFile>(Path.Combine(folder, "request.json"), ct);
        if (status is null && request is null) return null;

        var reported = status is null
            ? new UpdaterStatus(DateTimeOffset.MinValue, null, null, "idle", null, null)
            : new UpdaterStatus(status.Heartbeat, status.RequestId, status.Version, status.State ?? "idle", status.Message, status.FinishedAt);

        // Asked for and not picked up yet: say so, so a second click does not ask twice.
        return request is not null && request.Id != reported.RequestId
            ? reported with { RequestId = request.Id, Version = request.Version, State = "requested", Message = null, FinishedAt = null }
            : reported;
    }

    public async Task<string> RequestAsync(string version, CancellationToken ct = default)
    {
        var folder = Folder ?? throw new InvalidOperationException("No updater folder is set up (Updates:Path).");
        var request = new RequestFile(Guid.NewGuid().ToString("N"), version, time.GetUtcNow());

        // Written whole, then moved into place, so the updater never reads half a file.
        var temporary = Path.Combine(folder, $"request.{request.Id}.tmp");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(request, Json), ct);
        File.Move(temporary, Path.Combine(folder, "request.json"), overwrite: true);
        return request.Id;
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken ct) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null; // Being written right now; the next read gets it.
        }
    }

    private sealed record RequestFile(string Id, string Version, DateTimeOffset RequestedAt);

    private sealed record StatusFile(
        DateTimeOffset Heartbeat, string? RequestId, string? Version, string? State, string? Message, DateTimeOffset? FinishedAt);
}
