using System.Text.RegularExpressions;

namespace PersonaOS.Application.Board;

/// <summary>
/// Reads a message that plainly moves one task by its key: "Mark TASK-11 as done", "Move TASK-4
/// into progress", "Finish TASK-2", "Start TASK-9". Used when the model answered such a message
/// with no card: qwen2.5:3b said TASK-11 was "already considered done" (it was in the backlog), or
/// proposed update_task with nothing to change (accuracy suite, 2026-11-02). The backend routes a
/// command this plain itself (docs/model-accuracy.md, FR-02); the user still confirms the card.
/// </summary>
public static partial class TaskCommand
{
    [GeneratedRegex(
        @"\b(?:mark|set|move|put|change|drag|shift)\s+(?<key>TASK-\d+)\s+(?:as\s+|to\s+|into\s+|in\s+|over\s+to\s+)?(?:the\s+)?(?<column>done|complete(?:d)?|finished|in[\s_-]?progress|to[\s-]?do|backlog)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MoveTo();

    [GeneratedRegex(@"^\s*(?:please\s+)?(?<verb>complete|finish|close|start(?:\s+working\s+on)?)\s+(?<key>TASK-\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Verb();

    /// <summary>"I've finished TASK-11", "I'm done with TASK-11", "TASK-11 is done": told this, qwen2.5:3b proposed complete_sprint (holdout2, 2026-11-02).</summary>
    [GeneratedRegex(
        @"^\s*(?:i(?:'ve|\s+have)?\s+(?:just\s+)?(?:finished|completed|done)|i'?m\s+(?:done|finished)\s+with|(?:i\s+)?finished)\s+(?<key>TASK-\d+)\b|^\s*(?<key>TASK-\d+)\s+is\s+(?:now\s+)?(?:done|finished|complete(?:d)?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SaysDone();

    /// <summary>The task key and the move_task column the message asks for, or null when it does not plainly ask for one.</summary>
    public static (string TaskKey, string Column)? Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        // "Move TASK-4 to done and TASK-5 to review" is two commands: the model handles those.
        if (Regex.Count(message, @"\bTASK-\d+\b", RegexOptions.IgnoreCase) != 1) return null;

        if (MoveTo().Match(message) is { Success: true } move)
            return (move.Groups["key"].Value.ToUpperInvariant(), Column(move.Groups["column"].Value));

        if (SaysDone().Match(message) is { Success: true } done)
            return (done.Groups["key"].Value.ToUpperInvariant(), "done");

        if (Verb().Match(message) is { Success: true } verb)
        {
            var column = verb.Groups["verb"].Value.StartsWith("start", StringComparison.OrdinalIgnoreCase) ? "in_progress" : "done";
            return (verb.Groups["key"].Value.ToUpperInvariant(), column);
        }

        return null;
    }

    private static string Column(string said)
    {
        var word = Regex.Replace(said.ToLowerInvariant(), @"[\s_-]", string.Empty);
        return word switch
        {
            "inprogress" => "in_progress",
            "todo" => "todo",
            "backlog" => "backlog",
            _ => "done",
        };
    }
}
