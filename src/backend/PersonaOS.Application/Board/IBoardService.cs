using PersonaOS.Application.Common.Exceptions;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Board;

/// <summary>
/// The board columns. The backlog is not one of them any more — it lives on the plan (backlog)
/// page, and the board shows the running sprint only.
/// </summary>
public static class BoardColumns
{
    public const string Backlog = "backlog";
    public const string Todo = BoardTaskStatuses.Todo;
    public const string InProgress = BoardTaskStatuses.InProgress;
    public const string Done = BoardTaskStatuses.Done;

    /// <summary>Every place a task can sit, including outside a sprint.</summary>
    public static readonly IReadOnlyList<string> All = [Backlog, Todo, InProgress, Done];

    /// <summary>The columns the board itself shows.</summary>
    public static readonly IReadOnlyList<string> Board = [Todo, InProgress, Done];

    /// <summary>
    /// A task with no sprint is in the Backlog — unless it is already done, which is how work
    /// finished outside a sprint looks (a completed sub-goal converted to a task, say). Calling
    /// that "Backlog" made finished work look unstarted wherever tasks are listed.
    /// </summary>
    public static string Of(BoardTask task) => task.SprintId is not null
        ? task.Status
        : task.Status == BoardTaskStatuses.Done ? Done : Backlog;
}

