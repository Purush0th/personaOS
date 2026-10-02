using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Board;

/// <summary>The board: creating, editing, moving and deleting tasks.</summary>
public partial class BoardService
{
    // ---------------------------------------------------------------- tasks

    public async Task<BoardTaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var title = RequireTitle(request.Title);
        ValidatePoints(request.Points);
        var priority = ValidatePriority(request.Priority) ?? WorkItemPriorities.Medium;
        await RequireGoalAsync(request.GoalId, ct);

        var sprint = await FindSprintAsync(request.SprintKey, ct);
        if (sprint?.Status == SprintStatuses.Closed)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} is closed.");
        var status = CreateStatus(request.Column, sprint);

        var addedMidSprint = false;
        if (sprint is not null && IsScopeLocked(sprint))
        {
            if (!request.AcknowledgeScopeChange)
            {
                throw new ScopeChangeException(
                    $"{ItemKeys.Sprint(sprint.Number)} is running, so adding “{title}” is a scope change. " +
                    "Confirm to add it anyway; it will be reported as added, not committed.");
            }
            addedMidSprint = true;
        }

        var task = new BoardTask
        {
            Number = KeyNumberAllocator.LowestFree(await db.BoardTasks.Select(t => t.Number).ToListAsync(ct)),
            Title = title,
            Description = NormalizeText(request.Description),
            Points = request.Points,
            Priority = priority,
            GoalId = request.GoalId,
            SprintId = sprint?.Id,
            Status = status,
            SortOrder = await NextSortOrderAsync(sprint?.Id, status, ct),
            AddedMidSprint = addedMidSprint,
            CompletedAtUtc = status == BoardTaskStatuses.Done ? UtcNow() : null,
        };
        db.BoardTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return (await GetTaskAsync(task.Id, ct))!.Task;
    }

    public async Task<BoardTaskDto?> UpdateTaskAsync(int id, UpdateTaskRequest request, CancellationToken ct = default)
    {
        var task = await db.BoardTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

        if (request.Title is not null) task.Title = RequireTitle(request.Title);
        if (request.ClearDescription) task.Description = null;
        else if (request.Description is not null) task.Description = NormalizeText(request.Description);

        // Re-estimating during a sprint is fine; the committed number was frozen at the start.
        if (request.ClearPoints) task.Points = null;
        else if (request.Points is int points)
        {
            ValidatePoints(points);
            task.Points = points;
        }

        if (ValidatePriority(request.Priority) is string priority) task.Priority = priority;

        if (request.ClearGoal) task.GoalId = null;
        else if (request.GoalId is int goalId)
        {
            await RequireGoalAsync(goalId, ct);
            task.GoalId = goalId;
        }

        task.UpdatedAtUtc = UtcNow();
        await db.SaveChangesAsync(ct);
        return (await GetTaskAsync(id, ct))?.Task;
    }

    public async Task<BoardTaskDto?> MoveTaskAsync(int id, MoveTaskRequest request, CancellationToken ct = default)
    {
        var column = (request.Column ?? string.Empty).Trim().ToLowerInvariant();
        if (!BoardColumns.All.Contains(column))
            throw new BoardValidationException($"Column must be one of: {string.Join(", ", BoardColumns.All)}.");

        var task = await db.BoardTasks.Include(t => t.Sprint).Include(t => t.Goal).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

        // A completed monthly goal has no open tasks, so one of its tasks cannot be reopened
        // until the goal is.
        if (task.Status == BoardTaskStatuses.Done && column != BoardColumns.Done
            && task.Goal is { Status: GoalStatuses.Completed } goal)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Goal(goal.Number)} is completed; reopen it before reopening {ItemKeys.Task(task.Number)}.");
        }

        var from = task.Sprint;
        if (from?.Status == SprintStatuses.Closed)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Task(task.Number)} belongs to {ItemKeys.Sprint(from.Number)}, which is closed.");
        }

        Sprint? to;
        if (column == BoardColumns.Backlog)
        {
            to = null;
        }
        else if (request.SprintKey is not null)
        {
            to = await FindSprintAsync(request.SprintKey, ct)
                 ?? throw new BoardValidationException($"There is no sprint {request.SprintKey}.");
        }
        else
        {
            to = from ?? await db.Sprints.FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct)
                ?? throw new BoardValidationException(
                    "No sprint is running — say which sprint to move it into, or move it to the backlog.");
        }

        if (to?.Status == SprintStatuses.Closed)
            throw new BoardValidationException($"{ItemKeys.Sprint(to.Number)} is closed.");
        if (to is not null && to.Status == SprintStatuses.Planned && column != BoardColumns.Todo)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Sprint(to.Number)} has not started yet, so its work cannot be in progress or done.");
        }

        var leaving = from is not null && from.Id != to?.Id;
        var joining = to is not null && to.Id != from?.Id;
        var removesScope = leaving && IsScopeLocked(from!);
        var addsScope = joining && IsScopeLocked(to!);

        if ((removesScope || addsScope) && !request.AcknowledgeScopeChange)
        {
            var sprint = removesScope ? from! : to!;
            var verb = removesScope ? "taking it out" : "adding it";
            throw new ScopeChangeException(
                $"{ItemKeys.Sprint(sprint.Number)} is running, so {verb} is a scope change for " +
                $"{ItemKeys.Task(task.Number)}. Confirm to go ahead; it will show in the sprint report.");
        }

        // Taking out work that was committed counts as removed. Work that was itself added
        // mid-sprint just stops counting as added.
        if (removesScope && !task.AddedMidSprint) from!.RemovedPoints += task.Points ?? 0;
        if (leaving) task.AddedMidSprint = false;
        if (addsScope) task.AddedMidSprint = true;

        var status = column == BoardColumns.Backlog ? BoardTaskStatuses.Todo : column;
        if (status == BoardTaskStatuses.Done && task.Status != BoardTaskStatuses.Done) task.CompletedAtUtc = UtcNow();
        if (status != BoardTaskStatuses.Done) task.CompletedAtUtc = null;

        task.SprintId = to?.Id;
        task.Sprint = to;
        task.Status = status;
        task.UpdatedAtUtc = UtcNow();

        // Renumber the target column with the task at the requested position.
        var siblings = await db.BoardTasks
            .Where(t => t.Id != task.Id && t.SprintId == task.SprintId && t.Status == status)
            .Where(t => task.SprintId != null || t.Status != BoardTaskStatuses.Done)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Number)
            .ToListAsync(ct);
        var index = Math.Clamp(request.Index ?? siblings.Count, 0, siblings.Count);
        siblings.Insert(index, task);
        for (var i = 0; i < siblings.Count; i++) siblings[i].SortOrder = i;

        await db.SaveChangesAsync(ct);
        return (await GetTaskAsync(id, ct))?.Task;
    }

    public async Task<bool> DeleteTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await db.BoardTasks.Include(t => t.Sprint).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return false;

        if (task.Sprint is { } sprint && IsScopeLocked(sprint)
            && !task.AddedMidSprint && task.Status != BoardTaskStatuses.Done)
        {
            sprint.RemovedPoints += task.Points ?? 0;
        }

        // Unlink explicitly: the in-memory test store does not apply the database's SET NULL.
        foreach (var item in await db.PlannerItems.Where(p => p.TaskId == id).ToListAsync(ct)) item.TaskId = null;

        db.WorkItemComments.RemoveRange(
            await db.WorkItemComments.Where(c => c.ItemType == WorkItemTypes.Task && c.ItemId == id).ToListAsync(ct));
        db.WorkItemAttachments.RemoveRange(
            await db.WorkItemAttachments.Where(a => a.ItemType == WorkItemTypes.Task && a.ItemId == id).ToListAsync(ct));

        db.BoardTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
