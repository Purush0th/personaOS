namespace PersonaOS.Application.Goals;

/// <summary>A task under a goal, as listed with the goal.</summary>
public record GoalTaskSummary(
    int Id,
    string Key,
    string Title,
    int? Points,
    string Column,
    int? SprintNumber);

/// <summary>A goal (the epic) with progress derived from its tasks.</summary>
public record GoalDto(
    int Id,
    string Key,
    string Title,
    string? Description,
    string PeriodType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string Priority,
    int Progress,
    int EffectiveProgress,
    int TaskCount,
    int DoneTaskCount,
    int TotalPoints,
    int DonePoints,
    int CommentCount,
    int AttachmentCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<GoalTaskSummary> Tasks);

/// <param name="PeriodEnd">
/// The day the goal is due. Null takes the period type's default: a month on, 90 days on, or
/// 31 December.
/// </param>
public record CreateGoalRequest(
    string Title,
    string? Description,
    string PeriodType,
    DateOnly PeriodStart,
    int Progress = 0,
    string? Priority = null,
    DateOnly? PeriodEnd = null);

/// <summary>
/// Partial update; null fields are left unchanged. Changing the type or start without an end
/// gives the goal that type's default end again.
/// </summary>
public record UpdateGoalRequest(
    string? Title = null,
    string? Description = null,
    bool ClearDescription = false,
    string? PeriodType = null,
    DateOnly? PeriodStart = null,
    int? Progress = null,
    string? Priority = null,
    DateOnly? PeriodEnd = null);

/// <summary>Invalid input to a goal operation; message is user/model-presentable.</summary>
public class GoalValidationException(string message)
    : Common.Exceptions.DomainValidationException(message, "goal_validation_failed");

public interface IGoalService
{
    /// <summary>All goals, ordered by period, each with its tasks and derived progress.</summary>
    Task<IReadOnlyList<GoalDto>> GetAllAsync(bool includeDropped = false, CancellationToken ct = default);

    /// <summary>One goal, or null when it does not exist.</summary>
    Task<GoalDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Resolves a <c>GOAL-n</c> key (or its bare number) to the goal's id.</summary>
    Task<int?> ResolveKeyAsync(string? key, CancellationToken ct = default);

    Task<GoalDto> CreateAsync(CreateGoalRequest request, CancellationToken ct = default);

    Task<GoalDto?> UpdateAsync(int id, UpdateGoalRequest request, CancellationToken ct = default);

    /// <summary>Sets the status; completing a goal also sets its own progress to 100.</summary>
    Task<GoalDto?> UpdateStatusAsync(int id, string status, CancellationToken ct = default);

    /// <summary>Deletes a goal. Its tasks stay, without a goal. False when it does not exist.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
