namespace PersonaOS.Application.Ai;

/// <summary>
/// One event in the chat SSE stream.
/// Types: "start" (carries ConversationId), "delta" (carries Text),
/// "tool" (carries ToolName while a tool runs),
/// "done" (carries token usage, and Text only when the stored reply differs from the
/// streamed deltas — e.g. a leaked tool call was stripped — so clients replace the bubble),
/// "error" (carries Error).
/// </summary>
public record ChatStreamEvent(
    string Type,
    string? Text = null,
    int? ConversationId = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    string? Error = null,
    string? ToolName = null,
    IReadOnlyList<ToolReceipt>? Actions = null,
    IReadOnlyList<PendingActionDto>? Pending = null,
    bool? UnverifiedClaim = null,
    IReadOnlyList<string>? UnknownItems = null);

/// <summary>
/// A data-changing tool the assistant proposed. Nothing has happened yet: the user confirms
/// or discards it. Once confirmed, <paramref name="ResultSummary"/> says what actually ran.
/// </summary>
public record PendingActionDto(
    string Id,
    string Tool,
    string Summary,
    string Status,
    string? ResultSummary,
    bool? ResultOk);

public record ConversationSummary(
    int Id, string PublicId, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record ChatMessageDto(
    long Id, string Role, string Content, int? InputTokens, int? OutputTokens, DateTime CreatedAtUtc,
    IReadOnlyList<ToolReceipt>? ToolActions = null,
    IReadOnlyList<PendingActionDto>? PendingActions = null,
    bool UnverifiedClaim = false,
    IReadOnlyList<string>? UnknownItems = null);

public record ConversationDetail(
    int Id, string PublicId, string Title, DateTime CreatedAtUtc, IReadOnlyList<ChatMessageDto> Messages);

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

    /// <summary>
    /// Loads a conversation by its public id (as used in URLs) or, for links predating public
    /// ids, its numeric row id.
    /// </summary>
    Task<ConversationDetail?> GetConversationAsync(string idOrPublicId, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a conversation and everything hanging off it — messages, receipts
    /// and any proposals still awaiting confirmation. Returns false when it does not exist.
    /// </summary>
    Task<bool> DeleteConversationAsync(string idOrPublicId, CancellationToken ct = default);

    /// <summary>
    /// Runs a proposed data-changing action after the user confirmed it. This is the only path
    /// by which a model-requested write reaches the database. Returns null when the id is
    /// unknown; an already-resolved action is returned unchanged rather than run twice.
    /// </summary>
    Task<PendingActionDto?> ConfirmActionAsync(string actionId, CancellationToken ct = default);

    /// <summary>Marks a proposed action as declined. Nothing is executed.</summary>
    Task<PendingActionDto?> DiscardActionAsync(string actionId, CancellationToken ct = default);
}
