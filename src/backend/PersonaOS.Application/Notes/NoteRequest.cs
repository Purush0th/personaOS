using System.Text.RegularExpressions;

namespace PersonaOS.Application.Notes;

/// <summary>
/// Reads a chat message that plainly asks for a note: "Add a note to cook today", "Can you add a
/// note to meal prep today?", "Note: buy milk", "Note down that the car needs a service", "Jot
/// down call the plumber". Used when the model answered such a message without saving the note:
/// in a conversation where it had said "I've noted that" before, qwen2.5:3b kept saying it and
/// never called the tool, even when asked to (owner, 2026-10-11).
/// </summary>
public static partial class NoteRequest
{
    [GeneratedRegex(
        @"^\s*(?:(?:hey|ok|okay)[,\s]+)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?"
        + @"(?:(?:add|make|create|take|write|jot)\s+(?:down\s+)?(?:a\s+)?(?:quick\s+)?notes?(?:\s+down)?\s*(?:to|that|:|-|–|saying|about)?"
        + @"|notes?(?:\s+down)?\s*(?:that|to|:|-|–)"
        + @"|jot\s+down)"
        + @"\s+(?<text>.+?)[\s?.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AsksForANote();

    /// <summary>"Add a note to my planner", "add a note to TASK-5": about another module, not a note.</summary>
    [GeneratedRegex(@"^(?:my|the|this|that)\s+(?:planner|board|task|goal|reminder|calendar|sprint|backlog)\b|\b(?:TASK|GOAL|SPRINT)-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex AboutSomethingElse();

    /// <summary>What to note, starting with a capital ("Cook today"), or null when the message does not plainly ask for a note.</summary>
    public static string? TextOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var match = AsksForANote().Match(message);
        if (!match.Success) return null;

        var text = match.Groups["text"].Value.Trim();
        if (text.StartsWith("please ", StringComparison.OrdinalIgnoreCase)) text = text[7..].TrimStart();
        if (text.Length == 0 || AboutSomethingElse().IsMatch(text)) return null;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
