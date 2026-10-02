using System.Globalization;
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
public partial class BoardService(
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

    /// <summary>A requested start: a day takes the rhythm's start time; otherwise the exact time given.</summary>
    private static DateTime? RequestedStart(DateOnly? startsOn, DateTime? startsAtLocal) =>
        startsOn?.ToDateTime(SprintCalendar.StartTime) ?? startsAtLocal;

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

    /// <summary>A task's goal must exist, be a monthly goal and still be open (GoalService.TaskGoalProblem).</summary>
    private async Task RequireGoalAsync(int? goalId, CancellationToken ct)
    {
        if (goalId is not int id) return;
        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new BoardValidationException($"Goal {id} does not exist.");
        if (Goals.GoalService.TaskGoalProblem(goal) is { } problem) throw new BoardValidationException(problem);
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

    /// <summary>Public so the tools can refuse a bad value before it becomes a card, not after Confirm.</summary>
    public static void ValidatePoints(int? points)
    {
        if (points is int value && !ValuePoints.Allowed.Contains(value))
            throw new BoardValidationException(
                $"Value points must be one of {string.Join(", ", ValuePoints.Allowed)} (Fibonacci), or left unestimated.");
    }

    public static string? ValidatePriority(string? priority)
    {
        if (priority is null) return null;
        var normalized = priority.Trim().ToLowerInvariant();
        return WorkItemPriorities.All.Contains(normalized)
            ? normalized
            : throw new BoardValidationException(
                $"Priority must be one of: {string.Join(", ", WorkItemPriorities.All)}.");
    }

    public static string RequireTitle(string? title)
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
