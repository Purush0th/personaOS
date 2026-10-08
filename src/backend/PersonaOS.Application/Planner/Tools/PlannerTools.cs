using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Planner.Tools;

/// <summary>Shared plumbing for the planner tools: JSON options and input helpers.</summary>
public abstract class PlannerToolBase : IPersonaTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Planner;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>Tools that act on an existing item override this to check it early.</summary>
    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    public virtual Task<string> CompleteInputAsync(JsonElement input, CancellationToken ct = default) =>
        Task.FromResult(input.GetRawText());

    /// <summary>Tools that act on an existing item name it, for the confirmation card.</summary>
    public virtual Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Checks that the 'itemId' in the input names a planner item that exists and, for an event that
    /// repeats, an 'occurrenceDate' that is one of its days: caught here, not after the user confirms.
    /// </summary>
    protected static async Task RequirePlannerItemAsync(
        IPlannerService planner, JsonElement input, CancellationToken ct)
    {
        var id = RequireInt(input, "itemId");
        var occurrence = GetDate(input, "occurrenceDate");
        var item = await planner.GetAsync(id, occurrence, ct)
            ?? throw new PlannerValidationException(occurrence is null
                ? $"Planner item {id} does not exist. Use an id from get_planner."
                : $"Planner item {id} has no occurrence on {occurrence:yyyy-MM-dd}. Use an id and occurrenceDate from get_planner.");
        if (item.Recurrence is not null && occurrence is null)
            throw new PlannerValidationException(
                $"“{item.Title}” repeats ({item.RecurrenceText}): pass occurrenceDate, the date of the day you mean, from get_planner.");
    }

    /// <summary>"“Gym” on 2026-09-23", for a confirmation card; null when the id names no item.</summary>
    protected static async Task<string?> DescribePlannerItemAsync(IPlannerService planner, JsonElement input, CancellationToken ct) =>
        GetInt(input, "itemId") is int id && await planner.GetAsync(id, GetDate(input, "occurrenceDate"), ct) is { } item
            ? $"“{item.Title}” on {item.Date:yyyy-MM-dd}"
            : null;

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? GetInt(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    protected static DateOnly RequireDate(JsonElement input, string name)
    {
        var raw = GetString(input, name)
            ?? throw new PlannerValidationException($"'{name}' is required (ISO date, e.g. 2026-07-21).");
        return DateOnly.TryParse(raw, out var date)
            ? date
            : throw new PlannerValidationException($"'{name}' must be an ISO date like 2026-07-21.");
    }

    protected static DateOnly? GetDate(JsonElement input, string name)
    {
        var raw = GetString(input, name);
        if (raw is null) return null;
        return DateOnly.TryParse(raw, out var date)
            ? date
            : throw new PlannerValidationException($"'{name}' must be an ISO date like 2026-07-21.");
    }

    protected static TimeOnly? GetTime(JsonElement input, string name)
    {
        var raw = GetString(input, name);
        if (raw is null) return null;
        return TimeOnly.TryParse(raw, out var time)
            ? time
            : throw new PlannerValidationException($"'{name}' must be a 24-hour time like 09:30.");
    }

    protected static int RequireInt(JsonElement input, string name) =>
        GetInt(input, name) ?? throw new PlannerValidationException($"'{name}' is required.");

    protected static string RequireString(JsonElement input, string name) =>
        GetString(input, name) ?? throw new PlannerValidationException($"'{name}' is required.");

    protected static string Ok(object payload) => ToolJson.Serialize(payload);
}

