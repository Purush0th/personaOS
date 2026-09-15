using PersonaOS.Application.Common.Exceptions;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Board;

/// <summary>The four board columns. A task's column follows from its sprint and status.</summary>
public static class BoardColumns
{
    public const string Backlog = "backlog";
    public const string Todo = BoardTaskStatuses.Todo;
    public const string InProgress = BoardTaskStatuses.InProgress;
    public const string Done = BoardTaskStatuses.Done;

    public static readonly IReadOnlyList<string> All = [Backlog, Todo, InProgress, Done];

    public static string Of(BoardTask task) => task.SprintId is null ? Backlog : task.Status;
}

/// <summary>Which sprint a board request is about.</summary>
public static class SprintViews
{
    /// <summary>The running sprint, or the one being planned when none is running.</summary>
    public const string Current = "current";

    /// <summary>The sprint after the current one, for planning ahead.</summary>
    public const string Next = "next";

    public static readonly IReadOnlyList<string> All = [Current, Next];
}

public record BoardTaskDto(
    int Id,
    string Key,
    string Title,
    string? Description,
    int? Points,
    string Column,
    int? SprintId,
    int? SprintNumber,
    int? GoalId,
    string? GoalKey,
    string? GoalTitle,
    int SortOrder,
    bool AddedMidSprint,
    int CarryOverCount,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// A sprint with its totals. For a closed sprint the totals are the frozen report; for a running
/// or planned sprint they are live. <see cref="CommittedPoints"/> is null until a sprint starts,
/// and stays null for the first, unplanned week of a new board until it closes.
/// </summary>
public record SprintDto(
    int Id,
    int Number,
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
    int UnestimatedCount,
    int CarriedInCount,
    bool ScopeLocked);

public record BoardView(
    string View,
    SprintDto Sprint,
    bool InPlanningWindow,
    bool CanStartSprint,
    double? Velocity,
    int WipLimit,
    IReadOnlyList<int> AllowedPoints,
    IReadOnlyList<BoardTaskDto> Backlog,
    IReadOnlyList<BoardTaskDto> Todo,
    IReadOnlyList<BoardTaskDto> InProgress,
    IReadOnlyList<BoardTaskDto> Done);

/// <summary>Closed sprints, newest first, plus the recent average of completed points.</summary>
public record SprintReport(double? Velocity, IReadOnlyList<SprintDto> Sprints);

public record CreateTaskRequest(
    string Title,
    string? Description = null,
    int? Points = null,
    int? GoalId = null,
    string Destination = BoardColumns.Backlog,
    bool AcknowledgeScopeChange = false);

/// <summary>Partial update; null fields are left unchanged.</summary>
public record UpdateTaskRequest(
    string? Title = null,
    string? Description = null,
    int? Points = null,
    bool ClearPoints = false,
    int? GoalId = null,
    bool ClearGoal = false);

/// <param name="Column">Target column.</param>
/// <param name="Sprint">
/// <see cref="SprintViews"/> value for the target sprint. Ignored for the Backlog. When omitted,
/// the task stays in its sprint, or goes to the current sprint when it comes from the Backlog.
/// </param>
/// <param name="Index">Position within the target column; the end when omitted.</param>
public record MoveTaskRequest(
    string Column,
    string? Sprint = null,
    int? Index = null,
    bool AcknowledgeScopeChange = false);

/// <summary>What one pass of the automatic sprint cycle did.</summary>
public record SprintCycleResult(IReadOnlyList<string> Events);

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
    /// <summary>The board for the current or next sprint. Brings the sprint cycle up to date first.</summary>
    Task<BoardView> GetBoardAsync(string view = SprintViews.Current, CancellationToken ct = default);

    Task<SprintReport> GetReportAsync(int count = 12, CancellationToken ct = default);

    Task<BoardTaskDto?> GetTaskAsync(int id, CancellationToken ct = default);

    /// <summary>Resolves a <c>TASK-n</c> key (or its bare number) to the task's id.</summary>
    Task<int?> ResolveTaskKeyAsync(string? key, CancellationToken ct = default);

    Task<BoardTaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default);

    Task<BoardTaskDto?> UpdateTaskAsync(int id, UpdateTaskRequest request, CancellationToken ct = default);

    Task<BoardTaskDto?> MoveTaskAsync(int id, MoveTaskRequest request, CancellationToken ct = default);

    Task<bool> DeleteTaskAsync(int id, CancellationToken ct = default);

    /// <summary>Starts the planned sprint now. Only possible while no sprint is running.</summary>
    Task<SprintDto> StartSprintAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes a sprint that has ended (carrying unfinished work over), starts the planned sprint
    /// when its time has come, and sends the Sunday planning nudge. Idempotent; catches up after
    /// downtime.
    /// </summary>
    Task<SprintCycleResult> RunCycleAsync(CancellationToken ct = default);
}
