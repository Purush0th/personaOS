using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Planner;

public class PlannerService(IAppDbContext db) : IPlannerService
{
    private const int MaxRangeDays = 366;

    public async Task<PlannerDay> GetDayAsync(DateOnly date, CancellationToken ct = default)
    {
        var items = await OrderedQuery().Where(i => i.Date == date).ToListAsync(ct);
        return new PlannerDay(date, items.Select(Map).ToList());
    }

    public async Task<IReadOnlyList<PlannerDay>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            throw new PlannerValidationException("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber > MaxRangeDays)
            throw new PlannerValidationException($"Date range must span at most {MaxRangeDays} days.");

        var items = await OrderedQuery()
            .Where(i => i.Date >= from && i.Date <= to)
            .ToListAsync(ct);

        return items
            .GroupBy(i => i.Date)
            .OrderBy(g => g.Key)
            .Select(g => new PlannerDay(g.Key, g.Select(Map).ToList()))
            .ToList();
    }

    public async Task<PlannerItemDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var item = await OrderedQuery().FirstOrDefaultAsync(i => i.Id == id, ct);
        return item is null ? null : Map(item);
    }

    public async Task<PlannerItemDto> CreateAsync(CreatePlannerItemRequest request, CancellationToken ct = default)
    {
        var task = await FindTaskAsync(request.TaskId, ct);
        // Picking a board task is enough: its title and goal carry over unless given explicitly.
        var title = RequireTitle(string.IsNullOrWhiteSpace(request.Title) && task is not null ? task.Title : request.Title);
        var goalId = request.GoalId ?? task?.GoalId;
        await RequireGoalExistsAsync(goalId, ct);

        var item = new PlannerItem
        {
            Title = title,
            Notes = request.Notes?.Trim(),
            Date = request.Date,
            ScheduledTime = request.ScheduledTime,
            SortOrder = request.SortOrder ?? await NextSortOrderAsync(request.Date, ct),
            Status = PlannerItemStatuses.Planned,
            GoalId = goalId,
            TaskId = task?.Id,
        };

        db.PlannerItems.Add(item);
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, ct) ?? Map(item);
    }

    public async Task<PlannerItemDto?> UpdateAsync(int id, UpdatePlannerItemRequest request, CancellationToken ct = default)
    {
        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;

        if (request.Title is not null) item.Title = RequireTitle(request.Title);
        if (request.Notes is not null) item.Notes = request.Notes.Trim();
        if (request.Date is DateOnly date) item.Date = date;
        if (request.SortOrder is int sortOrder) item.SortOrder = sortOrder;

        if (request.ClearScheduledTime) item.ScheduledTime = null;
        else if (request.ScheduledTime is TimeOnly time) item.ScheduledTime = time;

        if (request.ClearGoal)
        {
            item.GoalId = null;
        }
        else if (request.GoalId is int goalId)
        {
            await RequireGoalExistsAsync(goalId, ct);
            item.GoalId = goalId;
        }

        if (request.ClearTask)
        {
            item.TaskId = null;
        }
        else if (request.TaskId is int taskId)
        {
            item.TaskId = (await FindTaskAsync(taskId, ct))!.Id;
        }

        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PlannerItemDto?> UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!PlannerItemStatuses.All.Contains(normalized))
            throw new PlannerValidationException(
                $"Status must be one of: {string.Join(", ", PlannerItemStatuses.All)}.");

        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;

        item.Status = normalized;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PlannerItemDto?> MoveAsync(int id, DateOnly date, CancellationToken ct = default)
    {
        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;

        item.Date = date;
        item.SortOrder = await NextSortOrderAsync(date, ct);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return false;

        db.PlannerItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Unscheduled items sort after timed ones; then by explicit order, then id.</summary>
    private IQueryable<PlannerItem> OrderedQuery() =>
        db.PlannerItems.AsNoTracking()
            .Include(i => i.Goal)
            .Include(i => i.Task).ThenInclude(t => t!.Goal)
            .OrderBy(i => i.ScheduledTime == null)
            .ThenBy(i => i.ScheduledTime)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.Id);

    private async Task<int> NextSortOrderAsync(DateOnly date, CancellationToken ct)
    {
        var max = await db.PlannerItems
            .Where(i => i.Date == date)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    private async Task<BoardTask?> FindTaskAsync(int? taskId, CancellationToken ct)
    {
        if (taskId is not int id) return null;
        return await db.BoardTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new PlannerValidationException($"Task {id} does not exist.");
    }

    private async Task RequireGoalExistsAsync(int? goalId, CancellationToken ct)
    {
        if (goalId is not int id) return;
        if (!await db.Goals.AnyAsync(g => g.Id == id, ct))
            throw new PlannerValidationException($"Goal {id} does not exist.");
    }

    private static string RequireTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new PlannerValidationException("Title is required.");
        return trimmed.Length > 300 ? trimmed[..300] : trimmed;
    }

    private static PlannerItemDto Map(PlannerItem i)
    {
        // A linked task's goal wins: the chip should match the card on the board.
        var goal = i.Task?.Goal ?? i.Goal;
        return new(
            i.Id, i.Title, i.Notes, i.Date, i.ScheduledTime, i.SortOrder, i.Status,
            goal?.Id, goal?.Title, i.CreatedAtUtc, i.UpdatedAtUtc,
            goal is null ? null : PersonaOS.Domain.Services.ItemKeys.Goal(goal.Number),
            i.TaskId,
            i.Task is null ? null : PersonaOS.Domain.Services.ItemKeys.Task(i.Task.Number));
    }
}
