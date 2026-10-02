using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Reminders.Tools;

/// <summary>Shared plumbing for the reminder tools.</summary>
public abstract class ReminderToolBase : IPersonaTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Reminders;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>Tools that act on an existing item override this to check it early.</summary>
    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Tools that act on an existing item name it, for the confirmation card.</summary>
    public virtual Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? GetInt(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    protected static bool GetBool(JsonElement input, string name, bool fallback = false) =>
        input.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    protected static string RequireString(JsonElement input, string name) =>
        GetString(input, name) ?? throw new ReminderValidationException($"'{name}' is required.");

    protected static int RequireInt(JsonElement input, string name) =>
        GetInt(input, name) ?? throw new ReminderValidationException($"'{name}' is required.");

    protected static string Ok(object payload) => ToolJson.Serialize(payload);
}

public class GetRemindersTool(IReminderService reminders) : ReminderToolBase
{
    public override string Name => "get_reminders";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the user's reminders with their due times. Pending only by default; " +
        "set includeCompleted to also see delivered, cancelled, and failed ones.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "includeCompleted": { "type": "boolean", "description": "Include non-pending reminders. Default false." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { reminders = await reminders.ListAsync(GetBool(input, "includeCompleted"), ct) });
}

/// <summary>
/// The links take only what the model was shown: a goal's key from get_goals, a planner item's id
/// from get_planner. At a low temperature qwen2.5:3b filled the optional "plannerItemId" with an
/// invented 123 on every try, so "remind me to call mum at 7pm tomorrow" never got a card
/// (2026-10-01, accuracy suite). Each link now says to leave it out unless the user asked for it.
/// </summary>
public class CreateReminderTool(IReminderService reminders, IGoalService goals) : ReminderToolBase
{
    public override string Name => "create_reminder";
    public override string Description =>
        "Schedules a reminder that notifies the user at a given time. 'dueAtLocal' is the " +
        "user's own wall-clock time (their time zone and today's date are in your context) — " +
        "resolve relative phrasing like 'tomorrow at 9am' into that value yourself. " +
        "Most reminders need only 'message' and 'dueAtLocal'.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "What to remind the user about." },
            "dueAtLocal": {
              "type": "string",
              "description": "Local date-time in the user's zone, ISO 8601 without offset, e.g. 2026-07-22T09:00:00."
            },
            "goalKey": {
              "type": "string",
              "description": "Only when the user ties the reminder to one of their goals: that goal's key from get_goals, e.g. \"GOAL-3\". Leave it out otherwise."
            },
            "plannerItemId": {
              "type": "integer",
              "description": "Only when the user ties the reminder to a planner item: the itemId get_planner returned for it. Leave it out otherwise; never guess an id."
            }
          },
          "required": ["message", "dueAtLocal"]
        }
        """;

    /// <summary>The goal the reminder is linked to, from its key ("GOAL-3"); null when none is given.</summary>
    private async Task<int?> GoalAsync(JsonElement input, CancellationToken ct)
    {
        var key = GetString(input, "goalKey");
        if (string.IsNullOrWhiteSpace(key)) return null;
        return await goals.ResolveKeyAsync(key, ct)
            ?? throw new ReminderValidationException(
                $"There is no goal {key}. Use a goalKey from get_goals (like \"GOAL-3\"), or leave it out.");
    }

    /// <summary>
    /// The time is the whole point of a reminder, so a card is never shown without one. A model
    /// proposed "Call mum at 7pm today" with no dueAtLocal at all: the card said when in prose
    /// and would have failed on confirm.
    /// </summary>
    /// <summary>
    /// Everything creating it checks, the time in the past included: qwen2.5:3b proposed a reminder
    /// for the day before, and the card only failed once confirmed (2026-10-01).
    /// </summary>
    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        await reminders.ValidateCreateAsync(new CreateReminderRequest(
            Message: RequireString(input, "message"),
            DueAtLocal: RequireDueAt(input),
            GoalId: await GoalAsync(input, ct),
            PlannerItemId: GetInt(input, "plannerItemId")), ct);

    private static DateTime RequireDueAt(JsonElement input)
    {
        var raw = RequireString(input, "dueAtLocal");
        return DateTime.TryParse(raw, out var local)
            ? local
            : throw new ReminderValidationException(
                "'dueAtLocal' must be an ISO date-time like 2026-07-22T09:00:00.");
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var local = RequireDueAt(input);

        return Ok(await reminders.CreateAsync(new CreateReminderRequest(
            Message: RequireString(input, "message"),
            DueAtLocal: local,
            GoalId: await GoalAsync(input, ct),
            PlannerItemId: GetInt(input, "plannerItemId")), ct));
    }
}

public class CancelReminderTool(IReminderService reminders) : ReminderToolBase
{
    public override string Name => "cancel_reminder";
    public override string Description =>
        "Cancels a pending reminder so it will not fire. Use get_reminders to find the id.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "reminderId": { "type": "integer", "description": "Reminder id to cancel." }
          },
          "required": ["reminderId"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "reminderId");
        if (await reminders.GetAsync(id, ct) is null)
            throw new ReminderValidationException($"Reminder {id} does not exist. Use an id from get_reminders.");
    }

    public override async Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        GetInt(input, "reminderId") is int id && await reminders.GetAsync(id, ct) is { } reminder
            ? $"“{reminder.Message}” at {reminder.DueAtLocal:yyyy-MM-dd HH:mm}"
            : null;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "reminderId");
        var cancelled = await reminders.CancelAsync(id, ct);
        return cancelled is null
            ? throw new ReminderValidationException($"Reminder {id} does not exist.")
            : Ok(cancelled);
    }
}
