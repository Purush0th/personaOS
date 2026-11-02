using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// A call, meeting or lunch the user asked to schedule or put on their calendar is a planner item,
/// not a reminder. Told "Schedule a call with Anna on Saturday at 11am", qwen2.5:3b proposed
/// create_reminder in two runs of three (accuracy suite, 2026-11-02). When the user asked for the
/// calendar and said nothing of being reminded, the reminder is rewritten as an event at the same
/// time; <see cref="UserTimeGuard"/> then holds it to the day and time they said.
/// </summary>
public sealed partial class CalendarRequestGuard : IToolCallGuard
{
    private const string Reminder = "create_reminder";
    private const string PlannerItem = "add_planner_item";

    [GeneratedRegex(
        @"\b(?:schedule|book|(?:put|add|pencil)\b.{1,60}\b(?:on|in|to|into)\s+(?:my|the)\s+(?:calendar|planner|diary|agenda))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksForCalendar();

    [GeneratedRegex(@"\b(?:remind|reminder|alarm|ping|alert|notify|nudge|wake)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksToBeReminded();

    public string Name => "calendar-request";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name != Reminder || !turn.ToolNames.Contains(PlannerItem) || turn.UserMessage is not { } message
            || !AsksForCalendar().IsMatch(message) || AsksToBeReminded().IsMatch(message))
            return NoVerdict;

        JsonObject? input;
        try
        {
            input = JsonNode.Parse(call.InputJson) as JsonObject;
        }
        catch (JsonException)
        {
            return NoVerdict;
        }
        if (input?["message"] is not JsonValue title || title.GetValueKind() != JsonValueKind.String) return NoVerdict;

        var item = new JsonObject { ["title"] = title.GetValue<string>() };
        if (input["dueAtLocal"] is JsonValue due && due.GetValueKind() == JsonValueKind.String
            && DateTime.TryParse(due.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at))
        {
            item["date"] = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            item["scheduledTime"] = at.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
            call with { Name = PlannerItem, InputJson = item.ToJsonString() }, "asked for the calendar, not a reminder"));
    }

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
