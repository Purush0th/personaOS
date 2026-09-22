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
        var ids = goals.Select(g => g.Id).ToList();
        var comments = await db.WorkItemComments.AsNoTracking()
            .Where(c => c.ItemType == WorkItemTypes.Goal && ids.Contains(c.ItemId))
            .GroupBy(c => c.ItemId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var attachments = await db.WorkItemAttachments.AsNoTracking()
            .Where(a => a.ItemType == WorkItemTypes.Goal && ids.Contains(a.ItemId))
            .GroupBy(a => a.ItemId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return goals.Select(g => ToDto(
            g,
            comments.FirstOrDefault(c => c.Key == g.Id)?.Count ?? 0,
            attachments.FirstOrDefault(a => a.Key == g.Id)?.Count ?? 0)).ToList();
    }

    public async Task<GoalDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var goal = await Query().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null) return null;
        return ToDto(goal, await CommentCountAsync(id, ct), await AttachmentCountAsync(id, ct));
    }

    private Task<int> CommentCountAsync(int goalId, CancellationToken ct) =>
        db.WorkItemComments.CountAsync(c => c.ItemType == WorkItemTypes.Goal && c.ItemId == goalId, ct);

    private Task<int> AttachmentCountAsync(int goalId, CancellationToken ct) =>
        db.WorkItemAttachments.CountAsync(a => a.ItemType == WorkItemTypes.Goal && a.ItemId == goalId, ct);

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
        var end = ValidPeriod(request.PeriodType, request.PeriodStart, request.PeriodEnd);

        var goal = new Goal
        {
            Number = KeyNumberAllocator.LowestFree(await db.Goals.Select(g => g.Number).ToListAsync(ct)),
            Title = title,
            Description = NormalizeDescription(request.Description),
            PeriodType = request.PeriodType,
            PeriodStart = request.PeriodStart,
            PeriodEnd = end,
            Progress = request.Progress,
            Priority = ValidatePriority(request.Priority) ?? WorkItemPriorities.Medium,
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
        if (request.ClearDescription) goal.Description = null;
        else if (request.Description is not null) goal.Description = NormalizeDescription(request.Description);
        if (ValidatePriority(request.Priority) is string priority) goal.Priority = priority;
        if (request.PeriodType is not null) ValidatePeriodType(request.PeriodType);
        if (request.PeriodType is not null || request.PeriodStart is not null || request.PeriodEnd is not null)
        {
            var type = request.PeriodType ?? goal.PeriodType;
            var start = request.PeriodStart ?? goal.PeriodStart;
            // A new type or start with no new end gets that type's default end: keeping the old
            // one would usually break the new rules, and the user did not ask to keep it.
            var end = ValidPeriod(type, start, request.PeriodEnd);
            goal.PeriodType = type;
            goal.PeriodStart = start;
            goal.PeriodEnd = end;
        }
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

        db.WorkItemComments.RemoveRange(
            await db.WorkItemComments.Where(c => c.ItemType == WorkItemTypes.Goal && c.ItemId == id).ToListAsync(ct));
        db.WorkItemAttachments.RemoveRange(
            await db.WorkItemAttachments.Where(a => a.ItemType == WorkItemTypes.Goal && a.ItemId == id).ToListAsync(ct));

        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<Goal> Query() =>
        db.Goals.AsNoTracking().Include(g => g.Tasks).ThenInclude(t => t.Sprint);

    private static GoalDto ToDto(Goal goal, int comments = 0, int attachments = 0)
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
            goal.PeriodEnd,
            goal.Status,
            goal.Priority,
            goal.Progress,
            GoalProgressCalculator.Compute(goal.Progress, tasks),
            tasks.Count,
            done.Count,
            tasks.Sum(t => t.Points ?? 0),
            done.Sum(t => t.Points ?? 0),
            comments,
            attachments,
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

    /// <summary>
    /// The end date to store: <paramref name="end"/> when given, the period type's default
    /// otherwise — refused when it breaks the rules for that period type.
    /// </summary>
    private static DateOnly ValidPeriod(string periodType, DateOnly start, DateOnly? end)
    {
        if (start == default) throw new GoalValidationException("A goal needs a start date.");

        var resolved = end ?? GoalPeriodCalculator.DefaultEnd(periodType, start);
        if (GoalPeriodCalculator.Problem(periodType, start, resolved) is { } problem)
            throw new GoalValidationException(problem);
        return resolved;
    }

    private static string? ValidatePriority(string? priority)
    {
        if (priority is null) return null;
        var normalized = priority.Trim().ToLowerInvariant();
        return WorkItemPriorities.All.Contains(normalized)
            ? normalized
            : throw new GoalValidationException(
                $"Priority must be one of: {string.Join(", ", WorkItemPriorities.All)}.");
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
