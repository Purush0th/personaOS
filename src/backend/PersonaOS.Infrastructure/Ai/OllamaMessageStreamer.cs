using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Infrastructure.Ai;

/// <summary>
/// Ollama through its own API (<c>POST /api/chat</c>), not its OpenAI-compatible one. The native
/// API takes what the compatible one cannot:
/// <list type="bullet">
/// <item><c>options.num_ctx</c>: Ollama otherwise loads many models with a 4,096-token context,
/// smaller than a PersonaOS request, and the prompt is silently cut.</item>
/// <item><c>think</c>: Qwen3 reasons before every reply; <c>/no_think</c> in the prompt measurably
/// did nothing through the compatible endpoint, while <c>think: false</c> switches it off.</item>
/// <item><c>keep_alive</c>: keeps the model loaded between messages instead of reloading it.</item>
/// </list>
/// The response is newline-delimited JSON, one object per chunk. Tool calls arrive whole, with
/// their arguments as an object, usually in one chunk before the last.
/// </summary>
public class OllamaMessageStreamer(HttpClient http) : IAiMessageStreamer
{
    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        AiRequest aiRequest, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aiRequest.BaseUrl))
            throw new AiStreamException("No base URL is set for Ollama. Add one in Settings, e.g. http://localhost:11434.");

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl(aiRequest.BaseUrl))
        {
            Content = new StringContent(BuildRequestBody(aiRequest).ToJsonString(), Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AiStreamException("Could not reach Ollama. Check the base URL in Settings and that Ollama is running.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw MapError(response.StatusCode, await response.Content.ReadAsStringAsync(ct), aiRequest.Model);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var toolCalls = new List<AiToolCall>();

            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct);
                }
                catch (IOException ex)
                {
                    throw new AiStreamException("The connection to Ollama ended unexpectedly.", ex);
                }

                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                foreach (var chunk in ReadLine(line, toolCalls)) yield return chunk;
            }
        }
    }

    /// <summary>
    /// Turns one line of the stream into chunks. Text streams as it comes; tool calls are held until
    /// the last line, then emitted together with the tool-use stop.
    /// </summary>
    private static IEnumerable<AiStreamChunk> ReadLine(string line, List<AiToolCall> toolCalls)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            yield break;
        }

        if (node?["error"]?.GetValue<string>() is { } error) throw new AiStreamException($"Ollama error: {error}");

        var message = node?["message"];
        if (message?["content"]?.GetValue<string>() is { Length: > 0 } text) yield return new AiStreamChunk(TextDelta: text);

        if (message?["tool_calls"] is JsonArray calls)
        {
            foreach (var call in calls)
            {
                var function = call?["function"];
                if (function?["name"]?.GetValue<string>() is not { Length: > 0 } name) continue;
                var arguments = function["arguments"] switch
                {
                    JsonObject args => args.ToJsonString(),
                    JsonValue value when value.TryGetValue<string>(out var raw) => raw,
                    _ => "{}",
                };
                toolCalls.Add(new AiToolCall(call?["id"]?.GetValue<string>() ?? $"call_{toolCalls.Count + 1}", name, arguments));
            }
        }

        if (node?["done"]?.GetValue<bool>() != true) yield break;

        long? input = node["prompt_eval_count"]?.GetValue<long>();
        long? output = node["eval_count"]?.GetValue<long>();
        foreach (var call in toolCalls) yield return new AiStreamChunk(ToolCall: call);
        yield return new AiStreamChunk(
            InputTokens: input,
            OutputTokens: output,
            StopReason: toolCalls.Count > 0 ? AiStopReasons.ToolUse : null);
        toolCalls.Clear();
    }

    /// <summary>
    /// Accepts the server's root ("http://host:11434") or, since that is what the OpenAI-compatible
    /// provider asked for, the same with "/v1" on the end.
    /// </summary>
    private static string ChatUrl(string baseUrl)
    {
        var root = baseUrl.Trim().TrimEnd('/');
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) root = root[..^3];
        return root + "/api/chat";
    }

    private static JsonObject BuildRequestBody(AiRequest request)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt } };

        // A tool result names the tool it answers; Ollama pairs them by name, not by id.
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var turn in request.Turns)
        {
            if (turn.ToolResults is { Count: > 0 } results)
            {
                foreach (var result in results)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["content"] = result.Content,
                        ["tool_name"] = toolNames.GetValueOrDefault(result.ToolUseId),
                    });
                }
                continue;
            }

            var message = new JsonObject
            {
                ["role"] = turn.Role == ChatRoles.Assistant ? "assistant" : "user",
                ["content"] = turn.Content,
            };
            if (turn.ToolCalls is { Count: > 0 } calls)
            {
                var array = new JsonArray();
                foreach (var call in calls)
                {
                    toolNames[call.Id] = call.Name;
                    array.Add(new JsonObject
                    {
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = ParseArguments(call.InputJson) },
                    });
                }
                message["tool_calls"] = array;
            }
            messages.Add(message);
        }

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.InputSchemaJson),
                },
            }).ToArray());
        }

        var options = request.ModelOptions;
        if (options.Think is bool think) body["think"] = think;
        if (options.KeepAlive is { } keepAlive) body["keep_alive"] = keepAlive;
        if (options.ContextTokens is int context) body["options"] = new JsonObject { ["num_ctx"] = context };
        return body;
    }

    /// <summary>Ollama wants arguments as an object; a model's malformed ones are sent as an empty one.</summary>
    private static JsonNode ParseArguments(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static AiStreamException MapError(HttpStatusCode status, string body, string model)
    {
        string? detail = null;
        try
        {
            detail = JsonNode.Parse(body)?["error"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            // Not JSON: fall back to the status alone.
        }

        return detail switch
        {
            not null when detail.Contains("does not support tools", StringComparison.OrdinalIgnoreCase) =>
                new AiStreamException($"{model} cannot use tools, which PersonaOS needs. Pick a model with tool support, such as qwen2.5:3b-instruct."),
            not null when status == HttpStatusCode.NotFound =>
                new AiStreamException($"Ollama does not have {model}. Run `ollama pull {model}` on the server, or check the model name in Settings."),
            not null => new AiStreamException($"Ollama error ({(int)status}): {detail}"),
            _ => new AiStreamException($"Ollama error ({(int)status})."),
        };
    }
}
