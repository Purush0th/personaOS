using System.Text.Json;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Application.WorkItems;
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

    /// <summary>"TASK-7 “File taxes”", for a confirmation card; null when the key names no task.</summary>
    protected async Task<string?> DescribeTaskAsync(string? key, CancellationToken ct) =>
        await Board.ResolveTaskKeyAsync(key, ct) is int id && await Board.GetTaskAsync(id, ct) is { } detail
            ? $"{detail.Task.Key} “{detail.Task.Title}”"
            : null;

    public override Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) =>
        GetKey(input, "taskKey") is { } key ? DescribeTaskAsync(key, ct) : Task.FromResult<string?>(null);

    protected async Task<int?> OptionalGoalAsync(string? key, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(key) ? null : await RequireGoalAsync(goals, key, ct);
}

public class GetBoardTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "get_board";
    public override bool Mutates => false;
    public override string Description =>
        "Shows the sprint board: the running sprint (key like SPRINT-2, name, dates, committed / " +
        "completed / added points, whether scope is locked), velocity (missing until a sprint has " +
        "finished), and its To do " +
        "(todo), In progress and Done columns. Tasks have keys like TASK-7, value points " +
        "(none = unestimated) and a priority. Use get_plan for the backlog and the sprints to come.";
    public override string InputSchemaJson => """{ "type": "object", "properties": {} }""";

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { board = await Board.GetBoardAsync(ct) });
}

public class GetPlanTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "get_plan";
    public override bool Mutates => false;
    public override string Description =>
        "THE TOOL FOR \"what is in my backlog\" and \"what is planned next\". Shows the backlog " +
        "(tasks in no sprint), every sprint planned after the running one, and the running sprint - " +
        "the same view as the Backlog page. Use it when planning a sprint or deciding what to pull in next.";
    public override string InputSchemaJson => """{ "type": "object", "properties": {} }""";

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { plan = await Board.GetPlanAsync(ct) });
}

