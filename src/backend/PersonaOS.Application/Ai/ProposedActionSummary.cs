using System.Text;
using System.Text.Json;

namespace PersonaOS.Application.Ai;

/// <summary>
/// A tool call the model asked for, held until the user confirms it. <paramref name="Target"/>
/// names the existing item it acts on, looked up when it was proposed.
/// </summary>
public record ProposedAction(string ToolName, string InputJson, string? Target = null);

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
        ("sprintKey", "to "),
        ("name", ""),
        ("dueAtLocal", ""),
        ("date", ""),
        ("scheduledTime", ""),
        ("periodType", ""),
        ("periodStart", "from "),
        ("parentKey", "under "),
        ("status", ""),
        ("progress", "progress "),
        ("column", "to "),
        ("sprint", "sprint "),
        ("destination", "into "),
        ("points", "points "),
        ("priority", "priority "),
    ];

    public static string Describe(string toolName, string? inputJson, string? target = null)
    {
        var action = ToolLabel.Action(toolName);

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

        // The item the call acts on, by name, comes first: it is what a wrong id gets wrong.
        if (target is not null)
        {
            sb.Append(' ').Append(ToolPayloadText.Truncate(target, 120));
        }
        else if (ToolPayloadText.FirstValue(input, ToolPayloadText.LabelFields) is { } label)
        {
            sb.Append(" “").Append(ToolPayloadText.Truncate(label, 80)).Append('”');
        }

        var parts = new List<string>();
        foreach (var (field, prefix) in Details)
        {
            var value = ToolPayloadText.FirstValue(input, [field]);
            // A key the target already names ("TASK-7 “File taxes”") is not repeated.
            if (!string.IsNullOrWhiteSpace(value) && target?.Contains(value, StringComparison.OrdinalIgnoreCase) != true)
            {
                parts.Add(prefix + value);
            }
        }

        // A goal's calendar slot is what the user is agreeing to: "Q4 2026", "Oct 2026".
        if (toolName is "create_goal" or "move_goal" && Slot(input) is { } slot) parts.Add(slot);

        // A goal leaving its parent says so; "Move goal GOAL-5" alone would not.
        if (toolName is "move_goal" && ToolPayloadText.FirstValue(input, ["parentKey"]) is null)
        {
            parts.Add("detached, standalone");
        }

        // What happens to a deleted goal's tasks is the part the user must not miss.
        if (toolName is "delete_goal")
        {
            parts.Add(ToolPayloadText.FirstValue(input, ["taskAction"])?.ToLowerInvariant() switch
            {
                "delete" => "its tasks are deleted too",
                "reassign" => "its tasks move to other goals",
                _ => "its tasks are kept without a goal",
            });
        }

        // Which goal a task lands under is the detail a model has actually got wrong, so state it
        // either way rather than only when present.
        if (toolName is "create_task" && ToolPayloadText.FirstValue(input, ["goalKey"]) is null)
        {
            parts.Add("no goal");
        }

        // A confirmed card is the acknowledgement of a scope change, so the card must say so.
        if (toolName is "create_task" or "move_task" && ToolPayloadText.FirstValue(input, ["sprintKey"]) is not null)
        {
            parts.Add("a scope change if that sprint has started");
        }

        if (parts.Count > 0) sb.Append(" — ").Append(string.Join(" · ", parts));

        return ToolPayloadText.Truncate(sb.ToString(), 500);
    }

    /// <summary>"Q4 2026" or "Oct 2026" from a goal call's quarter or month and year; null when absent.</summary>
    private static string? Slot(JsonElement input)
    {
        var year = ToolPayloadText.FirstValue(input, ["year"]);
        if (int.TryParse(ToolPayloadText.FirstValue(input, ["quarter"])?.TrimStart('Q', 'q'), out var quarter) && quarter is >= 1 and <= 4)
            return $"Q{quarter} {year}".Trim();
        if (MonthOf(ToolPayloadText.FirstValue(input, ["month"])) is int month)
            return $"{Months.GetAbbreviatedMonthName(month)} {year}".Trim();
        // A model that gave a start day instead of a slot still picked one: that day's.
        if (ToolPayloadText.FirstValue(input, ["periodType"]) is "quarter" or "month"
            && DateOnly.TryParse(ToolPayloadText.FirstValue(input, ["periodStart"]), out var day))
            return Domain.Services.GoalCalendar.Label(ToolPayloadText.FirstValue(input, ["periodType"])!, day);
        return year;
    }

    private static System.Globalization.DateTimeFormatInfo Months => System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat;

    /// <summary>1-12 from "11", "November" or "Nov"; the goal tools read it the same way.</summary>
    private static int? MonthOf(string? text)
    {
        if (int.TryParse(text, out var number)) return number is >= 1 and <= 12 ? number : null;
        if (text is null || text.Trim().Length < 3) return null;
        var index = Array.FindIndex(Months.MonthNames, m => m.StartsWith(text.Trim()[..3], StringComparison.OrdinalIgnoreCase));
        return index is >= 0 and < 12 ? index + 1 : null;
    }
}
