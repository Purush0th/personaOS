using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Board;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Goals;

public class GoalService(IAppDbContext db) : IGoalService
{
    public async Task<IReadOnlyList<GoalDto>> GetAllAsync(bool includeDropped = false, CancellationToken ct = default)
    {
        var goals = await Query()
            .Where(g => includeDropped || g.Status != GoalStatuses.Dropped)
            .OrderBy(g => g.PeriodStart).ThenBy(g => g.Number)
            .ToListAsync(ct);
        return goals.Select(ToDto).ToList();
    }

    public async Task<GoalDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var goal = await Query().FirstOrDefaultAsync(g => g.Id == id, ct);
        return goal is null ? null : ToDto(goal);
    }

    public async Task<int?> ResolveKeyAsync(string? key, CancellationToken ct = default)
    {
        var number = ItemKeys.Parse(key, ItemKeys.GoalPrefix);
        if (number is null) return null;
        return await db.Goals.Where(g => g.Number == number).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<GoalDto> CreateAsync(CreateGoalRequest request, CancellationToken ct = default)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0) throw new GoalValidationException("Title must not be empty.");
        ValidatePeriodType(request.PeriodType);
        ValidateProgress(request.Progress);

        var goal = new Goal
        {
            Number = KeyNumberAllocator.LowestFree(await db.Goals.Select(g => g.Number).ToListAsync(ct)),
            Title = title,
            Description = NormalizeDescription(request.Description),
            PeriodType = request.PeriodType,
            // Snap to the first day of the period: the tool contract promises it, and a model
            // asked for a "monthly" goal has been seen passing the 10th of the month.
            PeriodStart = GoalPeriodCalculator.NormalizeStart(request.PeriodType, request.PeriodStart),
            Progress = request.Progress,
        };
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(goal.Id, ct))!;
    }

    public async Task<GoalDto?> UpdateAsync(int id, UpdateGoalRequest request, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null) return null;

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (title.Length == 0) throw new GoalValidationException("Title must not be empty.");
            goal.Title = title;
        }
        if (request.Description is not null) goal.Description = NormalizeDescription(request.Description);
        if (request.PeriodType is not null)
        {
            ValidatePeriodType(request.PeriodType);
            goal.PeriodType = request.PeriodType;
        }
        if (request.PeriodStart is DateOnly start) goal.PeriodStart = start;
        // Re-snap after either field may have changed, so editing the type alone still lands
        // on a valid first-day-of-period.
        goal.PeriodStart = GoalPeriodCalculator.NormalizeStart(goal.PeriodType, goal.PeriodStart);
        if (request.Progress is int progress)
        {
            ValidateProgress(progress);
            goal.Progress = progress;
        }

        goal.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<GoalDto?> UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        if (!GoalStatuses.All.Contains(status))
            throw new GoalValidationException($"Status must be one of: {string.Join(", ", GoalStatuses.All)}.");

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null) return null;

        goal.Status = status;
        if (status == GoalStatuses.Completed) goal.Progress = 100;
        goal.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var goal = await db.Goals.Include(g => g.Tasks).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null) return false;

        // Unlink explicitly: the in-memory test store does not apply the database's SET NULL.
        foreach (var task in goal.Tasks) task.GoalId = null;
        foreach (var item in await db.PlannerItems.Where(p => p.GoalId == id).ToListAsync(ct)) item.GoalId = null;

        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<Goal> Query() =>
        db.Goals.AsNoTracking().Include(g => g.Tasks).ThenInclude(t => t.Sprint);

    private static GoalDto ToDto(Goal goal)
    {
        var tasks = goal.Tasks.OrderBy(t => t.Number).ToList();
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).ToList();
        return new GoalDto(
            goal.Id,
            ItemKeys.Goal(goal.Number),
            goal.Title,
            goal.Description,
            goal.PeriodType,
            goal.PeriodStart,
            goal.Status,
            goal.Progress,
            GoalProgressCalculator.Compute(goal.Progress, tasks),
            tasks.Count,
            done.Count,
            tasks.Sum(t => t.Points ?? 0),
            done.Sum(t => t.Points ?? 0),
            goal.CreatedAtUtc,
            goal.UpdatedAtUtc,
            tasks.Select(t => new GoalTaskSummary(
                t.Id, ItemKeys.Task(t.Number), t.Title, t.Points, BoardColumns.Of(t), t.Sprint?.Number)).ToList());
    }

    private static void ValidatePeriodType(string periodType)
    {
        if (!GoalPeriods.All.Contains(periodType))
            throw new GoalValidationException($"Period type must be one of: {string.Join(", ", GoalPeriods.All)}.");
    }

    private static void ValidateProgress(int progress)
    {
        if (progress is < 0 or > 100)
            throw new GoalValidationException("Progress must be between 0 and 100.");
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
