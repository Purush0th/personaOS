using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Goals.Tools;

/// <summary>Shared plumbing for the goal tools: JSON options and input helpers.</summary>
public abstract class GoalToolBase : IPersonaTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public virtual string? RequiredFeature => InstanceConfig.Modules.Goals;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>Tools that act on an existing goal override this to resolve its key early.</summary>
    public virtual Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Tools that act on an existing item name it, for the confirmation card.</summary>
    public virtual Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? GetInt(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var value)) return null;
        // Small models send numbers as strings ("4") as often as not.
        if (value.ValueKind == JsonValueKind.Number) return value.GetInt32();
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    /// <summary>A month as 1-12, "11", "November" or "Nov" — small models write all of them.</summary>
    protected static int? GetMonth(JsonElement input, string name)
    {
        if (GetInt(input, name) is int number) return number;
        var text = GetString(input, name)?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 3) return null;
        var names = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        var index = Array.FindIndex(names, m => m.StartsWith(text[..3], StringComparison.OrdinalIgnoreCase));
        return index is >= 0 and < 12 ? index + 1 : null;
    }

    /// <summary>A quarter as 1-4, "4" or "Q4".</summary>
    protected static int? GetQuarter(JsonElement input, string name) =>
        GetInt(input, name)
        ?? (GetString(input, name)?.Trim().TrimStart('Q', 'q') is { } digits && int.TryParse(digits, out var q) ? q : null);

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

    protected static DateOnly? OptionalDate(JsonElement input, string name)
    {
        var raw = GetString(input, name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateOnly.TryParse(raw, out var date)
            ? date
            : throw new GoalValidationException($"'{name}' must be an ISO date like 2026-07-01.");
    }

    /// <summary>"GOAL-2 “Learn Rust”", for a confirmation card; null when the key names no goal.</summary>
    protected static async Task<string?> DescribeGoalAsync(IGoalService goals, string? key, CancellationToken ct)
    {
        var id = await goals.ResolveKeyAsync(key, ct);
        return id is int found && await goals.GetAsync(found, ct) is { } goal ? $"{goal.Key} “{goal.Title}”" : null;
    }

    protected static async Task<int> RequireGoalAsync(IGoalService goals, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new GoalValidationException("'goalKey' is required, e.g. \"GOAL-3\" as listed by get_goals.");
        return await goals.ResolveKeyAsync(key, ct)
            ?? throw new GoalValidationException(
                $"There is no goal {key}. Use the goalKey values from get_goals (like \"GOAL-3\"), not list positions.");
    }

    protected static string Ok(object payload) => ToolJson.Serialize(payload);
}

public class GetGoalsTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "get_goals";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the user's goals — what they are working towards over a year, quarter or month. NOT " +
        "today's plan: for \"what are my tasks today\" use get_planner. Goals nest year > quarter > " +
        "month; each goal says which goal it is \"under\" and lists its \"childGoals\", and only monthly goals have tasks. Each " +
        "goal has a key like GOAL-3 (use it to change the goal), a slot such as \"Q4 2026\", a status " +
        "and progress (0-100).";
    public override string InputSchemaJson => """{ "type": "object", "properties": {} }""";

    /// <summary>
    /// A flat list in tree order, each goal naming its parent ("under") and its child goals. A
    /// nested tree was tried first: qwen2.5:3b then put a month under the wrong quarter and said
    /// a year with a quarter had no child goals. Each fact stated on the goal it is about reads
    /// better.
    /// </summary>
    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var all = await goals.GetAllAsync(ct);
        var byParent = all.Where(g => g.ParentId is not null).ToLookup(g => g.ParentId!.Value);
        var ordered = new List<GoalDto>();
        void Add(GoalDto g)
        {
            ordered.Add(g);
            foreach (var child in byParent[g.Id]) Add(child);
        }
        foreach (var root in all.Where(g => g.ParentId is null || all.All(p => p.Id != g.ParentId))) Add(root);

        return Ok(new
        {
            goals = ordered.Select(g => new
            {
                key = g.Key,
                title = g.Title,
                type = g.PeriodType,
                slot = g.Slot,
                start = g.PeriodStart,
                end = g.PeriodEnd,
                under = g.ParentKey,
                status = g.Status,
                progress = g.EffectiveProgress,
                description = g.Description,
                childGoals = g.Children.Select(c => c.Key).ToList(),
                tasks = g.Tasks.Select(t => new { key = t.Key, title = t.Title, status = t.Column }).ToList(),
            }).ToList(),
        });
    }
}

