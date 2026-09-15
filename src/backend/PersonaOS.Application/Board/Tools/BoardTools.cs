using System.Text.Json;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Board.Tools;

/// <summary>Shared plumbing for the sprint-board tools.</summary>
public abstract class BoardToolBase(IBoardService board, IGoalService goals) : GoalToolBase
{
    protected IBoardService Board { get; } = board;

    public override string? RequiredFeature => InstanceConfig.Modules.Board;

    protected async Task<int> RequireTaskAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BoardValidationException("'taskKey' is required, e.g. \"TASK-7\" as listed by get_board.");
        return await Board.ResolveTaskKeyAsync(key, ct)
            ?? throw new BoardValidationException(
                $"There is no task {key}. Use the key values from get_board or get_goals (like \"TASK-7\"), not list positions.");
    }

    protected async Task<int?> OptionalGoalAsync(string? key, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(key) ? null : await RequireGoalAsync(goals, key, ct);
}

public class GetBoardTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "get_board";
    public override bool Mutates => false;
    public override string Description =>
        "Shows the sprint board: the sprint (number, dates, committed / completed / added points, " +
        "whether scope is locked), recent velocity, and the tasks in Backlog, This week (todo), " +
        "In progress and Done. Tasks have keys like TASK-7, story points (null = unestimated), " +
        "and their goal. Use sprint \"next\" to see next week's sprint being planned.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "sprint": { "type": "string", "enum": ["current", "next"], "description": "Default current." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { board = await Board.GetBoardAsync(GetString(input, "sprint") ?? SprintViews.Current, ct) });
}

public class GetSprintReportTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "get_sprint_report";
    public override bool Mutates => false;
    public override string Description =>
        "Lists past sprints, newest first, with committed, added, removed, completed and carried-over " +
        "story points, and the velocity (average completed points of the last three sprints). Use it " +
        "for a sprint review and to suggest how much to commit to.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "count": { "type": "integer", "description": "How many sprints. Default 6." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { report = await Board.GetReportAsync(GetInt(input, "count") ?? 6, ct) });
}

public class CreateTaskTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "create_task";
    public override string Description =>
        "Creates a task on the sprint board, optionally under a goal. Points are Fibonacci story points " +
        "(1, 2, 3, 5, 8, 13, 21) for size and complexity; leave them out if not estimated yet. " +
        "destination: \"backlog\" (default), \"current\" (this week's sprint; a scope change once it has " +
        "started), or \"next\" (next week's sprint, for planning ahead).";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "description": { "type": "string" },
            "goalKey": { "type": "string", "description": "Goal key like \"GOAL-3\". Omit for a task with no goal." },
            "points": { "type": "integer", "enum": [1, 2, 3, 5, 8, 13, 21] },
            "destination": { "type": "string", "enum": ["backlog", "current", "next"] }
          },
          "required": ["title"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var task = await Board.CreateTaskAsync(
            new CreateTaskRequest(
                GetString(input, "title") ?? string.Empty,
                GetString(input, "description"),
                GetInt(input, "points"),
                await OptionalGoalAsync(GetKey(input, "goalKey"), ct),
                GetString(input, "destination") ?? BoardColumns.Backlog,
                // Only reached after the user confirmed the proposal card, which names the change.
                AcknowledgeScopeChange: true),
            ct);
        return Ok(new { created = task });
    }
}

public class UpdateTaskTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "update_task";
    public override string Description =>
        "Changes a task's title, description, story points, or goal. Use this to record estimates " +
        "during planning. To move a task between columns or sprints use move_task.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "taskKey": { "type": "string", "description": "Task key like \"TASK-7\"." },
            "title": { "type": "string" },
            "description": { "type": "string" },
            "points": { "type": "integer", "enum": [1, 2, 3, 5, 8, 13, 21] },
            "clearPoints": { "type": "boolean", "description": "Mark the task unestimated again." },
            "goalKey": { "type": "string", "description": "Move the task under this goal." },
            "clearGoal": { "type": "boolean", "description": "Detach the task from its goal." }
          },
          "required": ["taskKey"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        var task = await Board.UpdateTaskAsync(id,
            new UpdateTaskRequest(
                GetString(input, "title"),
                GetString(input, "description"),
                GetInt(input, "points"),
                GetBool(input, "clearPoints"),
                await OptionalGoalAsync(GetKey(input, "goalKey"), ct),
                GetBool(input, "clearGoal")),
            ct);
        return Ok(new { updated = task });
    }
}

public class MoveTaskTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "move_task";
    public override string Description =>
        "Moves a task to a board column: \"backlog\", \"todo\" (This week), \"in_progress\" or \"done\". " +
        "sprint: \"current\" or \"next\"; omit to keep the task in its sprint. Moving work into or out of " +
        "a sprint that has started is a scope change and shows in the sprint report.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "taskKey": { "type": "string", "description": "Task key like \"TASK-7\"." },
            "column": { "type": "string", "enum": ["backlog", "todo", "in_progress", "done"] },
            "sprint": { "type": "string", "enum": ["current", "next"] }
          },
          "required": ["taskKey", "column"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        var task = await Board.MoveTaskAsync(id,
            new MoveTaskRequest(
                GetString(input, "column") ?? string.Empty,
                GetString(input, "sprint"),
                AcknowledgeScopeChange: true),
            ct);
        return Ok(new { moved = task });
    }
}

public class DeleteTaskTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "delete_task";
    public override string Description =>
        "Permanently deletes a task. Its number becomes free for the next new task.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "taskKey": { "type": "string", "description": "Task key like \"TASK-7\"." }
          },
          "required": ["taskKey"]
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        var task = await Board.GetTaskAsync(id, ct);
        await Board.DeleteTaskAsync(id, ct);
        return Ok(new { deleted = new { key = task?.Key, title = task?.Title } });
    }
}
