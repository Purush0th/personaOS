using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Planner.Tools;

/// <summary>Shared plumbing for the planner tools: JSON options and input helpers.</summary>
public abstract class PlannerToolBase : IPersonaTool
{
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Planner;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

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

    protected static string Ok(object payload) => JsonSerializer.Serialize(payload, JsonOpts);
}

public class GetPlannerTool(IPlannerService planner) : PlannerToolBase
{
    public override string Name => "get_planner";
    public override bool Mutates => false;
    public override string Description =>
        "Reads the user's daily planner. Pass 'date' for a single day, or 'from'+'to' for a range " +
        "(e.g. a week). Each item has a status (planned/done/skipped) and may link to a goal.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "Single day, ISO date (e.g. 2026-07-21)." },
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

        var date = GetDate(input, "date")
            ?? throw new PlannerValidationException("Provide 'date', or both 'from' and 'to'.");
        return Ok(await planner.GetDayAsync(date, ct));
    }
}

public class AddPlannerItemTool(
    IPlannerService planner,
    PersonaOS.Application.Goals.IGoalService goals,
    PersonaOS.Application.Board.IBoardService board) : PlannerToolBase
{
    public override string Name => "add_planner_item";
    public override string Description =>
        "Adds an item to the user's planner for a given day. Optionally schedule a time. To plan a day's " +
        "work on a sprint-board task, pass its taskKey (like \"TASK-7\" from get_board); the title can then " +
        "be left out. Otherwise goalKey (like \"GOAL-3\") links the item to a goal.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "What the task is." },
            "date": { "type": "string", "description": "Day to plan it for, ISO date (e.g. 2026-07-21)." },
            "notes": { "type": "string", "description": "Optional detail." },
            "scheduledTime": { "type": "string", "description": "Optional 24-hour time, e.g. 09:30." },
            "taskKey": { "type": "string", "description": "Optional sprint-board task this is a day's work on." },
            "goalKey": { "type": "string", "description": "Optional goal this item contributes to." }
          },
          "required": ["date"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
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

        return Ok(await planner.CreateAsync(new CreatePlannerItemRequest(
            Title: GetString(input, "title") ?? string.Empty,
            Date: RequireDate(input, "date"),
            Notes: GetString(input, "notes"),
            ScheduledTime: GetTime(input, "scheduledTime"),
            GoalId: goalId,
            TaskId: taskId), ct));
    }
}

public class UpdatePlannerItemStatusTool(IPlannerService planner) : PlannerToolBase
{
    public override string Name => "update_planner_item_status";
    public override string Description =>
        "Marks a planner task as done, skipped, or back to planned. Use get_planner to find the item id.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "itemId": { "type": "integer", "description": "Planner item id." },
            "status": { "type": "string", "enum": ["planned", "done", "skipped"] }
          },
          "required": ["itemId", "status"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "itemId");
        var updated = await planner.UpdateStatusAsync(id, RequireString(input, "status"), ct);
        return updated is null
            ? throw new PlannerValidationException($"Planner item {id} does not exist.")
            : Ok(updated);
    }
}

public class MovePlannerItemTool(IPlannerService planner) : PlannerToolBase
{
    public override string Name => "move_planner_item";
    public override string Description =>
        "Moves a planner task to a different day (e.g. pushing today's unfinished task to tomorrow).";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "itemId": { "type": "integer", "description": "Planner item id." },
            "date": { "type": "string", "description": "New day, ISO date (e.g. 2026-07-22)." }
          },
          "required": ["itemId", "date"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = RequireInt(input, "itemId");
        var moved = await planner.MoveAsync(id, RequireDate(input, "date"), ct);
        return moved is null
            ? throw new PlannerValidationException($"Planner item {id} does not exist.")
            : Ok(moved);
    }
}
