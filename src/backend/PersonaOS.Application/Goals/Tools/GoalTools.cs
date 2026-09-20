using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Goals.Tools;

/// <summary>Shared plumbing for the goal tools: JSON options and input helpers.</summary>
public abstract class GoalToolBase : IPersonaTool
{
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public virtual string? RequiredFeature => InstanceConfig.Modules.Goals;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>Tools that act on an existing goal override this to resolve its key early.</summary>
    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? GetInt(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    /// <summary>A key written as "GOAL-3", or a model passing the bare number 3.</summary>
    protected static string? GetKey(JsonElement input, string name) =>
        GetString(input, name) ?? GetInt(input, name)?.ToString();

    protected static bool GetBool(JsonElement input, string name, bool fallback = false) =>
        input.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    protected static DateOnly RequireDate(JsonElement input, string name)
    {
        var raw = GetString(input, name)
            ?? throw new GoalValidationException($"'{name}' is required (ISO date, e.g. 2026-07-01).");
        return DateOnly.TryParse(raw, out var date)
            ? date
            : throw new GoalValidationException($"'{name}' must be an ISO date like 2026-07-01.");
    }

    protected static async Task<int> RequireGoalAsync(IGoalService goals, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new GoalValidationException("'goalKey' is required, e.g. \"GOAL-3\" as listed by get_goals.");
        return await goals.ResolveKeyAsync(key, ct)
            ?? throw new GoalValidationException(
                $"There is no goal {key}. Use the goalKey values from get_goals (like \"GOAL-3\"), not list positions.");
    }

    protected static string Ok(object payload) => JsonSerializer.Serialize(payload, JsonOpts);
}

public class GetGoalsTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "get_goals";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the user's goals. Each has a key like GOAL-3 (use it to change the goal), a period " +
        "(year, quarter, month), and its tasks with keys like TASK-7 and story points. " +
        "effectiveProgress (0-100) comes from the goal's tasks. Dropped goals are hidden unless includeDropped is true.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "includeDropped": { "type": "boolean", "description": "Also include dropped goals. Default false." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { goals = await goals.GetAllAsync(GetBool(input, "includeDropped"), ct) });
}

public class CreateGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "create_goal";
    public override string Description =>
        "Creates a goal (like an epic). Goals do not nest: to break a goal into smaller pieces, create " +
        "tasks under it with create_task. periodStart is the first day of the period.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Short goal title." },
            "description": { "type": "string", "description": "Optional longer description." },
            "periodType": { "type": "string", "enum": ["year", "quarter", "month"] },
            "periodStart": { "type": "string", "description": "First day of the period, ISO date (e.g. 2026-07-01)." },
            "progress": { "type": "integer", "description": "Initial progress 0-100, used only until the goal has tasks. Default 0." }
          },
          "required": ["title", "periodType", "periodStart"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var goal = await goals.CreateAsync(
            new CreateGoalRequest(
                GetString(input, "title") ?? string.Empty,
                GetString(input, "description"),
                GetString(input, "periodType") ?? string.Empty,
                RequireDate(input, "periodStart"),
                GetInt(input, "progress") ?? 0),
            ct);
        return Ok(new { created = goal });
    }
}

public class UpdateGoalStatusTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "update_goal_status";
    public override string Description =>
        "Updates a goal's status (active, completed, dropped) and/or its manual progress percentage. " +
        "Completing a goal sets its progress to 100. A goal with tasks takes its progress from them.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalKey": { "type": "string", "description": "The goal's key from get_goals, e.g. \"GOAL-3\"." },
            "status": { "type": "string", "enum": ["active", "completed", "dropped"] },
            "progress": { "type": "integer", "description": "New progress 0-100." }
          },
          "required": ["goalKey"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        RequireGoalAsync(goals, GetKey(input, "goalKey") ?? GetKey(input, "goalId"), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireGoalAsync(goals, GetKey(input, "goalKey") ?? GetKey(input, "goalId"), ct);
        var status = GetString(input, "status");
        var progress = GetInt(input, "progress");
        if (status is null && progress is null)
            throw new GoalValidationException("Provide 'status', 'progress', or both.");

        GoalDto? goal = null;
        if (progress is not null)
            goal = await goals.UpdateAsync(id, new UpdateGoalRequest(Progress: progress), ct);
        if (status is not null)
            goal = await goals.UpdateStatusAsync(id, status, ct);

        return Ok(new { updated = goal });
    }
}

public class DeleteGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "delete_goal";
    public override string Description =>
        "Permanently deletes a goal. Its tasks are kept as standalone tasks. Prefer update_goal_status " +
        "with \"dropped\" when the user just wants to stop pursuing it.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalKey": { "type": "string", "description": "The goal's key from get_goals, e.g. \"GOAL-3\"." }
          },
          "required": ["goalKey"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        RequireGoalAsync(goals, GetKey(input, "goalKey"), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var key = GetKey(input, "goalKey");
        var id = await RequireGoalAsync(goals, key, ct);
        var goal = await goals.GetAsync(id, ct);
        await goals.DeleteAsync(id, ct);
        return Ok(new { deleted = new { key = goal?.Key, title = goal?.Title } });
    }
}
