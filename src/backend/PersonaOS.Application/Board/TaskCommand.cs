using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>One change to one task's fields, read from the message; the fields not asked for are null.</summary>
    public sealed record TaskEdit(string TaskKey, string? Title, int? Points, string? Priority)
    {
        /// <summary>The update_task input for this change.</summary>
        public string ToJson() => JsonSerializer.Serialize(
            new { taskKey = TaskKey, title = Title, points = Points, priority = Priority }, OnlyGiven);

        private static readonly JsonSerializerOptions OnlyGiven = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        /// <summary>Whether an update_task call already asks for this change: the same task and every field asked for.</summary>
        public bool IsMetBy(string inputJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(inputJson);
                var input = doc.RootElement;
                string? Field(string name) =>
                    input.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
                        ? v.ToString()
                        : null;
                return string.Equals(Field("taskKey"), TaskKey, StringComparison.OrdinalIgnoreCase)
                    && (Title is null || string.Equals(Field("title")?.Trim(), Title, StringComparison.OrdinalIgnoreCase))
                    && (Points is null || Field("points") == Points.Value.ToString(CultureInfo.InvariantCulture))
                    && (Priority is null || string.Equals(Field("priority"), Priority, StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>"Make TASK-11 low priority", "Set TASK-11 priority to high".</summary>
    [GeneratedRegex(
        @"\b(?:make|set|mark|change|give|put|update)\s+(?<key>TASK-\d+)(?:'s)?\s+(?:priority\s+)?(?:to\s+|as\s+|at\s+)?(?:an?\s+)?(?<priority>highest|high|medium|low|lowest)(?:\s+priority)?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SetsPriority();

    /// <summary>"Set TASK-11 to 2 points", "Bump TASK-11 up to 13 points", "Change TASK-11 to 8 points".</summary>
    [GeneratedRegex(
        @"\b(?:make|set|change|give|bump|update|re-?estimate|put)\s+(?<key>TASK-\d+)(?:'s)?\s+(?:points\s+|estimate\s+)?(?:up\s+|down\s+)?(?:to\s+|at\s+|as\s+)?(?<points>\d{1,3})\s*(?:value\s+|story\s+)?(?:points?|pts)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SetsPoints();

    /// <summary>"Rename TASK-11 to Water the herbs", "Change the title of TASK-11 to …", "Change TASK-11's title to …".</summary>
    [GeneratedRegex(
        @"^\s*(?:please\s+)?(?:(?:rename|retitle)\s+(?<key>TASK-\d+)|(?:change|set|update)\s+(?:the\s+)?(?:title|name)\s+of\s+(?<key>TASK-\d+)|(?:change|set|update)\s+(?<key>TASK-\d+)'s\s+(?:title|name))\s+(?:to|as)\s+[""“']?(?<title>.+?)[""”']?[\s.!]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Renames();

    /// <summary>
    /// The edit a message plainly asks of one task: a new title, value points or priority. Null when
    /// it asks something else, or names more than one task. Told "Make TASK-11 low priority",
    /// qwen2.5:3b said the task was "not in a column" two tries in three (holdout3, 2026-11-02).
    /// </summary>
    public static TaskEdit? Edit(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (Regex.Count(message, @"\bTASK-\d+\b", RegexOptions.IgnoreCase) != 1) return null;

        if (Renames().Match(message) is { Success: true } rename && rename.Groups["title"].Value.Trim().Length > 0)
            return new TaskEdit(rename.Groups["key"].Value.ToUpperInvariant(), rename.Groups["title"].Value.Trim(), null, null);

        // A priority needs the word: "mark TASK-11 as high" could mean something else.
        if (message.Contains("priorit", StringComparison.OrdinalIgnoreCase) && SetsPriority().Match(message) is { Success: true } priority)
            return new TaskEdit(priority.Groups["key"].Value.ToUpperInvariant(), null, null, priority.Groups["priority"].Value.ToLowerInvariant());

        if (SetsPoints().Match(message) is { Success: true } points
            && int.TryParse(points.Groups["points"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return new TaskEdit(points.Groups["key"].Value.ToUpperInvariant(), null, n, null);

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
