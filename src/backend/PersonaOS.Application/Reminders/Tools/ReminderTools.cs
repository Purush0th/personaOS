using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Reminders.Tools;

/// <summary>Shared plumbing for the reminder tools.</summary>
public abstract class ReminderToolBase : IPersonaTool
{
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Reminders;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>Tools that act on an existing item override this to check it early.</summary>
    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

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

    protected static string Ok(object payload) => JsonSerializer.Serialize(payload, JsonOpts);
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

public class CreateReminderTool(IReminderService reminders) : ReminderToolBase
{
    public override string Name => "create_reminder";
    public override string Description =>
        "Schedules a reminder that notifies the user at a given time. 'dueAtLocal' is the " +
        "user's own wall-clock time (their time zone and today's date are in your context) — " +
        "resolve relative phrasing like 'tomorrow at 9am' into that value yourself. " +
        "Optionally link it to a goal or planner item.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "What to remind the user about." },
            "dueAtLocal": {
              "type": "string",
              "description": "Local date-time in the user's zone, ISO 8601 without offset, e.g. 2026-07-22T09:00:00."
            },
            "goalId": { "type": "integer", "description": "Optional related goal id." },
            "plannerItemId": { "type": "integer", "description": "Optional related planner item id." }
          },
          "required": ["message", "dueAtLocal"]
        }
        """;

    /// <summary>
    /// The time is the whole point of a reminder, so a card is never shown without one. A model
    /// proposed "Call mum at 7pm today" with no dueAtLocal at all: the card said when in prose
    /// and would have failed on confirm.
    /// </summary>
    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        RequireDueAt(input);
        return Task.CompletedTask;
    }

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
            GoalId: GetInt(input, "goalId"),
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

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "reminderId");
        var cancelled = await reminders.CancelAsync(id, ct);
        return cancelled is null
            ? throw new ReminderValidationException($"Reminder {id} does not exist.")
            : Ok(cancelled);
    }
}
