namespace PersonaOS.Application.Ai;

/// <summary>
/// One event in the chat SSE stream.
/// Types: "start" (carries ConversationId), "delta" (carries Text),
/// "done" (carries token usage), "error" (carries Error).
/// </summary>
public record ChatStreamEvent(
    string Type,
    string? Text = null,
    int? ConversationId = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    string? Error = null);

public record ConversationSummary(int Id, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record ChatMessageDto(
    long Id, string Role, string Content, int? InputTokens, int? OutputTokens, DateTime CreatedAtUtc);

public record ConversationDetail(
    int Id, string Title, DateTime CreatedAtUtc, IReadOnlyList<ChatMessageDto> Messages);

public interface IChatService
{
    /// <summary>
    /// Sends a user message in a conversation (creating one when null) and streams
    /// the assistant's reply. Persists both sides of the exchange with token usage.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        int? conversationId,
        string userMessage,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(CancellationToken ct = default);

    Task<ConversationDetail?> GetConversationAsync(int id, CancellationToken ct = default);
}
