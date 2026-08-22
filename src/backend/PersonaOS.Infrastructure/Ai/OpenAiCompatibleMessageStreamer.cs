using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Infrastructure.Ai;

/// <summary>
/// Streaming adapter for any OpenAI Chat Completions-compatible endpoint: OpenAI,
/// Ollama, Groq, OpenRouter, LM Studio, vLLM, and others. The provider is chosen
/// entirely by <c>AiBaseUrl</c> + model + key, so one adapter covers them all.
/// Translates the neutral turns/tools to the OpenAI wire format and streams the
/// SSE response back as provider-neutral chunks.
/// </summary>
public class OpenAiCompatibleMessageStreamer : IAiMessageStreamer
{
    // Streaming reads can outlast the default 100s; rely on the CancellationToken.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private const int MaxOutputTokens = 16000;

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        string apiKey,
        string model,
        string? baseUrl,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        IReadOnlyList<AiToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new AiStreamException("No base URL is set for the OpenAI-compatible provider. Add one in Settings.");

        var url = $"{baseUrl.TrimEnd('/')}/chat/completions";
        var body = BuildRequestBody(model, systemPrompt, turns, tools);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // `yield` cannot live inside try/catch — advance the reader inside try, yield outside.
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            throw new AiStreamException("Could not reach the AI provider. Check the base URL in Settings.", ex);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw MapError(response.StatusCode, errorBody);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            // Tool calls stream incrementally, keyed by index; assemble then emit on finish.
            var toolCalls = new SortedDictionary<int, ToolCallBuilder>();

            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct);
                }
                catch (Exception ex)
                {
                    throw new AiStreamException("The AI provider stream ended unexpectedly.", ex);
                }

                if (line is null) break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var payload = line["data:".Length..].Trim();
                if (payload.Length == 0) continue;
                if (payload == "[DONE]") break;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(payload); }
                catch (JsonException) { continue; } // ignore keep-alive / non-JSON frames

                using (doc)
                {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        long? input = usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number ? pt.GetInt64() : null;
                        long? output = usage.TryGetProperty("completion_tokens", out var cti) && cti.ValueKind == JsonValueKind.Number ? cti.GetInt64() : null;
                        if (input is not null || output is not null)
                            yield return new AiStreamChunk(InputTokens: input, OutputTokens: output);
                    }

                    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                        continue;

                    var choice = choices[0];

                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        {
                            var text = content.GetString();
                            if (!string.IsNullOrEmpty(text))
                                yield return new AiStreamChunk(TextDelta: text);
                        }

                        if (delta.TryGetProperty("tool_calls", out var deltaToolCalls)
                            && deltaToolCalls.ValueKind == JsonValueKind.Array)
                        {
                            AccumulateToolCalls(deltaToolCalls, toolCalls);
                        }
                    }

                    if (choice.TryGetProperty("finish_reason", out var finish)
                        && finish.ValueKind == JsonValueKind.String
                        && finish.GetString() == "tool_calls")
                    {
                        foreach (var builder in toolCalls.Values)
                        {
                            yield return new AiStreamChunk(ToolCall: new AiToolCall(
                                builder.Id ?? Guid.NewGuid().ToString(),
                                builder.Name ?? string.Empty,
                                builder.Arguments.Length == 0 ? "{}" : builder.Arguments.ToString()));
                        }
                        toolCalls.Clear();
                        yield return new AiStreamChunk(StopReason: AiStopReasons.ToolUse);
                    }
                }
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    private static void AccumulateToolCalls(JsonElement deltaToolCalls, SortedDictionary<int, ToolCallBuilder> toolCalls)
    {
        foreach (var tc in deltaToolCalls.EnumerateArray())
        {
            var index = tc.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Number ? idx.GetInt32() : 0;
            if (!toolCalls.TryGetValue(index, out var builder))
            {
                builder = new ToolCallBuilder();
                toolCalls[index] = builder;
            }

            if (tc.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                builder.Id = id.GetString();

            if (tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
            {
                if (fn.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    builder.Name = name.GetString();
                if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                    builder.Arguments.Append(args.GetString());
            }
        }
    }

    private static JsonObject BuildRequestBody(
        string model,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        IReadOnlyList<AiToolDefinition> tools)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
        };
        foreach (var turn in turns)
            AppendTurn(messages, turn);

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxOutputTokens,
            ["stream"] = true,
            // Ask compatible servers to report token usage in the stream; ignored if unsupported.
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
            ["messages"] = messages,
        };

        if (tools.Count > 0)
        {
            var toolArray = new JsonArray();
            foreach (var tool in tools)
            {
                toolArray.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.InputSchemaJson),
                    },
                });
            }
            body["tools"] = toolArray;
        }

        return body;
    }

    private static void AppendTurn(JsonArray messages, AiChatTurn turn)
    {
        // A user turn carrying tool results → one "tool" message per result.
        if (turn.ToolResults is { Count: > 0 })
        {
            foreach (var result in turn.ToolResults)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = result.ToolUseId,
                    ["content"] = result.Content,
                });
            }
            return;
        }

        // An assistant turn that requested tools.
        if (turn.ToolCalls is { Count: > 0 })
        {
            var toolCallArray = new JsonArray();
            foreach (var call in turn.ToolCalls)
            {
                toolCallArray.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.InputJson,
                    },
                });
            }
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = string.IsNullOrEmpty(turn.Content) ? null : turn.Content,
                ["tool_calls"] = toolCallArray,
            });
            return;
        }

        // A plain user/assistant turn.
        messages.Add(new JsonObject
        {
            ["role"] = turn.Role == ChatRoles.Assistant ? "assistant" : "user",
            ["content"] = turn.Content,
        });
    }

    private static AiStreamException MapError(System.Net.HttpStatusCode status, string body)
    {
        var detail = ExtractErrorMessage(body);
        return status switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                new AiStreamException("The AI provider rejected the configured API key. Check it in Settings."),
            System.Net.HttpStatusCode.NotFound =>
                new AiStreamException($"The AI provider could not find that model or endpoint. Check the model and base URL in Settings.{detail}"),
            System.Net.HttpStatusCode.TooManyRequests =>
                new AiStreamException("The AI provider rate limit was hit. Try again shortly."),
            _ => new AiStreamException($"AI provider error ({(int)status}).{detail}"),
        };
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString()
                        : error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                if (!string.IsNullOrWhiteSpace(message)) return $" {message}";
            }
        }
        catch (JsonException) { /* non-JSON body — omit detail */ }
        return string.Empty;
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