public class GetPlannerTool(IPlannerService planner, IInstanceConfigService configService) : PlannerToolBase
{
    public override string Name => "get_planner";
    public override bool Mutates => false;
    public override string Description =>
        "THE TOOL FOR \"what are my tasks today\", \"what am I doing today\", \"today's plan\". " +
        "Reads the user's daily planner: what they intend to do on a given day. Call it with no " +
        "arguments for today. Pass 'date' for another single day, or 'from'+'to' for a range " +
        "(e.g. a week). Dates are the user's local dates. Each item has a status " +
        "(planned/done/skipped) and may link to a goal. A timed item has a scheduledTime and endTime; one " +
        "without is all day. An item over several days has an endDate after its date and is listed once. An item that repeats shows recurrenceText (like \"Weekly on Saturday\"); " +
        "each day it falls on is its own entry with an occurrenceDate. NOT the backlog or the sprint " +
        "board: for those use get_plan or get_board.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "Single day, ISO date (e.g. 2026-07-21). Omit for today." },
            "from": { "type": "string", "description": "Range start, ISO date. Use with 'to'." },
            "to": { "type": "string", "description": "Range end (inclusive), ISO date. Use with 'from'." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var from = GetDate(input, "from");
        var to = GetDate(input, "to");
        if (from is not null || to is not null)
        {
            if (from is null || to is null)
                throw new PlannerValidationException("Provide both 'from' and 'to' for a range, or just 'date'.");
            return Ok(new { days = await planner.GetRangeAsync(from.Value, to.Value, ct) });
        }

        // No date means today. Refusing instead sent the model round the loop guessing: five
        // failed calls, then a date from 2023, when "what are my tasks for today?" has one answer.
        var date = GetDate(input, "date")
            ?? Common.UserClock.Today((await configService.GetOrCreateAsync(ct)).TimeZone);
        return Ok(await planner.GetDayAsync(date, ct));
    }
}

