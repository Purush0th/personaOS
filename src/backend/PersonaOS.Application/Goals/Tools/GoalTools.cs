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
    public string? RequiredFeature => InstanceConfig.Modules.Goals;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

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

    protected static DateOnly RequireDate(JsonElement input, string name)
    {
        var raw = GetString(input, name)
            ?? throw new GoalValidationException($"'{name}' is required (ISO date, e.g. 2026-07-01).");
        return DateOnly.TryParse(raw, out var date)
            ? date
            : throw new GoalValidationException($"'{name}' must be an ISO date like 2026-07-01.");
    }

    protected static string Ok(object payload) => JsonSerializer.Serialize(payload, JsonOpts);
}

public class GetGoalsTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "get_goals";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the user's goals as a hierarchy (yearly > quarterly > monthly). Each goal has an " +
        "effectiveProgress (0-100) rolled up from its children. Dropped goals are hidden unless includeDropped is true.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "includeDropped": { "type": "boolean", "description": "Also include dropped goals. Default false." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { goals = await goals.GetTreeAsync(GetBool(input, "includeDropped"), ct) });
}

public class CreateGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "create_goal";
    public override string Description =>
        "Creates a goal. Use parentGoalId to nest it under an existing goal " +
        "(e.g. a monthly goal under a quarterly one). periodStart is the first day of the period.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Short goal title." },
            "description": { "type": "string", "description": "Optional longer description." },
            "periodType": { "type": "string", "enum": ["year", "quarter", "month"] },
            "periodStart": { "type": "string", "description": "First day of the period, ISO date (e.g. 2026-07-01)." },
            "parentGoalId": { "type": "integer", "description": "Id of the parent goal to nest under." },
            "progress": { "type": "integer", "description": "Initial progress 0-100. Default 0." }
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
                GetInt(input, "parentGoalId"),
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
        "Updates a goal's status (active, completed, dropped) and/or its progress percentage. " +
        "Completing a goal sets its progress to 100.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalId": { "type": "integer" },
            "status": { "type": "string", "enum": ["active", "completed", "dropped"] },
            "progress": { "type": "integer", "description": "New progress 0-100." }
          },
          "required": ["goalId"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetInt(input, "goalId")
            ?? throw new GoalValidationException("'goalId' is required.");
        var status = GetString(input, "status");
        var progress = GetInt(input, "progress");
        if (status is null && progress is null)
            throw new GoalValidationException("Provide 'status', 'progress', or both.");

        GoalNode? goal = null;
        if (progress is not null)
            goal = await goals.UpdateAsync(id, new UpdateGoalRequest(Progress: progress), ct);
        if (status is not null)
            goal = await goals.UpdateStatusAsync(id, status, ct);

        return goal is null
            ? throw new GoalValidationException($"Goal {id} does not exist.")
            : Ok(new { updated = goal });
    }
}

public class LinkGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "link_goal";
    public override string Description =>
        "Moves a goal under a new parent goal, or makes it a top-level goal when parentGoalId is omitted.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalId": { "type": "integer" },
            "parentGoalId": { "type": "integer", "description": "New parent. Omit to make the goal top-level." }
          },
          "required": ["goalId"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetInt(input, "goalId")
            ?? throw new GoalValidationException("'goalId' is required.");
        var goal = await goals.LinkAsync(id, GetInt(input, "parentGoalId"), ct);
        return goal is null
            ? throw new GoalValidationException($"Goal {id} does not exist.")
            : Ok(new { updated = goal });
    }
}
