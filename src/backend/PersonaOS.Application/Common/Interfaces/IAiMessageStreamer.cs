namespace PersonaOS.Application.Common.Interfaces;

/// <summary>A tool invocation requested by the model.</summary>
public record AiToolCall(string Id, string Name, string InputJson);

/// <summary>The outcome of executing a tool, sent back to the model.</summary>
public record AiToolResult(string ToolUseId, string Content, bool IsError = false);

/// <summary>
/// One message of model-visible conversation history. Plain turns carry only
/// <paramref name="Content"/>; an assistant turn that requested tools carries
/// <paramref name="ToolCalls"/>, and the following user turn carries the
/// matching <paramref name="ToolResults"/>.
/// </summary>
public record AiChatTurn(
    string Role,
    string Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    IReadOnlyList<AiToolResult>? ToolResults = null);

/// <summary>A tool offered to the model. InputSchemaJson is a JSON Schema object.</summary>
public record AiToolDefinition(string Name, string Description, string InputSchemaJson);

/// <summary>Provider-neutral stop reasons surfaced by <see cref="AiStreamChunk.StopReason"/>.</summary>
public static class AiStopReasons
{
    public const string ToolUse = "tool_use";
}

/// <summary>
/// A chunk of the model's streamed response. Exactly one aspect is set per chunk:
/// text to append, a completed tool call, usage figures, or the stop reason.
/// </summary>
public record AiStreamChunk(
    string? TextDelta = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    AiToolCall? ToolCall = null,
    string? StopReason = null);

/// <summary>Thrown by streamer implementations with a user-presentable message.</summary>
public class AiStreamException(string userMessage, Exception? inner = null)
    : Exception(userMessage, inner);

/// <summary>
/// LLM streaming port. Provider SDKs live in Infrastructure adapters; Application
/// only ever sees provider-neutral chunks.
/// </summary>
public interface IAiMessageStreamer
{
    IAsyncEnumerable<AiStreamChunk> StreamAsync(
        string apiKey,
        string model,
        string? baseUrl,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken ct = default);
}

/// <summary>Selects the streaming adapter for the configured provider.</summary>
public interface IAiMessageStreamerFactory
{
    /// <summary>Returns the adapter for <paramref name="provider"/> (an
    /// <c>InstanceConfig.Providers</c> value); falls back to the default provider
    /// when unrecognized.</summary>
    IAiMessageStreamer ForProvider(string provider);
}
