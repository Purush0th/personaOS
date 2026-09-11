namespace PersonaOS.Domain.Entities;

public static class PendingActionStatuses
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Discarded = "discarded";

    public static readonly IReadOnlyList<string> All = [Pending, Confirmed, Discarded];
}

/// <summary>
/// A data-changing tool the assistant asked to run, held until the user says yes.
///
/// Models act without being asked — observed repeatedly: creating a goal right after the user
/// said "let's discuss before we add anything", and inventing a sub-goal nobody requested.
/// Prompt rules did not stop it, so nothing that writes runs on the model's say-so: the request
/// is recorded here, shown in the reply, and only executed on confirmation.
/// </summary>
public class PendingAction
{
    public int Id { get; set; }

    /// <summary>Opaque id used by the confirm/discard endpoints, so row ids stay internal.</summary>
    public string PublicId { get; set; } = Services.PublicIdGenerator.Next();

    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    /// <summary>The assistant message that proposed this, so the UI can show it in place.</summary>
    public long ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;

    public string ToolName { get; set; } = string.Empty;

    /// <summary>The arguments the model supplied, replayed verbatim when confirmed.</summary>
    public string InputJson { get; set; } = "{}";

    /// <summary>Human-readable description of what will happen, shown on the confirm card.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>See <see cref="PendingActionStatuses"/>.</summary>
    public string Status { get; set; } = PendingActionStatuses.Pending;

    /// <summary>Receipt of what actually happened, once confirmed and run.</summary>
    public string? ResultSummary { get; set; }

    /// <summary>False when the tool was confirmed but failed.</summary>
    public bool? ResultOk { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
