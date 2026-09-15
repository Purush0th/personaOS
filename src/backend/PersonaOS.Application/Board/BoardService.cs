using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Board;

public class BoardService(
    IAppDbContext db,
    IInstanceConfigService configService,
    IPushSender pushSender,
    TimeProvider timeProvider,
    ILogger<BoardService> logger) : IBoardService
{
    /// <summary>More than this many tasks in progress shows a warning; it never blocks.</summary>
    public const int WipLimit = 3;

    /// <summary>A planning nudge this late is skipped rather than sent.</summary>
    private static readonly TimeSpan NudgeLateThreshold = TimeSpan.FromHours(3);

    /// <summary>Closed sprints averaged for velocity.</summary>
    private const int VelocityWindow = 3;

    // ---------------------------------------------------------------- reads

    public async Task<BoardView> GetBoardAsync(string view = SprintViews.Current, CancellationToken ct = default)
    {
        RequireView(view);
        await RunCycleAsync(ct);

        var config = await configService.GetOrCreateAsync(ct);
        var sprint = await ResolveSprintAsync(view, ct);
        var tasks = await TaskQuery()
            .Where(t => t.SprintId == sprint.Id || (t.SprintId == null && t.Status != BoardTaskStatuses.Done))
            .ToListAsync(ct);

        var inSprint = tasks.Where(t => t.SprintId == sprint.Id).ToList();
        var running = await db.Sprints.AnyAsync(s => s.Status == SprintStatuses.Active, ct);

        return new BoardView(
            view,
            ToSprintDto(sprint, inSprint),
            SprintCalendar.InPlanningWindow(LocalNow(config)),
            CanStartSprint: !running && sprint.Status == SprintStatuses.Planned && view == SprintViews.Current,
            await VelocityAsync(ct),
            WipLimit,
            StoryPoints.Allowed,
            Column(tasks.Where(t => t.SprintId is null)),
            Column(inSprint.Where(t => t.Status == BoardTaskStatuses.Todo)),
            Column(inSprint.Where(t => t.Status == BoardTaskStatuses.InProgress)),
            Column(inSprint.Where(t => t.Status == BoardTaskStatuses.Done)));
    }

    public async Task<SprintReport> GetReportAsync(int count = 12, CancellationToken ct = default)
    {
        await RunCycleAsync(ct);
        var sprints = await db.Sprints.AsNoTracking()
            .Where(s => s.Status != SprintStatuses.Planned)
            .OrderByDescending(s => s.Number)
            .Take(Math.Clamp(count, 1, 104))
            .ToListAsync(ct);

        var ids = sprints.Select(s => s.Id).ToList();
        var tasks = await db.BoardTasks.AsNoTracking().Where(t => t.SprintId != null && ids.Contains(t.SprintId.Value)).ToListAsync(ct);

        return new SprintReport(
            await VelocityAsync(ct),
            sprints.Select(s => ToSprintDto(s, tasks.Where(t => t.SprintId == s.Id).ToList())).ToList());
    }

    public async Task<BoardTaskDto?> GetTaskAsync(int id, CancellationToken ct = default)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(t => t.Id == id, ct);
        return task is null ? null : ToTaskDto(task);
    }

    public async Task<int?> ResolveTaskKeyAsync(string? key, CancellationToken ct = default)
    {
        var number = ItemKeys.Parse(key, ItemKeys.TaskPrefix);
        if (number is null) return null;
        return await db.BoardTasks.Where(t => t.Number == number).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
    }

    // ---------------------------------------------------------------- writes

    public async Task<BoardTaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var title = RequireTitle(request.Title);
        ValidatePoints(request.Points);
        await RequireGoalAsync(request.GoalId, ct);

        var destination = (request.Destination ?? BoardColumns.Backlog).Trim().ToLowerInvariant();
        Sprint? sprint = destination switch
        {
            BoardColumns.Backlog => null,
            SprintViews.Current or SprintViews.Next => await RunCycleThenResolveAsync(destination, ct),
            _ => throw new BoardValidationException(
                $"Destination must be one of: {BoardColumns.Backlog}, {SprintViews.Current}, {SprintViews.Next}."),
        };

        var addedMidSprint = false;
        if (sprint is not null && IsScopeLocked(sprint))
        {
            if (!request.AcknowledgeScopeChange)
            {
                throw new ScopeChangeException(
                    $"Sprint {sprint.Number} has already started, so adding “{title}” is a scope change. " +
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
            GoalId = request.GoalId,
            SprintId = sprint?.Id,
            Status = BoardTaskStatuses.Todo,
            SortOrder = await NextSortOrderAsync(sprint?.Id, BoardTaskStatuses.Todo, ct),
            AddedMidSprint = addedMidSprint,
        };
        db.BoardTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return (await GetTaskAsync(task.Id, ct))!;
    }

    public async Task<BoardTaskDto?> UpdateTaskAsync(int id, UpdateTaskRequest request, CancellationToken ct = default)
    {
        var task = await db.BoardTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

        if (request.Title is not null) task.Title = RequireTitle(request.Title);
        if (request.Description is not null) task.Description = NormalizeText(request.Description);

        // Re-estimating during a sprint is fine; the committed number was frozen at the start.
        if (request.ClearPoints) task.Points = null;
        else if (request.Points is int points)
        {
            ValidatePoints(points);
            task.Points = points;
        }

        if (request.ClearGoal) task.GoalId = null;
        else if (request.GoalId is int goalId)
        {
            await RequireGoalAsync(goalId, ct);
            task.GoalId = goalId;
        }

        task.UpdatedAtUtc = UtcNow();
        await db.SaveChangesAsync(ct);
        return await GetTaskAsync(id, ct);
    }

    public async Task<BoardTaskDto?> MoveTaskAsync(int id, MoveTaskRequest request, CancellationToken ct = default)
    {
        var column = (request.Column ?? string.Empty).Trim().ToLowerInvariant();
        if (!BoardColumns.All.Contains(column))
            throw new BoardValidationException($"Column must be one of: {string.Join(", ", BoardColumns.All)}.");
        if (request.Sprint is not null) RequireView(request.Sprint.Trim().ToLowerInvariant());

        await RunCycleAsync(ct);

        var task = await db.BoardTasks.Include(t => t.Sprint).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

        var from = task.Sprint;
        if (from?.Status == SprintStatuses.Closed)
            throw new BoardValidationException(
                $"{ItemKeys.Task(task.Number)} was finished in sprint {from.Number}, which is closed.");

        Sprint? to = column == BoardColumns.Backlog
            ? null
            : request.Sprint is not null
                ? await ResolveSprintAsync(request.Sprint.Trim().ToLowerInvariant(), ct)
                : from ?? await ResolveSprintAsync(SprintViews.Current, ct);

        if (to is not null && to.Status == SprintStatuses.Planned && column != BoardColumns.Todo)
            throw new BoardValidationException(
                $"Sprint {to.Number} has not started yet, so its tasks can only be in “This week”.");

        var leaving = from is not null && from.Id != to?.Id;
        var joining = to is not null && to.Id != from?.Id;
        var removesScope = leaving && IsScopeLocked(from!);
        var addsScope = joining && IsScopeLocked(to!);

        if ((removesScope || addsScope) && !request.AcknowledgeScopeChange)
        {
            var sprintNumber = removesScope ? from!.Number : to!.Number;
            var verb = removesScope ? "taking it out" : "adding it";
            throw new ScopeChangeException(
                $"Sprint {sprintNumber} has already started, so {verb} is a scope change for " +
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
        return await GetTaskAsync(id, ct);
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

        db.BoardTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SprintDto> StartSprintAsync(CancellationToken ct = default)
    {
        await RunCycleAsync(ct);

        var running = await db.Sprints.FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        if (running is not null)
        {
            var config = await configService.GetOrCreateAsync(ct);
            var ends = UserClock.ToLocal(running.EndsAtUtc, config.TimeZone);
            throw new BoardValidationException(
                $"Sprint {running.Number} is still running until {ends:ddd d MMM HH:mm}.");
        }

        var planned = await NextPlannedAsync(ct);
        await StartAsync(planned, manual: true, ct);
        return ToSprintDto(planned, await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == planned.Id).ToListAsync(ct));
    }

    // ---------------------------------------------------------------- the weekly cycle

    public async Task<SprintCycleResult> RunCycleAsync(CancellationToken ct = default)
    {
        var events = new List<string>();
        var now = UtcNow();

        await BootstrapAsync(events, ct);

        // Loop so a server that was off for weeks catches up one boundary at a time.
        for (var guard = 0; guard < 520; guard++)
        {
            var active = await db.Sprints.FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
            var planned = await NextPlannedAsync(ct);

            if (active is not null && now >= active.EndsAtUtc)
            {
                await CloseAsync(active, planned, ct);
                events.Add($"closed sprint {active.Number}");
                continue;
            }

            if (active is null && now >= planned.StartsAtUtc)
            {
                await StartAsync(planned, manual: false, ct);
                events.Add($"started sprint {planned.Number}");
                continue;
            }

            break;
        }

        var nudge = await NudgeIfDueAsync(ct);
        if (nudge is not null) events.Add(nudge);

        return new SprintCycleResult(events);
    }

    /// <summary>
    /// A brand-new board gets a sprint straight away: running until the coming Sunday 18:00, or,
    /// when first opened during the Sunday planning window, planned to start at 20:00. The first
    /// running week has no committed number — nobody planned it — so adding to it is not a scope
    /// change.
    /// </summary>
    private async Task BootstrapAsync(List<string> events, CancellationToken ct)
    {
        if (await db.Sprints.AnyAsync(ct)) return;

        var config = await configService.GetOrCreateAsync(ct);
        var now = UtcNow();
        var local = LocalNow(config);

        Sprint first;
        if (SprintCalendar.InPlanningWindow(local))
        {
            var startLocal = SprintCalendar.StartAfterClose(local);
            first = new Sprint
            {
                Number = 1,
                Status = SprintStatuses.Planned,
                StartsAtUtc = UserClock.ToUtc(startLocal, config.TimeZone),
                EndsAtUtc = UserClock.ToUtc(SprintCalendar.CloseAfterStart(startLocal), config.TimeZone),
            };
        }
        else
        {
            first = new Sprint
            {
                Number = 1,
                Status = SprintStatuses.Active,
                StartsAtUtc = now,
                StartedAtUtc = now,
                EndsAtUtc = UserClock.ToUtc(SprintCalendar.NextClose(local), config.TimeZone),
            };
        }

        db.Sprints.Add(first);
        await db.SaveChangesAsync(ct);
        events.Add($"created sprint 1 ({first.Status})");
    }

    private async Task CloseAsync(Sprint sprint, Sprint next, CancellationToken ct)
    {
        var tasks = await db.BoardTasks.Where(t => t.SprintId == sprint.Id).ToListAsync(ct);
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).ToList();
        var unfinished = tasks.Where(t => t.Status != BoardTaskStatuses.Done)
            .OrderBy(t => t.Status == BoardTaskStatuses.InProgress ? 0 : 1)
            .ThenBy(t => t.SortOrder).ToList();

        sprint.CommittedPoints ??= tasks.Sum(t => t.Points ?? 0);
        sprint.AddedPoints = tasks.Where(t => t.AddedMidSprint).Sum(t => t.Points ?? 0);
        sprint.CompletedPoints = done.Sum(t => t.Points ?? 0);
        sprint.CarriedOverPoints = unfinished.Sum(t => t.Points ?? 0);
        sprint.Status = SprintStatuses.Closed;
        sprint.ClosedAtUtc = UtcNow();

        // Unfinished work moves on, ahead of anything already planned for next week.
        var alreadyPlanned = await db.BoardTasks.Where(t => t.SprintId == next.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var order = 0;
        foreach (var task in unfinished)
        {
            task.SprintId = next.Id;
            task.CarryOverCount++;
            task.AddedMidSprint = false;
            task.SortOrder = order++;
            task.UpdatedAtUtc = UtcNow();
        }
        foreach (var task in alreadyPlanned) task.SortOrder = order++;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Closed sprint {Number}: {Completed} of {Committed} points done, {Carried} carried over.",
            sprint.Number, sprint.CompletedPoints, sprint.CommittedPoints, sprint.CarriedOverPoints);
    }

    private async Task StartAsync(Sprint sprint, bool manual, CancellationToken ct)
    {
        var tasks = await db.BoardTasks.Where(t => t.SprintId == sprint.Id).ToListAsync(ct);
        var now = UtcNow();

        sprint.Status = SprintStatuses.Active;
        sprint.StartedAtUtc = now;
        if (manual) sprint.StartsAtUtc = now;
        sprint.CommittedPoints = tasks.Sum(t => t.Points ?? 0);
        foreach (var task in tasks) task.AddedMidSprint = false;

        await db.SaveChangesAsync(ct);
        await EnsureFollowingAsync(sprint, ct);
    }

    /// <summary>Sends "time to plan" once per planned sprint, from Sunday 19:00.</summary>
    private async Task<string?> NudgeIfDueAsync(CancellationToken ct)
    {
        if (await db.Sprints.AnyAsync(s => s.Status == SprintStatuses.Active, ct)) return null;

        var planned = await NextPlannedAsync(ct);
        if (planned.PlanningNudgedAtUtc is not null) return null;

        var config = await configService.GetOrCreateAsync(ct);
        var startLocal = UserClock.ToLocal(planned.StartsAtUtc, config.TimeZone);
        var nudgeAt = UserClock.ToUtc(SprintCalendar.NudgeBeforeStart(startLocal), config.TimeZone);
        var now = UtcNow();
        if (now < nudgeAt) return null;

        planned.PlanningNudgedAtUtc = now;
        await db.SaveChangesAsync(ct);

        if (now - nudgeAt > NudgeLateThreshold) return $"skipped late planning nudge for sprint {planned.Number}";

        var last = await db.Sprints.AsNoTracking()
            .Where(s => s.Status == SprintStatuses.Closed)
            .OrderByDescending(s => s.Number)
            .FirstOrDefaultAsync(ct);
        var body = last is null
            ? $"Time to plan sprint {planned.Number}. It starts at 20:00."
            : $"Sprint {last.Number} review: {last.CompletedPoints} of {last.CommittedPoints} points done. " +
              $"Time to plan sprint {planned.Number} — it starts at 20:00.";

        var pushed = await PushAsync(config, body, ct);
        return pushed ? $"sent planning nudge for sprint {planned.Number}" : $"planning nudge for sprint {planned.Number} not pushed";
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

    private async Task<Sprint> RunCycleThenResolveAsync(string view, CancellationToken ct)
    {
        await RunCycleAsync(ct);
        return await ResolveSprintAsync(view, ct);
    }

    /// <summary>Current = the running sprint, else the one being planned. Next = the one after.</summary>
    private async Task<Sprint> ResolveSprintAsync(string view, CancellationToken ct)
    {
        var current = await db.Sprints.FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct)
                      ?? await NextPlannedAsync(ct);
        return view == SprintViews.Next ? await EnsureFollowingAsync(current, ct) : current;
    }

    /// <summary>The earliest planned sprint, created after the latest sprint if there is none.</summary>
    private async Task<Sprint> NextPlannedAsync(CancellationToken ct)
    {
        var planned = await db.Sprints.Where(s => s.Status == SprintStatuses.Planned)
            .OrderBy(s => s.Number).FirstOrDefaultAsync(ct);
        if (planned is not null) return planned;

        var latest = await db.Sprints.OrderByDescending(s => s.Number).FirstAsync(ct);
        return await EnsureFollowingAsync(latest, ct);
    }

    private async Task<Sprint> EnsureFollowingAsync(Sprint sprint, CancellationToken ct)
    {
        var following = await db.Sprints.FirstOrDefaultAsync(s => s.Number == sprint.Number + 1, ct);
        if (following is not null) return following;

        var config = await configService.GetOrCreateAsync(ct);
        var closeLocal = UserClock.ToLocal(sprint.EndsAtUtc, config.TimeZone);
        var startLocal = SprintCalendar.StartAfterClose(closeLocal);
        following = new Sprint
        {
            Number = sprint.Number + 1,
            Status = SprintStatuses.Planned,
            StartsAtUtc = UserClock.ToUtc(startLocal, config.TimeZone),
            EndsAtUtc = UserClock.ToUtc(SprintCalendar.CloseAfterStart(startLocal), config.TimeZone),
        };
        db.Sprints.Add(following);
        await db.SaveChangesAsync(ct);
        return following;
    }

    /// <summary>A running sprint that was planned. The unplanned first week of a new board is open.</summary>
    private static bool IsScopeLocked(Sprint sprint) =>
        sprint.Status == SprintStatuses.Active && sprint.CommittedPoints is not null;

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

    private IQueryable<BoardTask> TaskQuery() =>
        db.BoardTasks.AsNoTracking().Include(t => t.Goal).Include(t => t.Sprint);

    private static IReadOnlyList<BoardTaskDto> Column(IEnumerable<BoardTask> tasks) =>
        tasks.OrderBy(t => t.SortOrder).ThenBy(t => t.Number).Select(ToTaskDto).ToList();

    public static BoardTaskDto ToTaskDto(BoardTask task) => new(
        task.Id,
        ItemKeys.Task(task.Number),
        task.Title,
        task.Description,
        task.Points,
        BoardColumns.Of(task),
        task.SprintId,
        task.Sprint?.Number,
        task.GoalId,
        task.Goal is null ? null : ItemKeys.Goal(task.Goal.Number),
        task.Goal?.Title,
        task.SortOrder,
        task.AddedMidSprint,
        task.CarryOverCount,
        task.CompletedAtUtc,
        task.CreatedAtUtc,
        task.UpdatedAtUtc);

    private static SprintDto ToSprintDto(Sprint sprint, IReadOnlyCollection<BoardTask> tasks)
    {
        var closed = sprint.Status == SprintStatuses.Closed;
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).Sum(t => t.Points ?? 0);
        var added = tasks.Where(t => t.AddedMidSprint).Sum(t => t.Points ?? 0);
        return new SprintDto(
            sprint.Id,
            sprint.Number,
            sprint.Status,
            sprint.StartsAtUtc,
            sprint.EndsAtUtc,
            sprint.StartedAtUtc,
            sprint.ClosedAtUtc,
            sprint.CommittedPoints,
            closed ? sprint.AddedPoints ?? 0 : added,
            sprint.RemovedPoints,
            closed ? sprint.CompletedPoints ?? 0 : done,
            closed ? sprint.CarriedOverPoints : null,
            tasks.Sum(t => t.Points ?? 0),
            tasks.Count,
            tasks.Count(t => t.Points is null),
            tasks.Count(t => t.CarryOverCount > 0),
            IsScopeLocked(sprint));
    }

    private static void RequireView(string view)
    {
        if (!SprintViews.All.Contains(view))
            throw new BoardValidationException($"Sprint must be one of: {string.Join(", ", SprintViews.All)}.");
    }

    private static void ValidatePoints(int? points)
    {
        if (points is int value && !StoryPoints.Allowed.Contains(value))
            throw new BoardValidationException(
                $"Story points must be one of {string.Join(", ", StoryPoints.Allowed)} (Fibonacci), or left unestimated.");
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
