using System.Text;
using System.Text.Json;

namespace PersonaOS.Application.Ai;

/// <summary>A tool call the model asked for, held until the user confirms it.</summary>
public record ProposedAction(string ToolName, string InputJson);

/// <summary>
/// Turns a proposed tool call into a line the user can actually judge.
///
/// The confirm card has to say what *will* happen in the user's terms — "Create goal
/// 'Learn C#' — month, 2026-10-01" — because the point is to catch the assistant getting it
/// wrong. A model claimed to be creating a sub-goal of "Master AI" while passing no parent at
/// all; a card showing "top-level" makes that visible before it is saved, not after.
/// </summary>
public static class ProposedActionSummary
{
    /// <summary>Extra fields worth showing, with the label to print them under.</summary>
    private static readonly (string Field, string Label)[] Details =
    [
        ("taskKey", ""),
        ("goalKey", ""),
        ("dueAtLocal", ""),
        ("date", ""),
        ("scheduledTime", ""),
        ("periodType", ""),
        ("periodStart", ""),
        ("status", ""),
        ("progress", "progress "),
        ("column", "to "),
        ("sprint", "sprint "),
        ("destination", "into "),
        ("points", "points "),
    ];

    public static string Describe(string toolName, string? inputJson)
    {
        var action = HumanAction(toolName);

        JsonElement input;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            input = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return action;
        }

        if (input.ValueKind != JsonValueKind.Object) return action;

        var sb = new StringBuilder(action);

        var label = ToolPayloadText.FirstValue(input, ToolPayloadText.LabelFields);
        if (label is not null) sb.Append(" “").Append(ToolPayloadText.Truncate(label, 80)).Append('”');

        var parts = new List<string>();
        foreach (var (field, prefix) in Details)
        {
            var value = ToolPayloadText.FirstValue(input, [field]);
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(prefix + value);
        }

        // Which goal a task lands under is the detail a model has actually got wrong, so state it
        // either way rather than only when present.
        if (toolName is "create_task" && ToolPayloadText.FirstValue(input, ["goalKey"]) is null)
        {
            parts.Add("no goal");
        }

        // A confirmed card is the acknowledgement of a scope change, so the card must say so.
        if (toolName is "create_task" or "move_task"
            && (ToolPayloadText.FirstValue(input, ["destination"]) ?? ToolPayloadText.FirstValue(input, ["sprint"])) == "current")
        {
            parts.Add("a scope change if this week's sprint has started");
        }

        if (parts.Count > 0) sb.Append(" — ").Append(string.Join(" · ", parts));

        return ToolPayloadText.Truncate(sb.ToString(), 500);
    }

    /// <summary>Tool names are snake_case verbs; "create_goal" reads fine as "Create goal".</summary>
    private static string HumanAction(string toolName)
    {
        var words = toolName.Replace('_', ' ').Trim();
        if (words.Length == 0) return "Run tool";

        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
