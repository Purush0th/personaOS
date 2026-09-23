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
/// How to run the model, beyond which one. Each adapter applies what its provider supports and
/// ignores the rest: only a local server can be told the context size, and only some models think.
/// </summary>
/// <param name="ContextTokens">Context window to load the model with (Ollama's <c>num_ctx</c>).</param>
/// <param name="Think">For models that reason before answering: false turns it off, null leaves the default.</param>
/// <param name="KeepAlive">How long a local server keeps the model loaded after a request, e.g. "30m".</param>
public record AiModelOptions(int? ContextTokens = null, bool? Think = null, string? KeepAlive = null)
{
    public static readonly AiModelOptions Default = new();
}

/// <summary>One call to the model: who to ask, what they have been told, and what they may use.</summary>
public record AiRequest(
    string ApiKey,
    string Model,
    string? BaseUrl,
    string SystemPrompt,
    IReadOnlyList<AiChatTurn> Turns,
    IReadOnlyList<AiToolDefinition> Tools,
    AiModelOptions? Options = null)
{
    public AiModelOptions ModelOptions => Options ?? AiModelOptions.Default;
}

/// <summary>
/// LLM streaming port. Provider SDKs live in Infrastructure adapters; Application
/// only ever sees provider-neutral chunks.
/// </summary>
public interface IAiMessageStreamer
{
    IAsyncEnumerable<AiStreamChunk> StreamAsync(AiRequest request, CancellationToken ct = default);
}

/// <summary>Selects the streaming adapter for the configured provider.</summary>
public interface IAiMessageStreamerFactory
{
    /// <summary>Returns the adapter for <paramref name="provider"/> (an
    /// <c>InstanceConfig.Providers</c> value); falls back to the default provider
    /// when unrecognized.</summary>
    IAiMessageStreamer ForProvider(string provider);
}
