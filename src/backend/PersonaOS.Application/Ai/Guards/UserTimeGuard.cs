using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Holds a reminder or an event to the time the user gave (<see cref="WhenPhrase"/>), and never lets a
/// reminder through with a time nobody gave.
/// <list type="bullet">
/// <item>Asked "Remind me to pay the rent", qwen2.5:3b proposed it for 18:00 tomorrow, 09:00 on
/// Friday and 19:00 on Saturday, three tries in three (accuracy suite, 2026-11-02). A reminder is
/// its time, so a call for one is answered with "ask when" unless the user said when, this message
/// or one of the last few.</item>
/// <item>When the message pins the day or the time down, the call is rewritten to it. The card still
/// shows what will be saved, so the user sees and confirms the corrected time.</item>
/// </list>
/// </summary>
public sealed class UserTimeGuard : IToolCallGuard
{
    private const string Reminder = "create_reminder";
    private const string PlannerItem = "add_planner_item";

    /// <summary>The refusal the user reads under a reply that still claims a reminder was set.</summary>
    public const string NoTimeGiven = "a reminder needs a time, and none was given.";

    public string Name => "user-time";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (call.Name == PlannerRead) return ReadTheDaySaid(call, turn);
        if (call.Name is not (Reminder or PlannerItem) || !turn.MutatingTools.Contains(call.Name)) return NoVerdict;

        JsonObject? input;
        try
        {
            input = JsonNode.Parse(call.InputJson) as JsonObject;
        }
        catch (JsonException)
        {
            return NoVerdict;
        }
        if (input is null) return NoVerdict;

        if (call.Name == Reminder
            && !WhenPhrase.SaysWhen(turn.UserMessage)
            && !turn.EarlierUserMessages.Any(WhenPhrase.SaysWhen))
        {
            turn.Refusals.Add(NoTimeGiven);
            const string ask = "The user has not said when to remind them. Do not guess a time: ask them when, in one "
                + "short question, and call create_reminder once they say.";
            return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
                JsonSerializer.Serialize(new { error = ask }), IsError: true, "reminder with no time given"));
        }

        if (turn.UserNow is not DateTime now || WhenPhrase.Read(turn.UserMessage, now) is not { } said) return NoVerdict;

        var fixedCall = call.Name == Reminder ? FixReminder(input, said) : FixEvent(input, said);
        return fixedCall is { } why
            ? Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(call with { InputJson = input.ToJsonString() }, why))
            : NoVerdict;
    }

    /// <summary>Sets dueAtLocal to the day and time the user said; null when it already was.</summary>
    private static string? FixReminder(JsonObject input, WhenPhrase.Reading said)
    {
        if (input["dueAtLocal"]?.GetValueKind() != JsonValueKind.String
            || !DateTime.TryParse(input["dueAtLocal"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var given))
            return null;

        var day = said.Day ?? DateOnly.FromDateTime(given);
        var time = said.Time ?? TimeOnly.FromDateTime(given);
        var meant = day.ToDateTime(new TimeOnly(time.Hour, time.Minute));
        if (meant == new DateTime(given.Year, given.Month, given.Day, given.Hour, given.Minute, 0)) return null;

        input["dueAtLocal"] = meant.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        return $"dueAtLocal {given:yyyy-MM-dd HH:mm} to {meant:yyyy-MM-dd HH:mm} for \"{Words(said)}\"";
    }

    /// <summary>Sets an event's date and start to what the user said, keeping its length; null when they already were.</summary>
    private static string? FixEvent(JsonObject input, WhenPhrase.Reading said)
    {
        var changes = new List<string>();
        var givenDate = input["date"]?.GetValueKind() == JsonValueKind.String ? input["date"]!.GetValue<string>() : null;
        // An end day not after the start is the model filling the field in, not a long event; the
        // tool drops it before the card. Left in, it kept this guard off the wrong day: "Thursday"
        // went through as Friday with endDate Friday (holdout3, 2026-11-02).
        if (Text(input["endDate"]) is { } endDate
            && (!DateOnly.TryParse(endDate, CultureInfo.InvariantCulture, out var last)
                || !DateOnly.TryParse(givenDate, CultureInfo.InvariantCulture, out var first) || last <= first))
        {
            input.Remove("endDate");
        }

        if (said.Day is DateOnly day && givenDate != day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && input["endDate"] is null)
        {
            input["date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            changes.Add($"date {givenDate ?? "(today)"} to {day:yyyy-MM-dd}");
        }

        if (said.Time is TimeOnly time)
        {
            var start = input["scheduledTime"]?.GetValueKind() == JsonValueKind.String
                && TimeOnly.TryParse(input["scheduledTime"]!.GetValue<string>(), CultureInfo.InvariantCulture, out var s) ? s : (TimeOnly?)null;
            if (start != time)
            {
                // An end the model gave keeps the event's length; one that would wrap past midnight is dropped.
                if (start is TimeOnly old && input["endTime"]?.GetValueKind() == JsonValueKind.String
                    && TimeOnly.TryParse(input["endTime"]!.GetValue<string>(), CultureInfo.InvariantCulture, out var end) && end > old)
                {
                    var moved = time.Add(end - old, out var wrapped);
                    if (wrapped == 0) input["endTime"] = moved.ToString("HH:mm", CultureInfo.InvariantCulture);
                    else input.Remove("endTime");
                }
                input["scheduledTime"] = time.ToString("HH:mm", CultureInfo.InvariantCulture);
                changes.Add($"start {start?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "(all day)"} to {time:HH:mm}");
            }
        }

        return changes.Count == 0 ? null : $"{string.Join(", ", changes)} for \"{Words(said)}\"";
    }

    /// <summary>
    /// A planner read for the day the user asked about. Asked "What is on my calendar tomorrow?",
    /// qwen2.5:3b read today and gave today's item as tomorrow's (accuracy suite, 2026-11-02).
    /// A range the model asked for is left alone.
    /// </summary>
    private static Task<ToolCallVerdict?> ReadTheDaySaid(AiToolCall call, ChatTurnState turn)
    {
        if (turn.UserNow is not DateTime now || WhenPhrase.Read(turn.UserMessage, now) is not { Day: DateOnly day } said) return NoVerdict;

        JsonObject? input;
        try
        {
            input = JsonNode.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson) as JsonObject;
        }
        catch (JsonException)
        {
            return NoVerdict;
        }
        if (input is null) return NoVerdict;

        var wanted = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // One day was named, so a range is narrowed to it: asked about tomorrow, qwen2.5:3b read
        // today to tomorrow and gave today's item as tomorrow's (accuracy suite, 2026-11-02).
        var range = input["from"] is not null || input["to"] is not null;
        var given = range ? $"{Text(input["from"])}..{Text(input["to"])}" : Text(input["date"]);
        // No date reads today, which is right when today is what was asked.
        if (given == wanted || (given is null && day == DateOnly.FromDateTime(now))) return NoVerdict;

        input.Remove("from");
        input.Remove("to");
        input["date"] = wanted;
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Rewrite(
            call with { InputJson = input.ToJsonString() }, $"read {given ?? "today"} as {wanted} for \"{said.DayWords}\""));
    }

    private static string? Text(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private const string PlannerRead = "get_planner";

    private static string Words(WhenPhrase.Reading said) =>
        string.Join(" ", new[] { said.DayWords, said.TimeWords }.Where(w => w.Length > 0).Distinct());

    private static readonly Task<ToolCallVerdict?> NoVerdict = Task.FromResult<ToolCallVerdict?>(null);
}
