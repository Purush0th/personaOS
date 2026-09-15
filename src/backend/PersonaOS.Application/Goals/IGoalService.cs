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
    string Status,
    int Progress,
    int EffectiveProgress,
    int TaskCount,
    int DoneTaskCount,
    int TotalPoints,
    int DonePoints,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<GoalTaskSummary> Tasks);

public record CreateGoalRequest(
    string Title,
    string? Description,
    string PeriodType,
    DateOnly PeriodStart,
    int Progress = 0);

/// <summary>Partial update; null fields are left unchanged.</summary>
public record UpdateGoalRequest(
    string? Title = null,
    string? Description = null,
    string? PeriodType = null,
    DateOnly? PeriodStart = null,
    int? Progress = null);

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