/// <summary>
/// The day is optional and means today: asked to plan something "for today", qwen2.5:3b sometimes
/// passed tomorrow's date (2026-10-01, accuracy suite). A day the model does not have to work out
/// is a day it cannot get wrong. The default is filled in before the card, so the card shows it.
/// </summary>
public class AddPlannerItemTool(
    IPlannerService planner,
    PersonaOS.Application.Goals.IGoalService goals,
    PersonaOS.Application.Board.IBoardService board,
    IInstanceConfigService configService) : PlannerToolBase
{
    public override string Name => "add_planner_item";
    public override string Description =>
        "Adds an event to the user's planner for a given day: all day, or from scheduledTime to endTime. " +
        "An event over several days (a trip, a conference) also has an endDate. " +
        "A timed event notifies the user's phone 30 minutes before unless notifyBeforeMinutes says " +
        "otherwise. It can repeat. To plan a day's work on a sprint-board task, pass its taskKey (like " +
        "\"TASK-7\" from get_board); the title can then be left out. Otherwise goalKey (like " +
        "\"GOAL-3\") links the item to a goal.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "What the task is." },
            "date": { "type": "string", "description": "Leave it out for today. For another day, its ISO date (e.g. 2026-07-21), copied from the dates in your instructions." },
            "notes": { "type": "string", "description": "Optional detail." },
            "scheduledTime": { "type": "string", "description": "Optional start, 24-hour time, e.g. 09:30. Leave it out for an all-day item." },
            "endTime": { "type": "string", "description": "Optional end, 24-hour time later than scheduledTime, e.g. 10:30. Leave it out unless the user gave one." },
            "endDate": { "type": "string", "description": "Only for an event over several days: its last day, ISO date after 'date'. With a scheduledTime it needs an endTime (on that day)." },
            "repeat": { "type": "string", "enum": ["none", "daily", "weekly", "weekdays", "monthly", "yearly"], "description": "none (leave it out) unless the user asked for it to repeat. weekdays is Monday to Friday." },
            "repeatUntil": { "type": "string", "description": "Optional last day of the repeat, ISO date. Only with repeat." },
            "notifyBeforeMinutes": { "type": "integer", "description": "Minutes before the start to notify the phone. Leave it out for the usual 30; -1 for no notification." },
            "taskKey": { "type": "string", "description": "Optional sprint-board task this is a day's work on." },
            "goalKey": { "type": "string", "description": "Optional goal this item contributes to." }
          }
        }
        """;

    private async Task<DateOnly> DayAsync(JsonElement input, CancellationToken ct) =>
        GetDate(input, "date") ?? Common.UserClock.Today((await configService.GetOrCreateAsync(ct)).TimeZone);

    /// <summary>Repeat words that mean it does not repeat; qwen2.5:3b fills the field with them.</summary>
    private static readonly string[] NoRepeat = ["", "none", "never", "no", "once", "does not repeat", "not", "null"];

    public override async Task<string> CompleteInputAsync(JsonElement input, CancellationToken ct = default)
    {
        var completed = JsonNode.Parse(input.GetRawText())!.AsObject();

        // A small model fills optional fields it was not asked for: an empty time, an end with no
        // start, a repeat of "none". Refusing those made it ask the user for a start time or a
        // period instead of proposing "Buy milk" (accuracy suite, 2026-10-07: 8/10). They are
        // dropped here, before the card, so the card shows only what will be saved.
        foreach (var field in new[] { "scheduledTime", "endTime", "endDate", "repeatUntil", "notes", "goalKey", "taskKey" })
        {
            if (completed[field] is JsonValue v && v.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
                completed.Remove(field);
        }
        if (GetString(input, "repeat") is { } repeat && NoRepeat.Contains(repeat.Trim().ToLowerInvariant()))
            completed.Remove("repeat");
        if (!completed.ContainsKey("scheduledTime")) completed.Remove("endTime");
        if (!completed.ContainsKey("repeat")) completed.Remove("repeatUntil");
        input = JsonDocument.Parse(completed.ToJsonString()).RootElement.Clone();

        var day = await DayAsync(input, ct);
        // A last day that is not after the first is the model filling the field in, not a long event.
        if (GetString(input, "endDate") is { } last && (!DateOnly.TryParse(last, CultureInfo.InvariantCulture, out var lastDay) || lastDay <= day))
            completed.Remove("endDate");
        if (GetString(input, "date") is null)
            completed["date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // A timed event gets the usual heads-up unless one was chosen, so the card shows it.
        if (GetInt(input, "notifyBeforeMinutes") is null && GetString(input, "scheduledTime") is not null)
            completed["notifyBeforeMinutes"] = PlannerEvents.DefaultNotifyBeforeMinutes;
        return completed.ToJsonString();
    }

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        // An unreadable day is what the card would promise wrongly, so it is caught here rather
        // than after the user has confirmed it.
        if (GetString(input, "date") is not null) RequireDate(input, "date");
        // The times, the alert and the repeat are checked here so a card that could not be saved is
        // never shown.
        var day = await DayAsync(input, ct);
        var start = GetTime(input, "scheduledTime");
        var end = GetTime(input, "endTime");
        var lastDay = GetDate(input, "endDate");
        if (end is not null && start is null)
            throw new PlannerValidationException("'endTime' needs a 'scheduledTime'; leave both out for an all-day item.");
        if (lastDay is DateOnly l && l < day)
            throw new PlannerValidationException("'endDate' is the last day: on or after 'date'.");
        var multiDay = lastDay is DateOnly over && over > day;
        if (multiDay && start is not null && end is null)
            throw new PlannerValidationException("An event over several days with a 'scheduledTime' needs an 'endTime' on its 'endDate'.");
        if (!multiDay && end is TimeOnly e && start is TimeOnly s && e <= s)
            throw new PlannerValidationException("'endTime' must be later than 'scheduledTime'.");
        _ = RepeatRule(input, day);
        if (GetInt(input, "notifyBeforeMinutes") is int minutes && minutes != -1) CheckNotify(minutes);
        // Something to do: a title, or a board task that lends its own. qwen2.5:3b proposed
        // {"date":"2026-10-01"} and nothing else, a card that would only fail once confirmed
        // (accuracy suite, 2026-10-01).
        if (string.IsNullOrWhiteSpace(GetString(input, "title")) && GetString(input, "taskKey") is null)
            throw new PlannerValidationException("'title' is required: what the user plans to do (or a taskKey from get_board).");
        await ResolveLinksAsync(input, ct);
    }

    private static void CheckNotify(int minutes)
    {
        if (minutes is < 0 or > PlannerEvents.MaxNotifyBeforeMinutes)
            throw new PlannerValidationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'notifyBeforeMinutes' must be 0 to {PlannerEvents.MaxNotifyBeforeMinutes:N0} (at most 4 weeks), or -1 for no notification."));
    }

    /// <summary>The repeat as rule text from the simple words the model is given, or null when it does not repeat.</summary>
    private static string? RepeatRule(JsonElement input, DateOnly day)
    {
        var until = GetDate(input, "repeatUntil");
        var repeat = GetString(input, "repeat");
        if (repeat is not null && NoRepeat.Contains(repeat.Trim().ToLowerInvariant())) repeat = null;
        if (repeat is null)
        {
            if (until is not null) throw new PlannerValidationException("'repeatUntil' only goes with 'repeat'.");
            return null;
        }

        var frequency = repeat.Trim().ToLowerInvariant() switch
        {
            "daily" => "FREQ=DAILY",
            "weekly" => "FREQ=WEEKLY",
            "weekdays" => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
            "monthly" => "FREQ=MONTHLY",
            "yearly" => "FREQ=YEARLY",
            _ => throw new PlannerValidationException("'repeat' must be daily, weekly, weekdays, monthly or yearly."),
        };
        if (until is DateOnly last && last < day)
            throw new PlannerValidationException("'repeatUntil' is before the first day.");
        return until is DateOnly end
            ? string.Create(CultureInfo.InvariantCulture, $"{frequency};UNTIL={end:yyyyMMdd}")
            : frequency;
    }

    /// <summary>Turns the optional task and goal keys into ids, refusing keys that name nothing.</summary>
    private async Task<(int? TaskId, int? GoalId)> ResolveLinksAsync(JsonElement input, CancellationToken ct)
    {
        int? taskId = null;
        if (GetString(input, "taskKey") is { } taskKey)
        {
            taskId = await board.ResolveTaskKeyAsync(taskKey, ct)
                ?? throw new PlannerValidationException($"There is no task {taskKey}. Use a key from get_board.");
        }

        int? goalId = null;
        if (GetString(input, "goalKey") is { } goalKey)
        {
            goalId = await goals.ResolveKeyAsync(goalKey, ct)
                ?? throw new PlannerValidationException($"There is no goal {goalKey}. Use a key from get_goals.");
        }

        return (taskId, goalId);
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var (taskId, goalId) = await ResolveLinksAsync(input, ct);
        var day = await DayAsync(input, ct);
        var notify = GetInt(input, "notifyBeforeMinutes");

        return Ok(await planner.CreateAsync(new CreatePlannerItemRequest(
            Title: GetString(input, "title") ?? string.Empty,
            Date: day,
            Notes: GetString(input, "notes"),
            ScheduledTime: GetTime(input, "scheduledTime"),
            GoalId: goalId,
            TaskId: taskId,
            EndTime: GetTime(input, "endTime"),
            NotifyBeforeMinutes: notify == -1 ? null : notify,
            Recurrence: RepeatRule(input, day),
            EndDate: GetDate(input, "endDate")), ct));
    }
}

public class UpdatePlannerItemStatusTool(IPlannerService planner) : PlannerToolBase
{
    public override string Name => "update_planner_item_status";
    public override string Description =>
        "Marks a planner task as done, skipped, or back to planned. Use get_planner to find the item id. " +
        "For an item that repeats, also pass its occurrenceDate from get_planner.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "itemId": { "type": "integer", "description": "Planner item id." },
            "occurrenceDate": { "type": "string", "description": "Only for an item that repeats: the occurrenceDate shown by get_planner." },
            "status": { "type": "string", "enum": ["planned", "done", "skipped"] }
          },
          "required": ["itemId", "status"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        RequirePlannerItemAsync(planner, input, ct);

    public override Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        DescribePlannerItemAsync(planner, input, ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "itemId");
        var updated = await planner.UpdateStatusAsync(id, RequireString(input, "status"), GetDate(input, "occurrenceDate"), ct);
        return updated is null
            ? throw new PlannerValidationException($"Planner item {id} does not exist.")
            : Ok(updated);
    }
}

public class MovePlannerItemTool(IPlannerService planner) : PlannerToolBase
{
    public override string Name => "move_planner_item";
    public override string Description =>
        "Moves a planner task to a different day (e.g. pushing today's unfinished task to tomorrow). " +
        "For an item that repeats this moves just that day, named by its occurrenceDate from get_planner.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "itemId": { "type": "integer", "description": "Planner item id." },
            "occurrenceDate": { "type": "string", "description": "Only for an item that repeats: the occurrenceDate shown by get_planner." },
            "date": { "type": "string", "description": "New day, ISO date (e.g. 2026-07-22)." }
          },
          "required": ["itemId", "date"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        RequirePlannerItemAsync(planner, input, ct);

    public override Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        DescribePlannerItemAsync(planner, input, ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "itemId");
        var occurrence = GetDate(input, "occurrenceDate");
        var moved = await planner.MoveAsync(
            id, RequireDate(input, "date"), occurrence is null ? null : new PlannerTarget(PlannerEditScope.This, occurrence), ct);
        return moved is null
            ? throw new PlannerValidationException($"Planner item {id} does not exist.")
            : Ok(moved);
    }
}
