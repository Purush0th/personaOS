namespace PersonaOS.Application.Common.Interfaces;

/// <summary>One message of model-visible conversation history.</summary>
public record AiChatTurn(string Role, string Content);

/// <summary>
/// A chunk of the model's streamed response. Exactly one aspect is set per chunk:
/// text to append, or usage figures reported by the provider.
/// </summary>
public record AiStreamChunk(string? TextDelta = null, long? InputTokens = null, long? OutputTokens = null);

/// <summary>Thrown by streamer implementations with a user-presentable message.</summary>
public class AiStreamException(string userMessage, Exception? inner = null)
    : Exception(userMessage, inner);

/// <summary>
/// LLM streaming port. The Anthropic SDK adapter lives in Infrastructure;
/// Application only sees provider-neutral chunks.
/// </summary>
public interface IAiMessageStreamer
{
    IAsyncEnumerable<AiStreamChunk> StreamAsync(
        string apiKey,
        string model,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken ct = default);
}
