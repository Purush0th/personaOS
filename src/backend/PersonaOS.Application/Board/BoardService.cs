using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Board;

/// <summary>
/// The sprint board. Sprints are created, started and completed by hand, the way a Jira board
/// works: nothing starts or closes on a timer, because a week that went sideways should not be
/// closed out by a clock. The only thing that still happens on its own is the Sunday nudge.
/// </summary>
public class BoardService(
    IAppDbContext db,
    IInstanceConfigService configService,
    IPushSender pushSender,
    TimeProvider timeProvider,
    ILogger<BoardService> logger) : IBoardService
{
    /// <summary>More than this many tasks in progress shows a warning; it never blocks.</summary>
    public const int WipLimit = 3;

    /// <summary>The proactive job name under which the weekly nudge records itself.</summary>
    public const string PlanningNudgeJob = "sprint_planning";

    /// <summary>A planning nudge this late is skipped rather than sent.</summary>
    private static readonly TimeSpan NudgeLateThreshold = TimeSpan.FromHours(3);

    /// <summary>Closed sprints averaged for velocity.</summary>
    private const int VelocityWindow = 3;

    // ---------------------------------------------------------------- reads

    public async Task<BoardView> GetBoardAsync(CancellationToken ct = default)
    {
        var sprint = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        var tasks = sprint is null
            ? []
            : await TaskQuery().Where(t => t.SprintId == sprint.Id).ToListAsync(ct);
        var counts = await CountsAsync(WorkItemTypes.Task, tasks.Select(t => t.Id).ToList(), ct);

        return new BoardView(
            sprint is null ? null : ToSprintDto(sprint, tasks),
            await VelocityAsync(ct),
            WipLimit,
            ValuePoints.Allowed,
            Column(tasks.Where(t => t.Status == BoardTaskStatuses.Todo), counts),
            Column(tasks.Where(t => t.Status == BoardTaskStatuses.InProgress), counts),
            Column(tasks.Where(t => t.Status == BoardTaskStatuses.Done), counts));
    }

    public async Task<PlanView> GetPlanAsync(CancellationToken ct = default)
    {
        var sprints = await db.Sprints.AsNoTracking()
            .Where(s => s.Status != SprintStatuses.Closed)
            .OrderBy(s => s.Status == SprintStatuses.Active ? 0 : 1).ThenBy(s => s.Number)
            .ToListAsync(ct);
        var sprintIds = sprints.Select(s => s.Id).ToList();

        var tasks = await TaskQuery()
            .Where(t => (t.SprintId != null && sprintIds.Contains(t.SprintId.Value))
                        || (t.SprintId == null && t.Status != BoardTaskStatuses.Done))
            .ToListAsync(ct);
        var counts = await CountsAsync(WorkItemTypes.Task, tasks.Select(t => t.Id).ToList(), ct);

        return new PlanView(
            sprints.Select(s =>
            {
                var own = tasks.Where(t => t.SprintId == s.Id).ToList();
                return new SprintPlan(ToSprintDto(s, own), Column(own, counts));
            }).ToList(),
            Column(tasks.Where(t => t.SprintId is null), counts),
            await VelocityAsync(ct),
            ValuePoints.Allowed,
            WorkItemPriorities.All);
    }

    public async Task<SprintReport> GetReportAsync(int count = 12, CancellationToken ct = default)
    {
        var sprints = await db.Sprints.AsNoTracking()
            .Where(s => s.Status != SprintStatuses.Planned)
            .OrderByDescending(s => s.Number)
            .Take(Math.Clamp(count, 1, 104))
            .ToListAsync(ct);

        var ids = sprints.Select(s => s.Id).ToList();
        var tasks = await db.BoardTasks.AsNoTracking()
            .Where(t => t.SprintId != null && ids.Contains(t.SprintId.Value)).ToListAsync(ct);

        return new SprintReport(
            await VelocityAsync(ct),
            sprints.Select(s => ToSprintDto(s, tasks.Where(t => t.SprintId == s.Id).ToList())).ToList());
    }

    public async Task<SprintDetail?> GetSprintAsync(int id, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;

        var tasks = await TaskQuery().Where(t => t.SprintId == id).ToListAsync(ct);
        var counts = await CountsAsync(WorkItemTypes.Task, tasks.Select(t => t.Id).ToList(), ct);
        var config = await configService.GetOrCreateAsync(ct);

        return new SprintDetail(
            ToSprintDto(sprint, tasks),
            Column(tasks, counts),
            Burndown(sprint, tasks, config.TimeZone, UtcNow()),
            await VelocityAsync(ct));
    }

    public async Task<int?> ResolveSprintKeyAsync(string? key, CancellationToken ct = default)
    {
        var number = ItemKeys.Parse(key, ItemKeys.SprintPrefix);
        if (number is null) return null;
        return await db.Sprints.Where(s => s.Number == number).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<TaskDetail?> GetTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

        var comments = await db.WorkItemComments.AsNoTracking()
            .Where(c => c.ItemType == WorkItemTypes.Task && c.ItemId == id)
            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Select(c => new WorkItemCommentDto(c.Id, c.Author, c.Body, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(ct);
        var attachments = await db.WorkItemAttachments.AsNoTracking()
            .Where(a => a.ItemType == WorkItemTypes.Task && a.ItemId == id)
            .OrderBy(a => a.Id)
            .Select(a => new WorkItemAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAtUtc))
            .ToListAsync(ct);

        return new TaskDetail(
            ToTaskDto(task, comments.Count, attachments.Count),
            comments,
            attachments);
    }

    public async Task<int?> ResolveTaskKeyAsync(string? key, CancellationToken ct = default)
    {
        var number = ItemKeys.Parse(key, ItemKeys.TaskPrefix);
        if (number is null) return null;
        return await db.BoardTasks.Where(t => t.Number == number).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
    }

    // ---------------------------------------------------------------- sprints

    public async Task<SprintDto> CreateSprintAsync(CreateSprintRequest request, CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var zone = config.TimeZone;

        // Default to the next free week in the Sunday-to-Sunday rhythm, so creating a sprint is
        // one click when the user has no opinion about the dates.
        var lastEnd = await db.Sprints.OrderByDescending(s => s.EndsAtUtc)
            .Select(s => (DateTime?)s.EndsAtUtc).FirstOrDefaultAsync(ct);

        // The first sprint starts now and runs to the coming Sunday; later ones take the next free
        // week in the Sunday 20:00 → Sunday 18:00 rhythm. All of it is editable afterwards.
        var startLocal = request.StartsAtLocal
            ?? (lastEnd is DateTime end ? SprintCalendar.StartAfterClose(UserClock.ToLocal(end, zone)) : LocalNow(config));
        var endLocal = request.EndsAtLocal ?? SprintCalendar.CloseAfterStart(startLocal);
        if (endLocal <= startLocal)
            throw new BoardValidationException("A sprint must end after it starts.");

        var sprint = new Sprint
        {
            // Lowest free, like task keys: deleting a planned sprint frees its number again.
            Number = KeyNumberAllocator.LowestFree(await db.Sprints.Select(s => s.Number).ToListAsync(ct)),
            Name = NormalizeText(request.Name),
            Status = SprintStatuses.Planned,
            StartsAtUtc = UserClock.ToUtc(startLocal, zone),
            EndsAtUtc = UserClock.ToUtc(endLocal, zone),
        };
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync(ct);
        return ToSprintDto(sprint, []);
    }

    public async Task<SprintDto?> UpdateSprintAsync(int id, UpdateSprintRequest request, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;
        if (sprint.Status == SprintStatuses.Closed)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} is closed; its dates are history now.");

        var config = await configService.GetOrCreateAsync(ct);
        if (request.ClearName) sprint.Name = null;
        else if (request.Name is not null) sprint.Name = NormalizeText(request.Name);

        if (request.StartsAtLocal is DateTime start) sprint.StartsAtUtc = UserClock.ToUtc(start, config.TimeZone);
        if (request.EndsAtLocal is DateTime end) sprint.EndsAtUtc = UserClock.ToUtc(end, config.TimeZone);
        if (sprint.EndsAtUtc <= sprint.StartsAtUtc)
            throw new BoardValidationException("A sprint must end after it starts.");

        await db.SaveChangesAsync(ct);
        return ToSprintDto(sprint, await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == id).ToListAsync(ct));
    }

    public async Task<SprintDto?> StartSprintAsync(int id, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;
        if (sprint.Status != SprintStatuses.Planned)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} has already been started.");

        var running = await db.Sprints.FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        if (running is not null)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Sprint(running.Number)} is still running. Complete it before starting another.");
        }

        var tasks = await db.BoardTasks.Where(t => t.SprintId == id).ToListAsync(ct);
        sprint.Status = SprintStatuses.Active;
        sprint.StartedAtUtc = UtcNow();
        sprint.CommittedPoints = tasks.Sum(t => t.Points ?? 0);
        foreach (var task in tasks) task.AddedMidSprint = false;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Started sprint {Number} with {Points} points.", sprint.Number, sprint.CommittedPoints);
        return ToSprintDto(sprint, tasks);
    }

    public async Task<SprintDto?> CompleteSprintAsync(
        int id, CompleteSprintRequest request, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;
        if (sprint.Status != SprintStatuses.Active)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} is not running.");

        // Unfinished work goes to the named sprint, or the next planned one, or the backlog.
        Sprint? target = null;
        if (!request.ToBacklog)
        {
            target = await FindSprintAsync(request.MoveUnfinishedToSprintKey, ct)
                     ?? await db.Sprints.Where(s => s.Status == SprintStatuses.Planned)
                         .OrderBy(s => s.Number).FirstOrDefaultAsync(ct);

            if (target is not null && target.Id == sprint.Id)
                throw new BoardValidationException("Unfinished work cannot move into the sprint being completed.");
            if (target is not null && target.Status == SprintStatuses.Closed)
                throw new BoardValidationException($"{ItemKeys.Sprint(target.Number)} is closed.");
        }

        var tasks = await db.BoardTasks.Where(t => t.SprintId == id).ToListAsync(ct);
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).ToList();
        var unfinished = tasks.Where(t => t.Status != BoardTaskStatuses.Done)
            .OrderBy(t => t.Status == BoardTaskStatuses.InProgress ? 0 : 1).ThenBy(t => t.SortOrder).ToList();

        sprint.CommittedPoints ??= tasks.Sum(t => t.Points ?? 0);
        sprint.AddedPoints = tasks.Where(t => t.AddedMidSprint).Sum(t => t.Points ?? 0);
        sprint.CompletedPoints = done.Sum(t => t.Points ?? 0);
        sprint.CarriedOverPoints = unfinished.Sum(t => t.Points ?? 0);
        sprint.Status = SprintStatuses.Closed;
        sprint.ClosedAtUtc = UtcNow();

        // Unfinished work moves on, ahead of anything already waiting there.
        var waiting = target is null
            ? []
            : await db.BoardTasks.Where(t => t.SprintId == target.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var order = 0;
        foreach (var task in unfinished)
        {
            task.SprintId = target?.Id;
            task.CarryOverCount++;
            task.AddedMidSprint = false;
            task.SortOrder = order++;
            task.UpdatedAtUtc = UtcNow();
        }
        foreach (var task in waiting) task.SortOrder = order++;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Completed sprint {Number}: {Completed} of {Committed} points, {Carried} carried to {Target}.",
            sprint.Number, sprint.CompletedPoints, sprint.CommittedPoints, sprint.CarriedOverPoints,
            target is null ? "the backlog" : ItemKeys.Sprint(target.Number));
        return ToSprintDto(sprint, tasks);
    }

    public async Task<bool> DeleteSprintAsync(int id, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return false;

        // A started sprint is a record of a week's work, so it is completed rather than deleted —
        // unless it holds nothing. An empty sprint records nothing, and refusing to remove it left
        // sprint history growing forever with no way to prune a mistake or a test run.
        var holdsWork = await db.BoardTasks.AnyAsync(t => t.SprintId == id, ct);
        if (sprint.Status != SprintStatuses.Planned && holdsWork)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Sprint(sprint.Number)} has already started and holds tasks; complete it "
                + "instead of deleting it.");
        }

        foreach (var task in await db.BoardTasks.Where(t => t.SprintId == id).ToListAsync(ct))
        {
            task.SprintId = null;
            task.Status = BoardTaskStatuses.Todo;
            task.AddedMidSprint = false;
            task.UpdatedAtUtc = UtcNow();
        }

        db.Sprints.Remove(sprint);
        await db.SaveChangesAsync(ct);
        return true;
    }

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

        var task = await db.BoardTasks.Include(t => t.Sprint).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

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

    // ---------------------------------------------------------------- the Sunday nudge

    public async Task<IReadOnlyList<string>> RunRemindersAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var now = UtcNow();
        var local = UserClock.ToLocal(now, config.TimeZone);
        if (local.DayOfWeek != DayOfWeek.Sunday) return [];

        var nudgeAt = UserClock.ToUtc(local.Date.Add(SprintCalendar.NudgeTime.ToTimeSpan()), config.TimeZone);
        if (now < nudgeAt) return [];

        var localDate = DateOnly.FromDateTime(local);
        if (await db.ProactiveJobRuns.AnyAsync(r => r.JobName == PlanningNudgeJob && r.LocalDate == localDate, ct))
        {
            return [];
        }

        var active = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        var body = await NudgeTextAsync(active, now, ct);
        var pushed = body is not null && now - nudgeAt <= NudgeLateThreshold && await PushAsync(config, body, ct);

        db.ProactiveJobRuns.Add(new ProactiveJobRun
        {
            JobName = PlanningNudgeJob,
            LocalDate = localDate,
            Pushed = pushed,
            Summary = body ?? string.Empty,
        });
        await db.SaveChangesAsync(ct);

        return body is null ? [] : [pushed ? "sent the planning nudge" : "planning nudge not pushed"];
    }

    private async Task<string?> NudgeTextAsync(Sprint? active, DateTime now, CancellationToken ct)
    {
        if (active is null)
        {
            var planned = await db.Sprints.AsNoTracking()
                .Where(s => s.Status == SprintStatuses.Planned).OrderBy(s => s.Number).FirstOrDefaultAsync(ct);
            return planned is null
                ? "No sprint is running. Plan one for the week ahead?"
                : $"{ItemKeys.Sprint(planned.Number)} is planned and waiting. Start it when you are ready.";
        }

        var tasks = await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == active.Id).ToListAsync(ct);
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).Sum(t => t.Points ?? 0);
        var total = tasks.Sum(t => t.Points ?? 0);
        var name = active.Name is null
            ? ItemKeys.Sprint(active.Number)
            : $"{ItemKeys.Sprint(active.Number)} “{active.Name}”";

        return now >= active.EndsAtUtc
            ? $"{name} has reached its end date: {done} of {total} points done. Review it, complete it, and plan the next one."
            : $"{name}: {done} of {total} points done. Time for a look at the week ahead.";
    }

    private async Task<bool> PushAsync(InstanceConfig config, string body, CancellationToken ct)
    {
        if (!pushSender.IsConfigured) return false;
        var tokens = await db.DeviceTokens.Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0) return false;

        try
        {
            var results = await pushSender.SendAsync(
                tokens, config.AssistantNickname, body,
                new Dictionary<string, string> { ["type"] = "board" }, ct);
            return results.Any(r => r.Success);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Planning nudge push failed.");
            return false;
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A running sprint that was planned; its committed number is frozen.</summary>
    private static bool IsScopeLocked(Sprint sprint) =>
        sprint.Status == SprintStatuses.Active && sprint.CommittedPoints is not null;

    private async Task<Sprint?> FindSprintAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.Trim().Equals(BoardColumns.Backlog, StringComparison.OrdinalIgnoreCase)) return null;

        var number = ItemKeys.Parse(key, ItemKeys.SprintPrefix)
            ?? throw new BoardValidationException($"“{key}” is not a sprint key like SPRINT-2.");
        return await db.Sprints.FirstOrDefaultAsync(s => s.Number == number, ct)
               ?? throw new BoardValidationException($"There is no sprint {ItemKeys.Sprint(number)}.");
    }

    /// <summary>How the points came down day by day, from what each task's completion recorded.</summary>
    private static IReadOnlyList<BurndownPoint> Burndown(
        Sprint sprint, IReadOnlyCollection<BoardTask> tasks, string timeZone, DateTime nowUtc)
    {
        var startLocal = UserClock.ToLocal(sprint.StartedAtUtc ?? sprint.StartsAtUtc, timeZone).Date;
        var lastLocal = UserClock.ToLocal(
            sprint.ClosedAtUtc ?? (nowUtc < sprint.EndsAtUtc ? nowUtc : sprint.EndsAtUtc), timeZone).Date;
        if (lastLocal < startLocal) lastLocal = startLocal;

        // A closed sprint no longer holds the work it carried over, so its own frozen totals are
        // the only honest source: otherwise the chart shows a sprint that only ever had the points
        // it finished, and the line always reaches zero.
        var total = sprint.Status == SprintStatuses.Closed
            ? Math.Max((sprint.CommittedPoints ?? 0) + (sprint.AddedPoints ?? 0) - sprint.RemovedPoints, 0)
            : tasks.Sum(t => t.Points ?? 0);
        var doneByDay = tasks
            .Where(t => t.Status == BoardTaskStatuses.Done && t.CompletedAtUtc is not null)
            .GroupBy(t => DateOnly.FromDateTime(UserClock.ToLocal(t.CompletedAtUtc!.Value, timeZone)))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Points ?? 0));

        var points = new List<BurndownPoint>();
        var completed = 0;
        for (var day = DateOnly.FromDateTime(startLocal); day <= DateOnly.FromDateTime(lastLocal); day = day.AddDays(1))
        {
            completed += doneByDay.GetValueOrDefault(day);
            points.Add(new BurndownPoint(day, Math.Max(total - completed, 0), completed));
        }
        return points;
    }

    private async Task<double?> VelocityAsync(CancellationToken ct)
    {
        var recent = await db.Sprints.AsNoTracking()
            .Where(s => s.Status == SprintStatuses.Closed)
            .OrderByDescending(s => s.Number)
            .Take(VelocityWindow)
            .Select(s => s.CompletedPoints ?? 0)
            .ToListAsync(ct);
        return recent.Count == 0 ? null : Math.Round(recent.Average(), 1);
    }

    /// <summary>
    /// The status a new task starts in. Work outside a running sprint can only be to do, the same
    /// rule <see cref="MoveTaskAsync"/> applies.
    /// </summary>
    private static string CreateStatus(string? column, Sprint? sprint)
    {
        var status = string.IsNullOrWhiteSpace(column) ? BoardTaskStatuses.Todo : column.Trim().ToLowerInvariant();
        if (!BoardTaskStatuses.All.Contains(status))
            throw new BoardValidationException($"Column must be one of: {string.Join(", ", BoardTaskStatuses.All)}.");
        if (status != BoardTaskStatuses.Todo && sprint?.Status != SprintStatuses.Active)
        {
            throw new BoardValidationException(sprint is null
                ? "Only work in a sprint can be in progress or done."
                : $"{ItemKeys.Sprint(sprint.Number)} has not started yet, so its work cannot be in progress or done.");
        }
        return status;
    }

    private async Task<int> NextSortOrderAsync(int? sprintId, string status, CancellationToken ct)
    {
        var max = await db.BoardTasks
            .Where(t => t.SprintId == sprintId && t.Status == status)
            .MaxAsync(t => (int?)t.SortOrder, ct);
        return (max ?? -1) + 1;
    }

    private async Task RequireGoalAsync(int? goalId, CancellationToken ct)
    {
        if (goalId is int id && !await db.Goals.AnyAsync(g => g.Id == id, ct))
            throw new BoardValidationException($"Goal {id} does not exist.");
    }

    /// <summary>How many comments and attachments each task has, for the card badges.</summary>
    private async Task<Dictionary<int, (int Comments, int Attachments)>> CountsAsync(
        string itemType, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        var comments = await db.WorkItemComments.AsNoTracking()
            .Where(c => c.ItemType == itemType && ids.Contains(c.ItemId))
            .GroupBy(c => c.ItemId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var attachments = await db.WorkItemAttachments.AsNoTracking()
            .Where(a => a.ItemType == itemType && ids.Contains(a.ItemId))
            .GroupBy(a => a.ItemId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return ids.ToDictionary(
            id => id,
            id => (comments.FirstOrDefault(c => c.Key == id)?.Count ?? 0,
                   attachments.FirstOrDefault(a => a.Key == id)?.Count ?? 0));
    }

    private IQueryable<BoardTask> TaskQuery() =>
        db.BoardTasks.AsNoTracking().Include(t => t.Goal).Include(t => t.Sprint);

    private static IReadOnlyList<BoardTaskDto> Column(
        IEnumerable<BoardTask> tasks, IReadOnlyDictionary<int, (int Comments, int Attachments)> counts) =>
        tasks.OrderBy(t => t.SortOrder).ThenBy(t => t.Number)
            .Select(t => ToTaskDto(t, counts.GetValueOrDefault(t.Id).Comments, counts.GetValueOrDefault(t.Id).Attachments))
            .ToList();

    public static BoardTaskDto ToTaskDto(BoardTask task, int comments = 0, int attachments = 0) => new(
        task.Id,
        ItemKeys.Task(task.Number),
        task.Title,
        task.Description,
        task.Points,
        task.Priority,
        BoardColumns.Of(task),
        task.SprintId,
        task.Sprint is null ? null : ItemKeys.Sprint(task.Sprint.Number),
        task.Sprint?.Name,
        task.GoalId,
        task.Goal is null ? null : ItemKeys.Goal(task.Goal.Number),
        task.Goal?.Title,
        task.SortOrder,
        task.AddedMidSprint,
        task.CarryOverCount,
        comments,
        attachments,
        task.CompletedAtUtc,
        task.CreatedAtUtc,
        task.UpdatedAtUtc);

    private static SprintDto ToSprintDto(Sprint sprint, IReadOnlyCollection<BoardTask> tasks)
    {
        var closed = sprint.Status == SprintStatuses.Closed;
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).ToList();
        return new SprintDto(
            sprint.Id,
            ItemKeys.Sprint(sprint.Number),
            sprint.Number,
            sprint.Name,
            sprint.Status,
            sprint.StartsAtUtc,
            sprint.EndsAtUtc,
            sprint.StartedAtUtc,
            sprint.ClosedAtUtc,
            sprint.CommittedPoints,
            closed ? sprint.AddedPoints ?? 0 : tasks.Where(t => t.AddedMidSprint).Sum(t => t.Points ?? 0),
            sprint.RemovedPoints,
            closed ? sprint.CompletedPoints ?? 0 : done.Sum(t => t.Points ?? 0),
            closed ? sprint.CarriedOverPoints : null,
            tasks.Sum(t => t.Points ?? 0),
            tasks.Count,
            done.Count,
            tasks.Count(t => t.Points is null),
            tasks.Count(t => t.CarryOverCount > 0),
            IsScopeLocked(sprint));
    }

    private static void ValidatePoints(int? points)
    {
        if (points is int value && !ValuePoints.Allowed.Contains(value))
            throw new BoardValidationException(
                $"Value points must be one of {string.Join(", ", ValuePoints.Allowed)} (Fibonacci), or left unestimated.");
    }

    private static string? ValidatePriority(string? priority)
    {
        if (priority is null) return null;
        var normalized = priority.Trim().ToLowerInvariant();
        return WorkItemPriorities.All.Contains(normalized)
            ? normalized
            : throw new BoardValidationException(
                $"Priority must be one of: {string.Join(", ", WorkItemPriorities.All)}.");
    }

    private static string RequireTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        return trimmed.Length == 0 ? throw new BoardValidationException("Title must not be empty.") : trimmed;
    }

    private static string? NormalizeText(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private DateTime LocalNow(InstanceConfig config) => UserClock.ToLocal(UtcNow(), config.TimeZone);
}
