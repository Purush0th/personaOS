using System.Text.Json;
using PersonaOS.Application.Board;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Goals.Tools;

/// <summary>
/// Proposes a goal with the goals and tasks under it as one card, and creates them all once the
/// user confirms. Plan mode's tool: "a quarter, its three months and their tasks" cannot be
/// separate cards, because a month needs its quarter's key and the quarter does not exist until
/// its card is confirmed.
/// </summary>
public class CreatePlanTool(IGoalService goals, IBoardService board, IInstanceConfigService configService) : GoalToolBase
{
    /// <summary>A plan bigger than this is a sign the model is inventing work, and too long a card to judge.</summary>
    private const int MaxGoals = 16;
    private const int MaxTasks = 40;

    public override string Name => "create_plan";

    public override string Description =>
        "Proposes a new goal together with the goals and tasks under it, as one card the user confirms: " +
        "a year with its quarters, a quarter with its months, a month with its tasks, or all of them. Goals " +
        "nest year > quarter > month and tasks go only under monthly goals (into the backlog). Use it for a " +
        "new nested plan; for one goal or task on its own, use create_goal or create_task. Example: " +
        "{\"goal\": {\"title\": \"Get fit\", \"periodType\": \"quarter\", \"quarter\": 4, \"goals\": [" +
        "{\"title\": \"Build a base\", \"periodType\": \"month\", \"month\": 10, \"tasks\": [\"Run 3 times a week\"]}, " +
        "{\"title\": \"Go longer\", \"periodType\": \"month\", \"month\": 11, \"tasks\": [\"Run 10 km\"]}]}}";

    private const string TaskSchema = """
        "tasks": { "type": "array", "description": "Only under a month goal.", "items": { "type": "object", "properties": {
          "title": { "type": "string" },
          "points": { "type": "integer", "description": "Optional value points: 1, 2, 3, 5, 8, 13 or 21." } }, "required": ["title"] } }
        """;

    private const string GoalFields = """
        "title": { "type": "string" },
        "periodType": { "type": "string", "enum": ["year", "quarter", "month"] },
        "year": { "type": "integer" },
        "quarter": { "type": "integer", "description": "1-4, for a quarter goal." },
        "month": { "type": "integer", "description": "1-12, for a month goal." },
        "description": { "type": "string" }
        """;

    public override string InputSchemaJson => $$"""
        {
          "type": "object",
          "properties": {
            "parentKey": { "type": "string", "description": "Optional existing goal the plan goes under, e.g. \"GOAL-2\"." },
            "goal": {
              "type": "object",
              "properties": {
                {{GoalFields}},
                {{TaskSchema}},
                "goals": { "type": "array", "description": "Goals under this one.", "items": { "type": "object", "properties": {
                  {{GoalFields}},
                  {{TaskSchema}},
                  "goals": { "type": "array", "description": "Goals under this one.", "items": { "type": "object", "properties": {
                    {{GoalFields}},
                    {{TaskSchema}} }, "required": ["title", "periodType"] } } }, "required": ["title", "periodType"] } }
              },
              "required": ["title", "periodType"]
            }
          },
          "required": ["goal"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        var plan = await PlanAsync(input, ct);
        if (plan.GoalCount > MaxGoals || plan.TaskCount > MaxTasks)
            throw new GoalValidationException(
                $"Keep one plan to at most {MaxGoals} goals and {MaxTasks} tasks; propose the rest once this one is confirmed.");
        if (plan.TaskCount > 0 && !(await configService.GetOrCreateAsync(ct)).IsEnabled(InstanceConfig.Modules.Board))
            throw new GoalValidationException("The board module is off, so a plan cannot include tasks. Leave them out.");
        await goals.ValidatePlanAsync(plan, ct);
    }

    public override async Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default)
    {
        var plan = await PlanAsync(input, ct);
        var parts = new List<string>();
        if (plan.GoalCount > 1) parts.Add(plan.GoalCount - 1 == 1 ? "1 goal" : $"{plan.GoalCount - 1} goals");
        if (plan.TaskCount > 0) parts.Add(plan.TaskCount == 1 ? "1 task" : $"{plan.TaskCount} tasks");
        var under = parts.Count == 0 ? string.Empty : $" with {string.Join(" and ", parts)} under it";
        return $"“{plan.Title}” ({GoalPeriods.Label(plan.PeriodType)}){under}";
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var plan = await PlanAsync(input, ct);

        var keys = new List<string>();
        var tasks = 0;
        async Task<GoalDto> CreateAsync(GoalPlan node, int? parentId)
        {
            var goal = await goals.CreateAsync(node.ToRequest(parentId), ct);
            keys.Add(goal.Key);
            foreach (var task in node.PlanTasks)
            {
                await board.CreateTaskAsync(new CreateTaskRequest(task.Title.Trim(), Points: task.Points, GoalId: goal.Id), ct);
                tasks++;
            }
            foreach (var child in node.ChildGoals) await CreateAsync(child, goal.Id);
            return goal;
        }

        var root = await CreateAsync(plan, plan.ParentId);
        return Ok(new
        {
            created = new
            {
                key = root.Key,
                title = root.Title,
                periodType = root.PeriodType,
                goals = keys,
                taskCount = tasks,
            },
        });
    }

    private async Task<GoalPlan> PlanAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("goal", out var goal) || goal.ValueKind != JsonValueKind.Object)
            throw new GoalValidationException("'goal' is required: the top goal of the plan, with its goals and tasks under it.");

        var parentKey = GetKey(input, "parentKey");
        var parentId = string.IsNullOrWhiteSpace(parentKey) ? (int?)null : await RequireGoalAsync(goals, parentKey, ct);
        return Parse(goal) with { ParentId = parentId };
    }

    /// <summary>One goal of the plan and everything under it. "children" is read too: models use both words.</summary>
    private static GoalPlan Parse(JsonElement goal)
    {
        var children = new List<GoalPlan>();
        foreach (var name in new[] { "goals", "children" })
        {
            if (goal.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
                children.AddRange(list.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object).Select(Parse));
        }

        var tasks = new List<PlanTask>();
        if (goal.TryGetProperty("tasks", out var taskList) && taskList.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in taskList.EnumerateArray())
            {
                // A bare string is a task title: small models write the list that way.
                if (task.ValueKind == JsonValueKind.String) tasks.Add(new PlanTask(task.GetString() ?? string.Empty));
                else if (task.ValueKind == JsonValueKind.Object) tasks.Add(new PlanTask(GetString(task, "title") ?? string.Empty, GetInt(task, "points")));
            }
        }

        return new GoalPlan(
            (GetString(goal, "title") ?? string.Empty).Trim(),
            (GetString(goal, "periodType") ?? string.Empty).Trim().ToLowerInvariant(),
            GetInt(goal, "year"),
            GetQuarter(goal, "quarter"),
            GetMonth(goal, "month"),
            OptionalDate(goal, "periodStart"),
            Children: children,
            Tasks: tasks,
            Description: GetString(goal, "description"));
    }
}
