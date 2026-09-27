namespace PersonaOS.Application.Goals;

/// <summary>A task under a goal, as listed with the goal.</summary>
public record GoalTaskSummary(
    int Id,
    string Key,
    string Title,
    int? Points,
    string Column,
    int? SprintNumber);

/// <summary>A child goal, as listed with its parent.</summary>
public record GoalChildSummary(
    int Id,
    string Key,
    string Title,
    string PeriodType,
    string Slot,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    int EffectiveProgress);

/// <summary>A goal with its place in the hierarchy and its derived progress.</summary>
/// <param name="Slot">"2026", "Q4 2026" or "Oct 2026".</param>
/// <param name="EffectiveProgress">See GoalProgressCalculator.</param>
/// <param name="CompleteProblem">
/// Why the goal cannot be completed now (open tasks, active child goals), or null.
/// </param>
public record GoalDto(
    int Id,
    string Key,
    string Title,
    string? Description,
    string PeriodType,
    string Slot,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int? ParentId,
    string? ParentKey,
    string? ParentTitle,
    string Status,
    string Priority,
    int Progress,
    int EffectiveProgress,
    int TaskCount,
    int DoneTaskCount,
    int ChildCount,
    int CompletedChildCount,
    string? CompleteProblem,
    int CommentCount,
    int AttachmentCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<GoalTaskSummary> Tasks,
    IReadOnlyList<GoalChildSummary> Children);

/// <summary>
/// A new goal. Its dates come from its type and slot (see GoalCalendar), never from free dates.
/// </summary>
/// <param name="Year">The slot's year: the current year or a later one.</param>
/// <param name="Quarter">1–4, for a quarter goal.</param>
/// <param name="Month">1–12, for a month goal.</param>
/// <param name="PeriodStart">
/// A year goal's start (today when omitted). For a quarter or month without a slot, the day
/// picks the slot.
/// </param>
/// <param name="ParentId">A year for a quarter, a quarter for a month; null for a standalone goal.</param>
public record CreateGoalRequest(
    string Title,
    string? Description,
    string PeriodType,
    int? Year = null,
    int? Quarter = null,
    int? Month = null,
    DateOnly? PeriodStart = null,
    int? ParentId = null,
    int Progress = 0,
    string? Priority = null);

/// <summary>
/// Partial update; null fields are left unchanged. A new type or slot (only for a standalone
/// goal with no child goals) gives the goal new dates by the same rules as creating one.
/// </summary>
public record UpdateGoalRequest(
    string? Title = null,
    string? Description = null,
    bool ClearDescription = false,
    int? Progress = null,
    string? Priority = null,
    string? PeriodType = null,
    int? Year = null,
    int? Quarter = null,
    int? Month = null,
    DateOnly? PeriodStart = null);

/// <summary>
/// Moves a goal under another parent, into the slot given (the parent's year when no year is
/// given); its child goals keep their position and move with it. A null parent detaches it: it
/// becomes standalone and keeps its dates.
/// </summary>
public record MoveGoalRequest(int? ParentId, int? Year = null, int? Quarter = null, int? Month = null);

/// <summary>What deleting a goal does with its tasks.</summary>
public static class GoalTaskActions
{
    /// <summary>The tasks stay, without a goal.</summary>
    public const string Keep = "keep";

    /// <summary>The tasks are deleted with the goal.</summary>
    public const string Delete = "delete";

    /// <summary>Each task goes where <see cref="DeleteGoalRequest.Reassign"/> maps it.</summary>
    public const string Reassign = "reassign";

    public static readonly IReadOnlyList<string> All = [Keep, Delete, Reassign];
}

/// <param name="TaskAction">One of <see cref="GoalTaskActions"/>; keep when omitted.</param>
/// <param name="Reassign">
/// For reassign: every task's key mapped to a monthly goal's key, or to null for no goal.
/// </param>
public record DeleteGoalRequest(string? TaskAction = null, IReadOnlyDictionary<string, string?>? Reassign = null);

/// <summary>Invalid input to a goal operation; message is user/model-presentable.</summary>
public class GoalValidationException(string message)
    : Common.Exceptions.DomainValidationException(message, "goal_validation_failed");

/// <summary>
/// Goals and their year → quarter → month hierarchy. Every change has a Validate twin that runs
/// the same checks without saving anything, so the assistant's confirmation card is refused
/// before the user sees it rather than failing after they confirm.
/// </summary>
public interface IGoalService
{
    /// <summary>All goals, ordered by start, each with its tasks, children and derived progress.</summary>
    Task<IReadOnlyList<GoalDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>One goal, or null when it does not exist.</summary>
    Task<GoalDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Resolves a <c>GOAL-n</c> key (or its bare number) to the goal's id.</summary>
    Task<int?> ResolveKeyAsync(string? key, CancellationToken ct = default);

    Task ValidateCreateAsync(CreateGoalRequest request, CancellationToken ct = default);

    Task<GoalDto> CreateAsync(CreateGoalRequest request, CancellationToken ct = default);

    Task<GoalDto?> UpdateAsync(int id, UpdateGoalRequest request, CancellationToken ct = default);

    /// <summary>Refuses completing a month with open tasks or a goal with active child goals.</summary>
    Task ValidateStatusAsync(int id, string status, CancellationToken ct = default);

    Task<GoalDto?> UpdateStatusAsync(int id, string status, CancellationToken ct = default);

    Task ValidateMoveAsync(int id, MoveGoalRequest request, CancellationToken ct = default);

    Task<GoalDto?> MoveAsync(int id, MoveGoalRequest request, CancellationToken ct = default);

    /// <summary>Refuses a goal that still has child goals, and a reassignment that cannot work.</summary>
    Task ValidateDeleteAsync(int id, DeleteGoalRequest request, CancellationToken ct = default);

    /// <summary>Deletes a goal and does what the request says with its tasks. False when it does not exist.</summary>
    Task<bool> DeleteAsync(int id, DeleteGoalRequest request, CancellationToken ct = default);
}
