using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Ai;

/// <summary>
/// Ollama's own API for finding the server and downloading a model: <c>GET /api/version</c>,
/// <c>GET /api/tags</c>, <c>POST /api/copy</c> and <c>POST /api/pull</c>, which streams one JSON object per line
/// (<c>{"status":"pulling …","total":…,"completed":…}</c>, or <c>{"error":"…"}</c>).
/// </summary>
public class OllamaModelHost(HttpClient http) : ILocalModelHost
{
    /// <summary>How long a probe waits: an address with nothing behind it should not hold up the wizard.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<LocalHostInfo?> DetectAsync(string address, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            var body = await http.GetFromJsonAsync<JsonObject>($"{address}/api/version", cts.Token);
            return body?["version"]?.GetValue<string>() is { Length: > 0 } version ? new LocalHostInfo(address, version) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    public async Task<bool> HasModelAsync(string address, string model, CancellationToken ct = default)
    {
        try
        {
            var body = await http.GetFromJsonAsync<JsonObject>($"{address}/api/tags", ct);
            var names = body?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>() ?? [];
            return names.Any(n => SameModel(n, model));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            throw new LocalModelHostException($"Could not list the models in Ollama at {address}.", ex);
        }
    }

    public async IAsyncEnumerable<ModelPullProgress> PullAsync(
        string address, string model, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{address}/api/pull")
        {
            Content = JsonContent.Create(new { model, stream = true }),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalModelHostException($"Could not reach Ollama at {address}.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new LocalModelHostException(ErrorOf(await response.Content.ReadAsStringAsync(ct)) ?? $"Ollama refused the download ({(int)response.StatusCode}).");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct);
                }
                catch (IOException ex)
                {
                    throw new LocalModelHostException("The download from Ollama stopped part way. Try again: it resumes.", ex);
                }

                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (ErrorOf(line) is { } error) throw new LocalModelHostException(error);
                if (Progress(line) is { } step) yield return step;
            }
        }
    }

    public async Task CopyModelAsync(string address, string source, string destination, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync($"{address}/api/copy", new { source, destination }, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalModelHostException($"Could not reach Ollama at {address}.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new LocalModelHostException(ErrorOf(await response.Content.ReadAsStringAsync(ct))
                    ?? $"Ollama could not name the model {destination} ({(int)response.StatusCode}).");
        }
    }

    /// <summary>"qwen3:4b" and "qwen3:4b:latest"-style names: a name without a tag means ":latest".</summary>
    internal static bool SameModel(string listed, string wanted)
    {
        static string Full(string name) => name.LastIndexOf(':') > name.LastIndexOf('/') ? name : name + ":latest";
        return string.Equals(Full(listed), Full(wanted), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ErrorOf(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["error"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ModelPullProgress? Progress(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node?["status"]?.GetValue<string>() is not { } status) return null;
            return new ModelPullProgress(status, node["completed"]?.GetValue<long>(), node["total"]?.GetValue<long>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