public class GetSprintReportTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "get_sprint_report";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the sprints that have started, newest first; the running one's numbers are so far. Each " +
        "has committed, added, removed and completed value points, and a finished one its carried-over " +
        "points. velocity is the average completed points of the last three finished sprints, and is " +
        "missing until one has finished: then say there is no velocity yet. Use it for a sprint review " +
        "and to suggest how much to commit to.";
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
        "Creates a task on the sprint board, optionally under a goal. Points are Fibonacci value points " +
        "(1, 2, 3, 5, 8, 13, 21) for size and complexity; leave them out if not estimated yet. " +
        "sprintKey puts it straight into a sprint (a scope change if that sprint is running); omit it " +
        "to put the task in the backlog.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "description": { "type": "string" },
            "goalKey": { "type": "string", "description": "Goal key like \"GOAL-3\". Omit for a task with no goal." },
            "points": { "type": "integer", "enum": [1, 2, 3, 5, 8, 13, 21] },
            "priority": { "type": "string", "enum": ["highest", "high", "medium", "low", "lowest"] },
            "sprintKey": { "type": "string", "description": "Sprint key like \"SPRINT-2\". Omit for the backlog." }
          },
          "required": ["title"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        await OptionalGoalAsync(GetKey(input, "goalKey"), ct);
        if (GetString(input, "sprintKey") is { Length: > 0 } sprintKey)
            await StartSprintTool.RequireSprintAsync(Board, sprintKey, ct);
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var task = await Board.CreateTaskAsync(
            new CreateTaskRequest(
                GetString(input, "title") ?? string.Empty,
                GetString(input, "description"),
                GetInt(input, "points"),
                GetString(input, "priority"),
                await OptionalGoalAsync(GetKey(input, "goalKey"), ct),
                GetString(input, "sprintKey"),
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
        "Changes a task's title, description, value points, priority or goal. Use this to record " +
        "estimates during planning. To move a task between columns or sprints use move_task.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "taskKey": { "type": "string", "description": "Task key like \"TASK-7\"." },
            "title": { "type": "string" },
            "description": { "type": "string" },
            "points": { "type": "integer", "enum": [1, 2, 3, 5, 8, 13, 21] },
            "clearPoints": { "type": "boolean", "description": "Mark the task unestimated again." },
            "priority": { "type": "string", "enum": ["highest", "high", "medium", "low", "lowest"] },
            "goalKey": { "type": "string", "description": "Move the task under this goal." },
            "clearGoal": { "type": "boolean", "description": "Detach the task from its goal." }
          },
          "required": ["taskKey"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        await OptionalGoalAsync(GetKey(input, "goalKey"), ct);
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        var task = await Board.UpdateTaskAsync(id,
            new UpdateTaskRequest(
                GetString(input, "title"),
                GetString(input, "description"),
                ClearDescription: false,
                GetInt(input, "points"),
                GetBool(input, "clearPoints"),
                GetString(input, "priority"),
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
        "Moves a task to a board column: \"backlog\", \"todo\" (To do), \"in_progress\" or \"done\". " +
        "sprintKey moves it into another sprint, e.g. from SPRINT-1 to SPRINT-2; omit it to keep the " +
        "task where it is. Moving work into or out of a sprint that has started is a scope change and " +
        "shows in the sprint report.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "taskKey": { "type": "string", "description": "Task key like \"TASK-7\"." },
            "column": { "type": "string", "enum": ["backlog", "todo", "in_progress", "done"] },
            "sprintKey": { "type": "string", "description": "Target sprint key like \"SPRINT-2\"." }
          },
          "required": ["taskKey", "column"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        if (GetString(input, "sprintKey") is { Length: > 0 } sprintKey)
            await StartSprintTool.RequireSprintAsync(Board, sprintKey, ct);
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        var task = await Board.MoveTaskAsync(id,
            new MoveTaskRequest(
                GetString(input, "column") ?? string.Empty,
                GetString(input, "sprintKey"),
                AcknowledgeScopeChange: true),
            ct);
        return Ok(new { moved = task });
    }
}

public class DeleteTaskTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "delete_task";
    public override string Description =>
        "Permanently deletes a task, with its comments and attachments. Its number becomes free for " +
        "the next new task.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "taskKey": { "type": "string", "description": "Task key like \"TASK-7\"." }
          },
          "required": ["taskKey"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        RequireTaskAsync(GetKey(input, "taskKey"), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireTaskAsync(GetKey(input, "taskKey"), ct);
        var task = await Board.GetTaskAsync(id, ct);
        await Board.DeleteTaskAsync(id, ct);
        return Ok(new { deleted = new { key = task?.Task.Key, title = task?.Task.Title } });
    }
}

public class CreateSprintTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "create_sprint";
    public override string Description =>
        "Creates the next sprint, planned but not started. Dates are the user's local time and default " +
        "to the next free Sunday 20:00 → Sunday 18:00 week. Give it a short name when the user has one " +
        "in mind. Sprints never start by themselves — use start_sprint.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Optional short name, e.g. \"Paperwork week\"." },
            "startsAtLocal": { "type": "string", "description": "Local start, e.g. 2026-09-20T20:00." },
            "endsAtLocal": { "type": "string", "description": "Local end, e.g. 2026-09-27T18:00." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var sprint = await Board.CreateSprintAsync(
            new CreateSprintRequest(
                GetString(input, "name"),
                ParseLocal(input, "startsAtLocal"),
                ParseLocal(input, "endsAtLocal")),
            ct);
        return Ok(new { created = sprint });
    }

    private static DateTime? ParseLocal(JsonElement input, string name)
    {
        var raw = GetString(input, name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, out var value)
            ? value
            : throw new BoardValidationException($"'{name}' must look like 2026-09-20T20:00.");
    }
}

