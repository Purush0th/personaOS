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
    /// <summary>Fields that name the thing being acted on, in order of preference.</summary>
    private static readonly string[] LabelFields = ["title", "message", "task", "fileName", "name"];

    /// <summary>Extra fields worth showing, with the label to print them under.</summary>
    private static readonly (string Field, string Label)[] Details =
    [
        ("dueAtLocal", ""),
        ("date", ""),
        ("scheduledTime", ""),
        ("periodType", ""),
        ("periodStart", ""),
        ("status", ""),
        ("progress", "progress "),
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

        var label = FirstValue(input, LabelFields);
        if (label is not null) sb.Append(" “").Append(Truncate(label, 80)).Append('”');

        var parts = new List<string>();
        foreach (var (field, prefix) in Details)
        {
            var value = FirstValue(input, [field]);
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(prefix + value);
        }

        // Parentage is the detail a model has actually got wrong, so state it either way
        // rather than only when present.
        if (toolName is "create_goal")
        {
            var parent = FirstValue(input, ["parentGoalId"]);
            parts.Add(parent is null ? "top-level" : $"under goal {parent}");
        }

        if (parts.Count > 0) sb.Append(" — ").Append(string.Join(" · ", parts));

        return Truncate(sb.ToString(), 500);
    }

    /// <summary>Tool names are snake_case verbs; "create_goal" reads fine as "Create goal".</summary>
    private static string HumanAction(string toolName)
    {
        var words = toolName.Replace('_', ' ').Trim();
        if (words.Length == 0) return "Run tool";

        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string? FirstValue(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "yes",
                JsonValueKind.False => "no",
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text)) return Tidy(text);
        }

        return null;
    }

    /// <summary>ISO timestamps read badly on a one-line card; drop the 'T' and seconds.</summary>
    private static string Tidy(string value)
    {
        if (value.Contains('T') && DateTime.TryParse(value, out var parsed))
        {
            return parsed.TimeOfDay == TimeSpan.Zero
                ? parsed.ToString("yyyy-MM-dd")
                : parsed.ToString("yyyy-MM-dd HH:mm");
        }

        return value;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