public class CreateGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "create_goal";
    public override string Description =>
        "Creates a goal. Goals follow the calendar: a year goal runs from its start (today or later) " +
        "to 31 December and needs at least 90 days; a quarter goal is Q1-Q4 of a year and needs at " +
        "least 45 days left; a month goal is one calendar month and needs at least 15 days left. A " +
        "goal never starts in the past. Goals nest year > quarter > month: give parentKey to put a " +
        "quarter under a year or a month under a quarter. Tasks go only under monthly goals.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Short goal title." },
            "description": { "type": "string", "description": "Optional longer description." },
            "periodType": { "type": "string", "enum": ["year", "quarter", "month"] },
            "year": { "type": "integer", "description": "The year, e.g. 2026: the current year or later. Omit for the next such quarter or month (or the parent's year)." },
            "quarter": { "type": "integer", "description": "1-4, for a quarter goal." },
            "month": { "type": "integer", "description": "1-12, for a month goal." },
            "periodStart": { "type": "string", "description": "Year goals only: the day it starts, ISO date. Default today." },
            "parentKey": { "type": "string", "description": "The year (for a quarter) or quarter (for a month) it sits under, e.g. \"GOAL-2\". Omit for a standalone goal." },
            "progress": { "type": "integer", "description": "Initial progress 0-100, used only until it has tasks or child goals. Default 0." }
          },
          "required": ["title", "periodType"]
        }
        """;

    /// <summary>The same checks as creating it, so a goal that cannot exist never becomes a card.</summary>
    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        await goals.ValidateCreateAsync(await RequestAsync(input, ct), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { created = await goals.CreateAsync(await RequestAsync(input, ct), ct) });

    private async Task<CreateGoalRequest> RequestAsync(JsonElement input, CancellationToken ct)
    {
        var parentKey = GetKey(input, "parentKey");
        return new CreateGoalRequest(
            GetString(input, "title") ?? string.Empty,
            GetString(input, "description"),
            GetString(input, "periodType") ?? string.Empty,
            GetInt(input, "year"),
            GetQuarter(input, "quarter"),
            GetMonth(input, "month"),
            OptionalDate(input, "periodStart"),
            string.IsNullOrWhiteSpace(parentKey) ? null : await RequireGoalAsync(goals, parentKey, ct),
            GetInt(input, "progress") ?? 0);
    }
}

public class UpdateGoalStatusTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "update_goal_status";
    public override string Description =>
        "Completes or reopens a goal (status active or completed) and/or sets its manual progress " +
        "percentage. Only the user completes goals. A monthly goal with open tasks, or a goal with " +
        "active child goals, cannot be completed yet. Manual progress applies only to a goal with no " +
        "tasks and no child goals; the others take theirs from them.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalKey": { "type": "string", "description": "The goal's key from get_goals, e.g. \"GOAL-3\"." },
            "status": { "type": "string", "enum": ["active", "completed"] },
            "progress": { "type": "integer", "description": "New manual progress 0-100." }
          },
          "required": ["goalKey"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireGoalAsync(goals, Key(input), ct);
        var status = GetString(input, "status");
        var progress = GetInt(input, "progress");
        if (status is null && progress is null)
            throw new GoalValidationException("Provide 'status', 'progress', or both.");
        if (status is not null) await goals.ValidateStatusAsync(id, status, ct);
        if (progress is not null && await goals.GetAsync(id, ct) is { } goal && (goal.TaskCount > 0 || goal.ChildCount > 0))
        {
            throw new GoalValidationException(
                $"{goal.Key} takes its progress from its {(goal.ChildCount > 0 ? "child goals" : "tasks")}.");
        }
    }

    public override Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        DescribeGoalAsync(goals, Key(input), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var id = await RequireGoalAsync(goals, Key(input), ct);
        var status = GetString(input, "status");
        var progress = GetInt(input, "progress");

        GoalDto? goal = null;
        if (progress is not null)
            goal = await goals.UpdateAsync(id, new UpdateGoalRequest(Progress: progress), ct);
        if (status is not null)
            goal = await goals.UpdateStatusAsync(id, status, ct);

        return Ok(new { updated = goal });
    }

    private static string? Key(JsonElement input) => GetKey(input, "goalKey") ?? GetKey(input, "goalId");
}

public class MoveGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "move_goal";
    public override string Description =>
        "Moves a quarter goal under a year goal, or a month goal under a quarter goal, into the slot " +
        "given; its dates change to that slot and its child goals move with it. Without parentKey " +
        "the goal is detached and becomes standalone, keeping its dates.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalKey": { "type": "string", "description": "The goal to move, e.g. \"GOAL-5\"." },
            "parentKey": { "type": "string", "description": "The new parent, e.g. \"GOAL-2\". Omit to detach." },
            "year": { "type": "integer", "description": "The slot's year. Default: the parent's year." },
            "quarter": { "type": "integer", "description": "1-4, when moving a quarter goal." },
            "month": { "type": "integer", "description": "1-12, when moving a month goal." }
          },
          "required": ["goalKey"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var (id, request) = await RequestAsync(input, ct);
        await goals.ValidateMoveAsync(id, request, ct);
    }

    public override Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        DescribeGoalAsync(goals, GetKey(input, "goalKey"), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var (id, request) = await RequestAsync(input, ct);
        return Ok(new { moved = await goals.MoveAsync(id, request, ct) });
    }

    private async Task<(int Id, MoveGoalRequest Request)> RequestAsync(JsonElement input, CancellationToken ct)
    {
        var id = await RequireGoalAsync(goals, GetKey(input, "goalKey"), ct);
        var parentKey = GetKey(input, "parentKey");
        var parentId = string.IsNullOrWhiteSpace(parentKey) ? (int?)null : await RequireGoalAsync(goals, parentKey, ct);
        return (id, new MoveGoalRequest(parentId, GetInt(input, "year"), GetQuarter(input, "quarter"), GetMonth(input, "month")));
    }
}

public class DeleteGoalTool(IGoalService goals) : GoalToolBase
{
    public override string Name => "delete_goal";
    public override string Description =>
        "Permanently deletes a goal. A goal with child goals cannot be deleted until they are. Its " +
        "tasks are kept without a goal (taskAction \"keep\", the default), deleted (\"delete\"), or " +
        "moved (\"reassign\", with reassign mapping every task key to a monthly goal key or null). " +
        "When the goal has tasks, ask the user which they want.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "goalKey": { "type": "string", "description": "The goal's key from get_goals, e.g. \"GOAL-3\"." },
            "taskAction": { "type": "string", "enum": ["keep", "delete", "reassign"] },
            "reassign": {
              "type": "object",
              "description": "For reassign: each task key mapped to a monthly goal key, or null for no goal. E.g. {\"TASK-5\": \"GOAL-7\", \"TASK-6\": null}.",
              "additionalProperties": { "type": ["string", "null"] }
            }
          },
          "required": ["goalKey"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireGoalAsync(goals, GetKey(input, "goalKey"), ct);
        await goals.ValidateDeleteAsync(id, Request(input), ct);
    }

    public override Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        DescribeGoalAsync(goals, GetKey(input, "goalKey"), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireGoalAsync(goals, GetKey(input, "goalKey"), ct);
        var goal = await goals.GetAsync(id, ct);
        await goals.DeleteAsync(id, Request(input), ct);
        return Ok(new { deleted = new { key = goal?.Key, title = goal?.Title } });
    }

    private static DeleteGoalRequest Request(JsonElement input)
    {
        Dictionary<string, string?>? map = null;
        if (input.TryGetProperty("reassign", out var raw) && raw.ValueKind == JsonValueKind.Object)
        {
            map = raw.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null);
        }
        return new DeleteGoalRequest(GetString(input, "taskAction"), map);
    }
}