public class StartSprintTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "start_sprint";
    public override string Description =>
        "Starts a planned sprint, freezing what it holds as the commitment. Only one sprint runs at a time.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "sprintKey": { "type": "string", "description": "Sprint key like \"SPRINT-2\"." }
          },
          "required": ["sprintKey"]
        }
        """;

    public override Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        RequireSprintAsync(Board, GetString(input, "sprintKey"), ct);

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await RequireSprintAsync(Board, GetString(input, "sprintKey"), ct);
        return Ok(new { started = await Board.StartSprintAsync(id, ct) });
    }

    internal static async Task<int> RequireSprintAsync(IBoardService board, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BoardValidationException("'sprintKey' is required, e.g. \"SPRINT-2\".");
        return await board.ResolveSprintKeyAsync(key, ct)
            ?? throw new BoardValidationException($"There is no sprint {key}.");
    }
}

public class CompleteSprintTool(IBoardService board, IGoalService goals) : BoardToolBase(board, goals)
{
    public override string Name => "complete_sprint";
    public override string Description =>
        "Completes the running sprint: freezes what was done and moves unfinished work to the next " +
        "planned sprint, to the sprint named in moveUnfinishedToSprintKey, or to the backlog.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "sprintKey": { "type": "string", "description": "Sprint key like \"SPRINT-2\"." },
            "moveUnfinishedToSprintKey": { "type": "string", "description": "Where unfinished work goes." },
            "toBacklog": { "type": "boolean", "description": "Send unfinished work to the backlog instead." }
          },
          "required": ["sprintKey"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default)
    {
        await StartSprintTool.RequireSprintAsync(Board, GetString(input, "sprintKey"), ct);
        if (GetString(input, "moveUnfinishedToSprintKey") is { Length: > 0 } target)
            await StartSprintTool.RequireSprintAsync(Board, target, ct);
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = await StartSprintTool.RequireSprintAsync(Board, GetString(input, "sprintKey"), ct);
        var sprint = await Board.CompleteSprintAsync(id,
            new CompleteSprintRequest(GetString(input, "moveUnfinishedToSprintKey"), GetBool(input, "toBacklog")), ct);
        return Ok(new { completed = sprint });
    }
}

public class AddCommentTool(IBoardService board, IGoalService goals, IWorkItemService items)
    : BoardToolBase(board, goals)
{
    public override string Name => "add_comment";
    public override string Description =>
        "Adds a comment to a task or goal — a note of what was decided, what is blocked, or what you " +
        "found. Comments from you are shown as written by the assistant.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "itemKey": { "type": "string", "description": "A task or goal key, e.g. \"TASK-7\" or \"GOAL-3\"." },
            "body": { "type": "string", "description": "The comment." }
          },
          "required": ["itemKey", "body"]
        }
        """;

    public override async Task ValidateAsync(JsonElement input, CancellationToken ct = default) =>
        await RequireItemAsync(input, ct);

    /// <summary>Resolves 'itemKey' to the task or goal it names, refusing a key that names nothing.</summary>
    private async Task<WorkItemRef> RequireItemAsync(JsonElement input, CancellationToken ct)
    {
        var key = GetString(input, "itemKey")
            ?? throw new BoardValidationException("'itemKey' is required, e.g. \"TASK-7\".");
        var type = key.TrimStart('#').StartsWith("GOAL", StringComparison.OrdinalIgnoreCase)
            ? WorkItemTypes.Goal
            : WorkItemTypes.Task;

        return await items.ResolveAsync(type, key, ct)
            ?? throw new BoardValidationException($"There is no {type} {key}.");
    }

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var item = await RequireItemAsync(input, ct);
        var comment = await items.AddCommentAsync(
            item, new AddCommentRequest(GetString(input, "body") ?? string.Empty, CommentAuthors.Assistant), ct);

        return Ok(new { commented = new { key = item.Key, title = item.Title, comment.Body } });
    }
}
