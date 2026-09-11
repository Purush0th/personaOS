using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Goals;

public class GoalService(IAppDbContext db) : IGoalService
{
    public async Task<IReadOnlyList<GoalNode>> GetTreeAsync(bool includeDropped = false, CancellationToken ct = default)
    {
        var all = await db.Goals.AsNoTracking().ToListAsync(ct);
        var effective = GoalProgressCalculator.ComputeEffectiveProgress(all);
        var visible = includeDropped ? all : all.Where(g => g.Status != GoalStatuses.Dropped).ToList();
        return BuildTree(visible, effective, parentId: null);
    }

    public async Task<GoalNode?> GetAsync(int id, CancellationToken ct = default)
    {
        var all = await db.Goals.AsNoTracking().ToListAsync(ct);
        var root = all.FirstOrDefault(g => g.Id == id);
        if (root is null) return null;
        var effective = GoalProgressCalculator.ComputeEffectiveProgress(all);
        return ToNode(root, all, effective);
    }

    public async Task<GoalNode> CreateAsync(CreateGoalRequest request, CancellationToken ct = default)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0) throw new GoalValidationException("Title must not be empty.");
        ValidatePeriodType(request.PeriodType);
        ValidateProgress(request.Progress);
        if (request.ParentGoalId is int parentId
            && !await db.Goals.AnyAsync(g => g.Id == parentId, ct))
        {
            throw new GoalValidationException($"Parent goal {parentId} does not exist.");
        }

        var goal = new Goal
        {
            Title = title,
            Description = NormalizeDescription(request.Description),
            ParentGoalId = request.ParentGoalId,
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

    public async Task<GoalNode?> UpdateAsync(int id, UpdateGoalRequest request, CancellationToken ct = default)
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

    public async Task<GoalNode?> UpdateStatusAsync(int id, string status, CancellationToken ct = default)
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

    public async Task<GoalNode?> LinkAsync(int id, int? parentGoalId, CancellationToken ct = default)
    {
        var all = await db.Goals.ToListAsync(ct);
        var goal = all.FirstOrDefault(g => g.Id == id);
        if (goal is null) return null;

        if (parentGoalId is int parentId)
        {
            if (parentId == id) throw new GoalValidationException("A goal cannot be its own parent.");
            var parent = all.FirstOrDefault(g => g.Id == parentId)
                ?? throw new GoalValidationException($"Parent goal {parentId} does not exist.");

            // Reject cycles: the new parent must not sit anywhere below this goal.
            for (var cursor = parent; cursor is not null;
                 cursor = cursor.ParentGoalId is int up ? all.FirstOrDefault(g => g.Id == up) : null)
            {
                if (cursor.Id == id)
                    throw new GoalValidationException("Linking would create a cycle in the goal hierarchy.");
            }
        }

        goal.ParentGoalId = parentGoalId;
        goal.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var all = await db.Goals.ToListAsync(ct);
        var root = all.FirstOrDefault(g => g.Id == id);
        if (root is null) return false;

        // Collect the whole subtree explicitly (the self-referencing FK is Restrict, not cascade).
        var childrenByParent = all.Where(g => g.ParentGoalId is not null).ToLookup(g => g.ParentGoalId!.Value);
        var doomed = new List<Goal>();
        var queue = new Queue<Goal>([root]);
        while (queue.TryDequeue(out var current))
        {
            doomed.Add(current);
            foreach (var child in childrenByParent[current.Id]) queue.Enqueue(child);
        }

        foreach (var goal in doomed) db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        return true;
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

    private static IReadOnlyList<GoalNode> BuildTree(
        IReadOnlyCollection<Goal> visible,
        IReadOnlyDictionary<int, int> effective,
        int? parentId)
    {
        var visibleIds = visible.Select(g => g.Id).ToHashSet();
        return visible
            // A goal whose parent is filtered out (or missing) surfaces as a root.
            .Where(g => parentId is null
                ? g.ParentGoalId is null || !visibleIds.Contains(g.ParentGoalId.Value)
                : g.ParentGoalId == parentId)
            .OrderBy(g => g.PeriodStart).ThenBy(g => g.Id)
            .Select(g => ToNode(g, visible, effective))
            .ToList();
    }

    private static GoalNode ToNode(Goal goal, IReadOnlyCollection<Goal> all, IReadOnlyDictionary<int, int> effective) =>
        new(
            goal.Id,
            goal.Title,
            goal.Description,
            goal.ParentGoalId,
            goal.PeriodType,
            goal.PeriodStart,
            goal.Status,
            goal.Progress,
            effective.GetValueOrDefault(goal.Id, goal.Progress),
            goal.CreatedAtUtc,
            goal.UpdatedAtUtc,
            all.Where(g => g.ParentGoalId == goal.Id)
                .OrderBy(g => g.PeriodStart).ThenBy(g => g.Id)
                .Select(g => ToNode(g, all, effective))
                .ToList());
}
