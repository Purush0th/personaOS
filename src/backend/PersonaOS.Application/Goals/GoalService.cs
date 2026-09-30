using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Board;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Goals;

public class GoalService(
    IAppDbContext db,
    IInstanceConfigService configService,
    IBoardService board,
    TimeProvider timeProvider) : IGoalService
{
    /// <summary>At most this many keys are named in a refusal ("… and 3 more").</summary>
    private const int KeysNamed = 5;

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<GoalDto>> GetAllAsync(CancellationToken ct = default)
    {
        var goals = await LoadAllAsync(ct);
        var progress = GoalProgressCalculator.ComputeAll(goals);
        var comments = await CountsAsync(db.WorkItemComments.Where(c => c.ItemType == WorkItemTypes.Goal).Select(c => c.ItemId), ct);
        var attachments = await CountsAsync(db.WorkItemAttachments.Where(a => a.ItemType == WorkItemTypes.Goal).Select(a => a.ItemId), ct);

        return goals
            .OrderBy(g => g.PeriodStart).ThenBy(g => g.Number)
            .Select(g => ToDto(g, goals, progress, comments.GetValueOrDefault(g.Id), attachments.GetValueOrDefault(g.Id)))
            .ToList();
    }

    public async Task<GoalDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var goals = await LoadAllAsync(ct);
        var goal = goals.FirstOrDefault(g => g.Id == id);
        if (goal is null) return null;

        var comments = await db.WorkItemComments.CountAsync(c => c.ItemType == WorkItemTypes.Goal && c.ItemId == id, ct);
        var attachments = await db.WorkItemAttachments.CountAsync(a => a.ItemType == WorkItemTypes.Goal && a.ItemId == id, ct);
        return ToDto(goal, goals, GoalProgressCalculator.ComputeAll(goals), comments, attachments);
    }

    public async Task<int?> ResolveKeyAsync(string? key, CancellationToken ct = default)
    {
        var number = ItemKeys.Parse(key, ItemKeys.GoalPrefix);
        if (number is null) return null;
        return await db.Goals.Where(g => g.Number == number).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
    }

    // ---------------------------------------------------------------- create and update

    public async Task ValidateCreateAsync(CreateGoalRequest request, CancellationToken ct = default) =>
        await PlanCreateAsync(request, ct);

    public async Task ValidatePlanAsync(GoalPlan plan, CancellationToken ct = default)
    {
        var (_, dates) = await PlanCreateAsync(plan.ToRequest(plan.ParentId), ct);
        CheckPlanNode(plan, dates, await TodayAsync(ct));
    }

    /// <summary>
    /// The goals and tasks under one goal of a plan. Its children are checked against a stand-in
    /// for it with the dates it will get, the same calendar rules a real parent applies.
    /// </summary>
    private static void CheckPlanNode(GoalPlan node, GoalDates dates, DateOnly today)
    {
        if (node.PlanTasks.Count > 0 && node.PeriodType != GoalPeriods.Month)
            throw new GoalValidationException(
                $"Tasks sit only under monthly goals, and “{node.Title}” is a {PeriodWord(node.PeriodType)} goal. Put them under its months.");
        foreach (var task in node.PlanTasks)
        {
            if (string.IsNullOrWhiteSpace(task.Title))
                throw new GoalValidationException($"A task under “{node.Title}” has no title.");
            if (task.Points is int points && !ValuePoints.Allowed.Contains(points))
                throw new GoalValidationException(
                    $"“{task.Title}” has {points} points; value points are one of {string.Join(", ", ValuePoints.Allowed)}, or left out.");
        }

        var standIn = new Goal { Title = node.Title, PeriodType = node.PeriodType, PeriodStart = dates.Start, PeriodEnd = dates.End };
        var slots = new HashSet<int?>();
        foreach (var child in node.ChildGoals)
        {
            if (string.IsNullOrWhiteSpace(child.Title))
                throw new GoalValidationException($"A goal under “{node.Title}” has no title.");
            ValidatePeriodType(child.PeriodType);
            if (GoalCalendar.ParentTypeOf(child.PeriodType) != node.PeriodType)
                throw new GoalValidationException(
                    $"“{child.Title}” is a {PeriodWord(child.PeriodType)} goal and cannot go under “{node.Title}”, a {PeriodWord(node.PeriodType)} goal. "
                    + "Goals nest year > quarter > month.");

            var childDates = Resolve(child.PeriodType, child.Year, SlotOf(child.PeriodType, child.Quarter, child.Month),
                child.PeriodStart, today, standIn);
            if (!slots.Add(GoalCalendar.SlotOf(child.PeriodType, childDates.End)))
                throw new GoalValidationException(
                    $"The plan has two goals for {GoalCalendar.Label(child.PeriodType, childDates.End)} under “{node.Title}”.");
            CheckPlanNode(child, childDates, today);
        }
    }

    public async Task<GoalDto> CreateAsync(CreateGoalRequest request, CancellationToken ct = default)
    {
        var (title, dates) = await PlanCreateAsync(request, ct);
        var goal = new Goal
        {
            Number = KeyNumberAllocator.LowestFree(await db.Goals.Select(g => g.Number).ToListAsync(ct)),
            Title = title,
            Description = NormalizeDescription(request.Description),
            PeriodType = request.PeriodType,
            PeriodStart = dates.Start,
            PeriodEnd = dates.End,
            ParentGoalId = request.ParentId,
            Progress = request.Progress,
            Priority = ValidatePriority(request.Priority) ?? WorkItemPriorities.Medium,
        };
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(goal.Id, ct))!;
    }

    private async Task<(string Title, GoalDates Dates)> PlanCreateAsync(CreateGoalRequest request, CancellationToken ct)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0) throw new GoalValidationException("Title must not be empty.");
        ValidatePeriodType(request.PeriodType);
        ValidateProgress(request.Progress);
        ValidatePriority(request.Priority);

        Goal? parent = null;
        if (request.ParentId is int parentId)
        {
            parent = await db.Goals.AsNoTracking().Include(g => g.ChildGoals).FirstOrDefaultAsync(g => g.Id == parentId, ct)
                ?? throw new GoalValidationException($"Goal {parentId} does not exist.");
            CheckParent(request.PeriodType, parent);
        }

        var dates = Resolve(request.PeriodType, request.Year, SlotOf(request.PeriodType, request.Quarter, request.Month),
            request.PeriodStart, await TodayAsync(ct), parent);
        if (parent is not null) CheckSlotFree(parent, request.PeriodType, dates, null);
        return (title, dates);
    }

    public async Task<GoalDto?> UpdateAsync(int id, UpdateGoalRequest request, CancellationToken ct = default)
    {
        var goal = await db.Goals.Include(g => g.Tasks).Include(g => g.ChildGoals).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null) return null;
        var key = ItemKeys.Goal(goal.Number);

        string? title = null;
        if (request.Title is not null)
        {
            title = request.Title.Trim();
            if (title.Length == 0) throw new GoalValidationException("Title must not be empty.");
        }
        var priority = ValidatePriority(request.Priority);
        if (request.Progress is int progress)
        {
            ValidateProgress(progress);
            if (goal.ChildGoals.Count > 0)
                throw new GoalValidationException($"{key} takes its progress from its child goals.");
            if (goal.Tasks.Count > 0)
                throw new GoalValidationException($"{key} takes its progress from its tasks.");
        }

        GoalDates? dates = null;
        var type = request.PeriodType ?? goal.PeriodType;
        if (request.PeriodType is not null || request.Year is not null || request.Quarter is not null
            || request.Month is not null || request.PeriodStart is not null)
        {
            ValidatePeriodType(type);
            if (goal.ParentGoalId is not null)
                throw new GoalValidationException(
                    $"{key} sits under another goal and takes its dates from its place there. Move it, or detach it first.");
            if (goal.ChildGoals.Count > 0)
                throw new GoalValidationException($"{key} has child goals; its type and dates cannot change while it does.");
            if (type != GoalPeriods.Month && goal.Tasks.Count > 0)
                throw new GoalValidationException($"Tasks sit only under monthly goals; move {key}'s tasks first.");
            dates = Resolve(type, request.Year, SlotOf(type, request.Quarter, request.Month), request.PeriodStart,
                await TodayAsync(ct), null);
        }

        if (title is not null) goal.Title = title;
        if (request.ClearDescription) goal.Description = null;
        else if (request.Description is not null) goal.Description = NormalizeDescription(request.Description);
        if (priority is not null) goal.Priority = priority;
        if (request.Progress is int newProgress) goal.Progress = newProgress;
        if (dates is { } d)
        {
            goal.PeriodType = type;
            goal.PeriodStart = d.Start;
            goal.PeriodEnd = d.End;
        }

        goal.UpdatedAtUtc = UtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------------------------------------------------------------- status

    public async Task ValidateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        if (!GoalStatuses.All.Contains(status))
            throw new GoalValidationException($"Status must be one of: {string.Join(", ", GoalStatuses.All)}.");
        var goal = await db.Goals.AsNoTracking()
            .Include(g => g.Tasks).Include(g => g.ChildGoals).Include(g => g.ParentGoal)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new GoalValidationException($"Goal {id} does not exist.");

        if (status == GoalStatuses.Completed && goal.Status != GoalStatuses.Completed
            && CompleteProblem(goal) is { } problem)
        {
            throw new GoalValidationException(problem);
        }
        if (status == GoalStatuses.Active && goal.Status == GoalStatuses.Completed
            && goal.ParentGoal is { Status: GoalStatuses.Completed } parent)
        {
            throw new GoalValidationException(
                $"{ItemKeys.Goal(parent.Number)} is completed; reopen it before reopening {ItemKeys.Goal(goal.Number)}.");
        }
    }

    public async Task<GoalDto?> UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        if (!await db.Goals.AnyAsync(g => g.Id == id, ct)) return null;
        await ValidateStatusAsync(id, status, ct);

        var goal = await db.Goals.FirstAsync(g => g.Id == id, ct);
        goal.Status = status;
        goal.UpdatedAtUtc = UtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Why a goal cannot be completed yet, or null. Completion is the user's call, but a month
    /// with open tasks or a parent with active child goals is not finished.
    /// </summary>
    private static string? CompleteProblem(Goal goal)
    {
        var key = ItemKeys.Goal(goal.Number);
        var active = goal.ChildGoals.Where(c => c.Status == GoalStatuses.Active).OrderBy(c => c.Number)
            .Select(c => ItemKeys.Goal(c.Number)).ToList();
        if (active.Count > 0)
            return $"Complete {key}'s child goals first: {Named(active)}.";

        var open = goal.Tasks.Where(t => t.Status != BoardTaskStatuses.Done).OrderBy(t => t.Number)
            .Select(t => ItemKeys.Task(t.Number)).ToList();
        return open.Count > 0
            ? $"{key} has open tasks: {Named(open)}. Mark them done or move them to another monthly goal first."
            : null;
    }

    // ---------------------------------------------------------------- move

    public async Task ValidateMoveAsync(int id, MoveGoalRequest request, CancellationToken ct = default) =>
        await PlanMoveAsync(id, request, ct);

    public async Task<GoalDto?> MoveAsync(int id, MoveGoalRequest request, CancellationToken ct = default)
    {
        if (!await db.Goals.AnyAsync(g => g.Id == id, ct)) return null;
        var plan = await PlanMoveAsync(id, request, ct);

        var goal = await db.Goals.FirstAsync(g => g.Id == id, ct);
        goal.ParentGoalId = request.ParentId;
        foreach (var (goalId, dates) in plan)
        {
            var moved = goalId == id ? goal : await db.Goals.FirstAsync(g => g.Id == goalId, ct);
            moved.PeriodStart = dates.Start;
            moved.PeriodEnd = dates.End;
            moved.UpdatedAtUtc = UtcNow();
        }
        goal.UpdatedAtUtc = UtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The new dates of the goal and each child goal that moves with it; empty when only detached.</summary>
    private async Task<List<(int GoalId, GoalDates Dates)>> PlanMoveAsync(int id, MoveGoalRequest request, CancellationToken ct)
    {
        var goal = await db.Goals.AsNoTracking().Include(g => g.ChildGoals).FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new GoalValidationException($"Goal {id} does not exist.");
        var key = ItemKeys.Goal(goal.Number);

        // Detaching keeps the dates: a nested month is already a valid standalone month.
        if (request.ParentId is null) return [];

        if (request.ParentId == id) throw new GoalValidationException($"{key} cannot sit under itself.");
        var parent = await db.Goals.AsNoTracking().Include(g => g.ChildGoals)
            .FirstOrDefaultAsync(g => g.Id == request.ParentId, ct)
            ?? throw new GoalValidationException($"Goal {request.ParentId} does not exist.");
        CheckParent(goal.PeriodType, parent);

        var today = await TodayAsync(ct);
        var slot = SlotOf(goal.PeriodType, request.Quarter, request.Month)
            ?? throw new GoalValidationException(
                $"Pick which {(goal.PeriodType == GoalPeriods.Quarter ? "quarter" : "month")} of {ItemKeys.Goal(parent.Number)} {key} becomes.");
        var dates = Resolve(goal.PeriodType, request.Year ?? parent.PeriodEnd.Year, slot, null, today, parent);
        CheckSlotFree(parent, goal.PeriodType, dates, id);

        var plan = new List<(int, GoalDates)> { (id, dates) };

        // Child months keep their place in the quarter: the second month stays the second.
        foreach (var child in goal.ChildGoals.OrderBy(c => c.PeriodEnd))
        {
            var position = (child.PeriodEnd.Month - 1) % 3;
            var month = GoalCalendar.QuarterStart(dates.End.Year, GoalCalendar.QuarterOf(dates.End)).Month + position;
            var (childDates, problem) = GoalCalendar.Resolve(
                child.PeriodType, dates.End.Year, month, null, today, (goal.PeriodType, dates));
            if (problem is not null)
                throw new GoalValidationException($"Moving {key} there would move {ItemKeys.Goal(child.Number)} too: {problem}");
            plan.Add((child.Id, childDates!.Value));
        }
        return plan;
    }

    // ---------------------------------------------------------------- delete

    public async Task ValidateDeleteAsync(int id, DeleteGoalRequest request, CancellationToken ct = default) =>
        await PlanDeleteAsync(id, request, ct);

    public async Task<bool> DeleteAsync(int id, DeleteGoalRequest request, CancellationToken ct = default)
    {
        if (!await db.Goals.AnyAsync(g => g.Id == id, ct)) return false;
        var (action, targets) = await PlanDeleteAsync(id, request, ct);

        var goal = await db.Goals.Include(g => g.Tasks).FirstAsync(g => g.Id == id, ct);
        var tasks = goal.Tasks.ToList();
        if (action == GoalTaskActions.Delete)
        {
            foreach (var task in tasks) await board.DeleteTaskAsync(task.Id, ct);
        }
        else
        {
            // Keep unlinks; reassign moves each task where it was mapped. Explicit either way:
            // the in-memory test store does not apply the database's SET NULL.
            foreach (var task in tasks)
            {
                task.GoalId = targets.GetValueOrDefault(task.Id);
                task.UpdatedAtUtc = UtcNow();
            }
        }

        foreach (var item in await db.PlannerItems.Where(p => p.GoalId == id).ToListAsync(ct)) item.GoalId = null;
        db.WorkItemComments.RemoveRange(
            await db.WorkItemComments.Where(c => c.ItemType == WorkItemTypes.Goal && c.ItemId == id).ToListAsync(ct));
        db.WorkItemAttachments.RemoveRange(
            await db.WorkItemAttachments.Where(a => a.ItemType == WorkItemTypes.Goal && a.ItemId == id).ToListAsync(ct));

        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The task action, and for reassign each task's new goal id (absent = no goal).</summary>
    private async Task<(string Action, Dictionary<int, int?> Targets)> PlanDeleteAsync(
        int id, DeleteGoalRequest request, CancellationToken ct)
    {
        var goal = await db.Goals.AsNoTracking().Include(g => g.Tasks).Include(g => g.ChildGoals)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new GoalValidationException($"Goal {id} does not exist.");
        var key = ItemKeys.Goal(goal.Number);

        if (goal.ChildGoals.Count > 0)
        {
            var children = goal.ChildGoals.OrderBy(c => c.Number).Select(c => ItemKeys.Goal(c.Number)).ToList();
            throw new GoalValidationException($"{key} has child goals. Delete them first: {Named(children)}.");
        }

        var action = (request.TaskAction ?? GoalTaskActions.Keep).Trim().ToLowerInvariant();
        if (!GoalTaskActions.All.Contains(action))
            throw new GoalValidationException($"What happens to the tasks must be one of: {string.Join(", ", GoalTaskActions.All)}.");
        var targets = new Dictionary<int, int?>();
        if (action != GoalTaskActions.Reassign || goal.Tasks.Count == 0) return (action, targets);

        var tasks = goal.Tasks.ToDictionary(t => t.Number);
        var map = new Dictionary<int, string?>();
        foreach (var (taskKey, goalKey) in request.Reassign ?? new Dictionary<string, string?>())
        {
            if (ItemKeys.Parse(taskKey, ItemKeys.TaskPrefix) is not int number || !tasks.ContainsKey(number))
                throw new GoalValidationException($"{taskKey} is not one of {key}'s tasks.");
            map[number] = goalKey;
        }
        var missing = tasks.Keys.Where(n => !map.ContainsKey(n)).Order().Select(ItemKeys.Task).ToList();
        if (missing.Count > 0)
            throw new GoalValidationException($"Say where each task goes; missing: {Named(missing)}.");

        foreach (var (number, goalKey) in map)
        {
            if (string.IsNullOrWhiteSpace(goalKey)) continue;
            var targetNumber = ItemKeys.Parse(goalKey, ItemKeys.GoalPrefix);
            var target = targetNumber is null ? null
                : await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Number == targetNumber, ct);
            if (target is null) throw new GoalValidationException($"There is no goal {goalKey}.");
            if (target.Id == id) throw new GoalValidationException($"{ItemKeys.Task(number)} cannot stay with {key}; it is being deleted.");
            if (TaskGoalProblem(target) is { } problem) throw new GoalValidationException(problem);
            targets[tasks[number].Id] = target.Id;
        }
        return (action, targets);
    }

    /// <summary>
    /// Why a task cannot sit under <paramref name="goal"/>, or null. Shared with the board, which
    /// applies the same rule when a task is created or moved to a goal.
    /// </summary>
    public static string? TaskGoalProblem(Goal goal) =>
        TaskGoalProblem(ItemKeys.Goal(goal.Number), goal.PeriodType, goal.Status);

    /// <inheritdoc cref="TaskGoalProblem(Goal)"/>
    public static string? TaskGoalProblem(string key, string periodType, string status)
    {
        if (periodType != GoalPeriods.Month)
            return $"Tasks sit only under monthly goals, and {key} is a {PeriodWord(periodType)} goal. "
                + "Pick one of its months, or create one.";
        return status == GoalStatuses.Completed ? $"{key} is completed; reopen it before adding tasks to it." : null;
    }

    // ---------------------------------------------------------------- rules

    private static void CheckParent(string type, Goal parent)
    {
        var parentKey = ItemKeys.Goal(parent.Number);
        var wanted = GoalCalendar.ParentTypeOf(type);
        if (wanted is null)
            throw new GoalValidationException($"A {PeriodWord(type)} goal cannot sit under another goal.");
        if (parent.PeriodType != wanted)
            throw new GoalValidationException(
                $"A {PeriodWord(type)} goal goes under a {PeriodWord(wanted)} goal, and {parentKey} is a {PeriodWord(parent.PeriodType)} goal.");
        if (parent.Status == GoalStatuses.Completed)
            throw new GoalValidationException($"{parentKey} is completed; reopen it before adding goals to it.");
    }

    private static void CheckSlotFree(Goal parent, string type, GoalDates dates, int? movingId)
    {
        var slot = GoalCalendar.SlotOf(type, dates.End);
        var taken = parent.ChildGoals.FirstOrDefault(c => c.Id != movingId && GoalCalendar.SlotOf(c.PeriodType, c.PeriodEnd) == slot);
        if (taken is not null)
        {
            throw new GoalValidationException(
                $"{ItemKeys.Goal(parent.Number)} already has {GoalCalendar.Label(type, dates.End)}: "
                + $"{ItemKeys.Goal(taken.Number)} “{taken.Title}”.");
        }
    }

    private static GoalDates Resolve(string type, int? year, int? slot, DateOnly? start, DateOnly today, Goal? parent)
    {
        var (dates, problem) = GoalCalendar.Resolve(type, year, slot, start, today,
            parent is null ? null : (parent.PeriodType, new GoalDates(parent.PeriodStart, parent.PeriodEnd)));
        return dates ?? throw new GoalValidationException(problem!);
    }

    private static int? SlotOf(string type, int? quarter, int? month) =>
        type == GoalPeriods.Quarter ? quarter : type == GoalPeriods.Month ? month : null;

    private static string PeriodWord(string type) => type switch
    {
        GoalPeriods.Year => "yearly",
        GoalPeriods.Quarter => "quarterly",
        GoalPeriods.Month => "monthly",
        _ => type,
    };

    private static string Named(IReadOnlyList<string> keys) =>
        keys.Count <= KeysNamed
            ? string.Join(", ", keys)
            : $"{string.Join(", ", keys.Take(KeysNamed))} and {keys.Count - KeysNamed} more";

    // ---------------------------------------------------------------- plumbing

    private async Task<List<Goal>> LoadAllAsync(CancellationToken ct) =>
        await db.Goals.AsNoTracking().Include(g => g.Tasks).ThenInclude(t => t.Sprint).ToListAsync(ct);

    private static async Task<Dictionary<int, int>> CountsAsync(IQueryable<int> itemIds, CancellationToken ct) =>
        (await itemIds.GroupBy(i => i).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return DateOnly.FromDateTime(UserClock.ToLocal(UtcNow(), config.TimeZone));
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static GoalDto ToDto(Goal goal, IReadOnlyList<Goal> all, IReadOnlyDictionary<int, int> progress, int comments, int attachments)
    {
        var tasks = goal.Tasks.OrderBy(t => t.Number).ToList();
        var children = all.Where(g => g.ParentGoalId == goal.Id).OrderBy(g => g.PeriodStart).ThenBy(g => g.Number).ToList();
        var parent = goal.ParentGoalId is int parentId ? all.FirstOrDefault(g => g.Id == parentId) : null;

        // CompleteProblem reads the children through the navigation, which a no-tracking query
        // does not fill for goals loaded side by side.
        var withChildren = new Goal { Number = goal.Number, Tasks = tasks, ChildGoals = children };

        return new GoalDto(
            goal.Id,
            ItemKeys.Goal(goal.Number),
            goal.Title,
            goal.Description,
            goal.PeriodType,
            GoalCalendar.Label(goal.PeriodType, goal.PeriodEnd),
            goal.PeriodStart,
            goal.PeriodEnd,
            goal.ParentGoalId,
            parent is null ? null : ItemKeys.Goal(parent.Number),
            parent?.Title,
            goal.Status,
            goal.Priority,
            goal.Progress,
            progress.GetValueOrDefault(goal.Id),
            tasks.Count,
            tasks.Count(t => t.Status == BoardTaskStatuses.Done),
            children.Count,
            children.Count(c => c.Status == GoalStatuses.Completed),
            goal.Status == GoalStatuses.Completed ? null : CompleteProblem(withChildren),
            comments,
            attachments,
            goal.CreatedAtUtc,
            goal.UpdatedAtUtc,
            tasks.Select(t => new GoalTaskSummary(
                t.Id, ItemKeys.Task(t.Number), t.Title, t.Points, BoardColumns.Of(t), t.Sprint?.Number)).ToList(),
            children.Select(c => new GoalChildSummary(
                c.Id, ItemKeys.Goal(c.Number), c.Title, c.PeriodType, GoalCalendar.Label(c.PeriodType, c.PeriodEnd),
                c.PeriodStart, c.PeriodEnd, c.Status, progress.GetValueOrDefault(c.Id))).ToList());
    }

    private static void ValidatePeriodType(string periodType)
    {
        if (!GoalPeriods.All.Contains(periodType))
            throw new GoalValidationException($"Period type must be one of: {string.Join(", ", GoalPeriods.All)}.");
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
