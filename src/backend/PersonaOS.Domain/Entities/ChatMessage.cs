namespace PersonaOS.Domain.Entities;

public static class ChatRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

/// <summary>A single message within a conversation, with per-message token usage.</summary>
public class ChatMessage
{
    public long Id { get; set; }

    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    /// <summary>"user" or "assistant" (see <see cref="ChatRoles"/>).</summary>
    public string Role { get; set; } = ChatRoles.User;

    public string Content { get; set; } = string.Empty;

    /// <summary>Anthropic usage for the API call that produced this message (assistant messages only).</summary>
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }

    /// <summary>
    /// True when this assistant reply says a change was made but no tool made one — even after
    /// the model was asked to correct itself. The client warns that nothing was saved, so a
    /// confident "I've set a reminder" is not taken at its word when the reminders table is empty.
    /// </summary>
    public bool UnverifiedClaim { get; set; }

    /// <summary>
    /// What the tools actually did during this turn, as a JSON array of receipts
    /// (assistant messages only; null when no tool ran). This is the app's own record —
    /// derived from real executions, never from what the model said it did — so a reply
    /// that misdescribes an action can be checked against it.
    /// </summary>
    public string? ToolActionsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
