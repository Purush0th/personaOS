using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.Planner;

/// <summary>A planner item with its optional goal link resolved for display.</summary>
public record PlannerItemDto(
    int Id,
    string Title,
    string? Notes,
    DateOnly Date,
    TimeOnly? ScheduledTime,
    int SortOrder,
    string Status,
    int? GoalId,
    string? GoalTitle,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? GoalKey = null,
    int? TaskId = null,
    string? TaskKey = null);

/// <summary>One day's plan.</summary>
public record PlannerDay(DateOnly Date, IReadOnlyList<PlannerItemDto> Items);

public record CreatePlannerItemRequest(
    string Title,
    DateOnly Date,
    string? Notes = null,
    TimeOnly? ScheduledTime = null,
    int? SortOrder = null,
    int? GoalId = null,
    int? TaskId = null);

/// <summary>Partial update; null fields are left unchanged.</summary>
public record UpdatePlannerItemRequest(
    string? Title = null,
    string? Notes = null,
    DateOnly? Date = null,
    TimeOnly? ScheduledTime = null,
    int? SortOrder = null,
    int? GoalId = null,
    bool ClearGoal = false,
    bool ClearScheduledTime = false,
    int? TaskId = null,
    bool ClearTask = false);

/// <summary>Invalid input to a planner operation; message is user/model-presentable.</summary>
public class PlannerValidationException(string message)
    : DomainValidationException(message, "planner_validation_failed");

public interface IPlannerService
{
    /// <summary>All items planned for a single day, ordered by time then sort order.</summary>
    Task<PlannerDay> GetDayAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Days in an inclusive date range that have items (for week/month views).</summary>
    Task<IReadOnlyList<PlannerDay>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<PlannerItemDto?> GetAsync(int id, CancellationToken ct = default);

    Task<PlannerItemDto> CreateAsync(CreatePlannerItemRequest request, CancellationToken ct = default);

    Task<PlannerItemDto?> UpdateAsync(int id, UpdatePlannerItemRequest request, CancellationToken ct = default);

    /// <summary>Sets the status (planned / done / skipped).</summary>
    Task<PlannerItemDto?> UpdateStatusAsync(int id, string status, CancellationToken ct = default);

    /// <summary>Moves an item to another day, keeping everything else intact.</summary>
    Task<PlannerItemDto?> MoveAsync(int id, DateOnly date, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
