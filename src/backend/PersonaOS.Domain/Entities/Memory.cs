namespace PersonaOS.Domain.Entities;

/// <summary>What kind of thing a memory is. Stored as the lowercase value.</summary>
public static class MemoryCategories
{
    /// <summary>A fact about the user: "has two kids", "works nights on Fridays".</summary>
    public const string Fact = "fact";

    /// <summary>How the user likes things: "prefers morning workouts", "wants short answers".</summary>
    public const string Preference = "preference";

    /// <summary>Ongoing project context: "the house move is planned for March".</summary>
    public const string Project = "project";

    /// <summary>A decision the user made: "will not take on new clients this year".</summary>
    public const string Decision = "decision";

    public static readonly IReadOnlyList<string> All = [Fact, Preference, Project, Decision];

    /// <summary>The category in the user's words: "Preference".</summary>
    public static string Label(string category) =>
        category.Length == 0 ? category : char.ToUpperInvariant(category[0]) + category[1..];
}

/// <summary>
/// One durable thing the assistant should know in later conversations: a fact, a preference,
/// project context or a decision. Stored on the server so every signed-in device shares it; the
/// user can view, edit and delete each one. Separate from "About you" (one text the user writes)
/// and from rolling chat summaries (which are about one conversation only).
/// </summary>
public class Memory
{
    public int Id { get; set; }

    /// <summary>The memory itself, one short sentence in the user's terms.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>One of <see cref="MemoryCategories"/>.</summary>
    public string Category { get; set; } = MemoryCategories.Fact;

    /// <summary>The conversation it came from, when it came from chat. Cleared if that conversation is deleted.</summary>
    public int? SourceConversationId { get; set; }
    public Conversation? SourceConversation { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