public record BoardTaskDto(
    int Id,
    string Key,
    string Title,
    string? Description,
    int? Points,
    string Priority,
    string Column,
    int? SprintId,
    string? SprintKey,
    string? SprintName,
    int? GoalId,
    string? GoalKey,
    string? GoalTitle,
    int SortOrder,
    bool AddedMidSprint,
    int CarryOverCount,
    int CommentCount,
    int AttachmentCount,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// A sprint with its totals. For a closed sprint the totals are the frozen report; for a running
/// or planned sprint they are live. <see cref="CommittedPoints"/> is null until a sprint starts.
/// </summary>
public record SprintDto(
    int Id,
    string Key,
    int Number,
    string? Name,
    string Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime? StartedAtUtc,
    DateTime? ClosedAtUtc,
    int? CommittedPoints,
    int AddedPoints,
    int RemovedPoints,
    int CompletedPoints,
    int? CarriedOverPoints,
    int TotalPoints,
    int TaskCount,
    int DoneTaskCount,
    int UnestimatedCount,
    int CarriedInCount,
    bool ScopeLocked);

/// <summary>The running sprint's board. <see cref="Sprint"/> is null when nothing is running.</summary>
public record BoardView(
    SprintDto? Sprint,
    double? Velocity,
    int WipLimit,
    IReadOnlyList<int> AllowedPoints,
    IReadOnlyList<BoardTaskDto> Todo,
    IReadOnlyList<BoardTaskDto> InProgress,
    IReadOnlyList<BoardTaskDto> Done);

/// <summary>One sprint on the plan page, with the work sitting in it.</summary>
public record SprintPlan(SprintDto Sprint, IReadOnlyList<BoardTaskDto> Tasks);

/// <summary>The plan (backlog) page: the running sprint, sprints to come, then the backlog.</summary>
public record PlanView(
    IReadOnlyList<SprintPlan> Sprints,
    IReadOnlyList<BoardTaskDto> Backlog,
    double? Velocity,
    IReadOnlyList<int> AllowedPoints,
    IReadOnlyList<string> Priorities);

/// <summary>One day of a sprint: points still open at the end of that day.</summary>
public record BurndownPoint(DateOnly Date, int RemainingPoints, int CompletedPoints);

/// <summary>A sprint in full: its totals, its work, and how the points came down.</summary>
public record SprintDetail(
    SprintDto Sprint,
    IReadOnlyList<BoardTaskDto> Tasks,
    IReadOnlyList<BurndownPoint> Burndown,
    double? Velocity);

/// <summary>A task in full, with everything hanging off it.</summary>
public record TaskDetail(
    BoardTaskDto Task,
    IReadOnlyList<WorkItemCommentDto> Comments,
    IReadOnlyList<WorkItemAttachmentDto> Attachments);

public record WorkItemCommentDto(int Id, string Author, string Body, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record WorkItemAttachmentDto(
    int Id, string FileName, string ContentType, long SizeBytes, DateTime CreatedAtUtc);

public record CreateTaskRequest(
    string Title,
    string? Description = null,
    int? Points = null,
    string? Priority = null,
    int? GoalId = null,
    /// <summary>Sprint key like "SPRINT-2"; null puts the task in the backlog.</summary>
    string? SprintKey = null,
    bool AcknowledgeScopeChange = false,
    /// <summary>
    /// Board column to create it in: todo (the default), in_progress or done. The last two need a
    /// running sprint, the same rule a move follows.
    /// </summary>
    string? Column = null);

/// <summary>Partial update; null fields are left unchanged.</summary>
public record UpdateTaskRequest(
    string? Title = null,
    string? Description = null,
    bool ClearDescription = false,
    int? Points = null,
    bool ClearPoints = false,
    string? Priority = null,
    int? GoalId = null,
    bool ClearGoal = false);

/// <param name="Column">Target column: backlog, todo, in_progress or done.</param>
/// <param name="SprintKey">
/// Sprint to move into, e.g. "SPRINT-2". Ignored for the backlog; when omitted the task stays in
/// the sprint it is already in (or joins the running one when it comes from the backlog).
/// </param>
/// <param name="Index">Position within the target column; the end when omitted.</param>
public record MoveTaskRequest(
    string Column,
    string? SprintKey = null,
    int? Index = null,
    bool AcknowledgeScopeChange = false);

/// <param name="StartsAtLocal">Start, in the user's own time zone.</param>
/// <param name="EndsAtLocal">
/// End, in the user's own time zone. Left out, the sprint closes on the first Sunday after it
/// starts (<see cref="PersonaOS.Domain.Services.SprintCalendar.CloseAfterStart"/>).
/// </param>
/// <param name="StartsOn">
/// The start as a day, which takes the rhythm's start time; what the web form sends, since it asks
/// for a date and shows the end it implies. Wins over <paramref name="StartsAtLocal"/>.
/// </param>
public record CreateSprintRequest(
    string? Name,
    DateTime? StartsAtLocal = null,
    DateTime? EndsAtLocal = null,
    DateOnly? StartsOn = null);

/// <summary>
/// Partial update. A new start moves the end to the first Sunday after it unless an end is given
/// too, the same rule as creating a sprint.
/// </summary>
public record UpdateSprintRequest(
    string? Name = null,
    bool ClearName = false,
    DateTime? StartsAtLocal = null,
    DateTime? EndsAtLocal = null,
    DateOnly? StartsOn = null);

/// <param name="MoveUnfinishedToSprintKey">
/// Where unfinished work goes: a sprint key, or null for the backlog. Defaults to the next
/// planned sprint when there is one.
/// </param>
public record CompleteSprintRequest(string? MoveUnfinishedToSprintKey = null, bool ToBacklog = false);

/// <summary>Closed sprints, newest first, plus the recent average of completed points.</summary>
public record SprintReport(double? Velocity, IReadOnlyList<SprintDto> Sprints);

/// <summary>Invalid input to a board operation; message is user/model-presentable.</summary>
public class BoardValidationException(string message)
    : DomainValidationException(message, "board_validation_failed");

/// <summary>
/// A change to a running sprint's scope that was not acknowledged. Clients show the message and
/// retry with <c>acknowledgeScopeChange</c> once the user agrees.
/// </summary>
public class ScopeChangeException(string message)
    : DomainValidationException(message, "scope_change_unacknowledged");

public interface IBoardService
{
    /// <summary>The running sprint's three columns, or an empty board when nothing is running.</summary>
    Task<BoardView> GetBoardAsync(CancellationToken ct = default);

    /// <summary>The plan page: running sprint, sprints to come, and the backlog underneath.</summary>
    Task<PlanView> GetPlanAsync(CancellationToken ct = default);

    Task<SprintReport> GetReportAsync(int count = 12, CancellationToken ct = default);

    Task<SprintDetail?> GetSprintAsync(int id, CancellationToken ct = default);

    /// <summary>Resolves a <c>SPRINT-n</c> key (or its bare number) to the sprint's id.</summary>
    Task<int?> ResolveSprintKeyAsync(string? key, CancellationToken ct = default);

    Task<SprintDto> CreateSprintAsync(CreateSprintRequest request, CancellationToken ct = default);

    Task<SprintDto?> UpdateSprintAsync(int id, UpdateSprintRequest request, CancellationToken ct = default);

    /// <summary>Starts a planned sprint. Only one sprint runs at a time.</summary>
    Task<SprintDto?> StartSprintAsync(int id, CancellationToken ct = default);

    /// <summary>Completes the running sprint, moving unfinished work on.</summary>
    Task<SprintDto?> CompleteSprintAsync(int id, CompleteSprintRequest request, CancellationToken ct = default);

    /// <summary>Deletes a sprint that has not started; its tasks go back to the backlog.</summary>
    Task<bool> DeleteSprintAsync(int id, CancellationToken ct = default);

    Task<TaskDetail?> GetTaskAsync(int id, CancellationToken ct = default);

    /// <summary>Resolves a <c>TASK-n</c> key (or its bare number) to the task's id.</summary>
    Task<int?> ResolveTaskKeyAsync(string? key, CancellationToken ct = default);

    Task<BoardTaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default);

    Task<BoardTaskDto?> UpdateTaskAsync(int id, UpdateTaskRequest request, CancellationToken ct = default);

    Task<BoardTaskDto?> MoveTaskAsync(int id, MoveTaskRequest request, CancellationToken ct = default);

    Task<bool> DeleteTaskAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Sends the Sunday planning nudge: complete the sprint that has reached its end, or plan one
    /// when nothing is running. Idempotent per day; sprints themselves never start or stop by
    /// themselves.
    /// </summary>
    Task<IReadOnlyList<string>> RunRemindersAsync(CancellationToken ct = default);
}
