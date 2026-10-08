using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.Planner;

/// <summary>
/// An event on one day, with its optional goal link resolved for display. For a repeating event
/// this is one occurrence: <see cref="Id"/> is the series, <see cref="OccurrenceDate"/> says which.
/// </summary>
/// <param name="ScheduledTime">Start time; null means the event lasts all day.</param>
/// <param name="EndTime">End time of a timed event (the default length after the start when none was set; on <paramref name="EndDate"/> for one over several days); null for all-day.</param>
/// <param name="NotifyBeforeMinutes">Minutes before the start the phone is notified; null means never.</param>
/// <param name="Recurrence">The repeat rule as text; null for a one-off.</param>
/// <param name="RecurrenceText">The rule in words, like "Weekly on Saturday".</param>
/// <param name="OccurrenceDate">The day the series' rule put this occurrence on: what to send back to change just this one.</param>
/// <param name="IsChanged">This occurrence was edited on its own.</param>
/// <param name="EndDate">The last day: the same as <paramref name="Date"/> unless the event lasts several days.</param>
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
    string? TaskKey = null,
    TimeOnly? EndTime = null,
    int? NotifyBeforeMinutes = null,
    string? Recurrence = null,
    string? RecurrenceText = null,
    DateOnly? OccurrenceDate = null,
    bool IsChanged = false,
    DateOnly? EndDate = null);

/// <summary>One day's plan.</summary>
public record PlannerDay(DateOnly Date, IReadOnlyList<PlannerItemDto> Items);

/// <param name="EndDate">The last day of an event over several days; null (or the same day) for one day.</param>
public record CreatePlannerItemRequest(
    string Title,
    DateOnly Date,
    string? Notes = null,
    TimeOnly? ScheduledTime = null,
    int? SortOrder = null,
    int? GoalId = null,
    int? TaskId = null,
    TimeOnly? EndTime = null,
    int? NotifyBeforeMinutes = null,
    string? Recurrence = null,
    DateOnly? EndDate = null);

/// <summary>
/// Partial update; null fields are left unchanged. A new <see cref="Date"/> keeps the number of days
/// an event lasts; <see cref="EndDate"/> sets its last day (counted from the new day, or the day of
/// the occurrence named, for a repeating event); the same day makes it a one-day event.
/// </summary>
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
    bool ClearTask = false,
    TimeOnly? EndTime = null,
    bool ClearEndTime = false,
    int? NotifyBeforeMinutes = null,
    bool ClearNotify = false,
    string? Recurrence = null,
    bool ClearRecurrence = false,
    DateOnly? EndDate = null);

/// <summary>Which events of a repeating series a change or delete applies to.</summary>
public enum PlannerEditScope
{
    /// <summary>Every event in the series (and the only choice for an event that does not repeat).</summary>
    All,

    /// <summary>Just the occurrence named.</summary>
    This,

    /// <summary>The occurrence named and every one after it.</summary>
    Following,
}

/// <summary>What a change or delete is aimed at: the scope, and for a repeating event which occurrence.</summary>
public record PlannerTarget(PlannerEditScope Scope = PlannerEditScope.All, DateOnly? Occurrence = null);

/// <summary>Invalid input to a planner operation; message is user/model-presentable.</summary>
public class PlannerValidationException(string message)
    : DomainValidationException(message, "planner_validation_failed");

public interface IPlannerService
{
    /// <summary>All items planned for a single day, ordered by time then sort order.</summary>
    Task<PlannerDay> GetDayAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Days in an inclusive date range that have items (for the week, month and year views). An
    /// event over several days is listed once, on its first day in the range.
    /// </summary>
    Task<IReadOnlyList<PlannerDay>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>One event; for a repeating one the occurrence named, or the series' first.</summary>
    Task<PlannerItemDto?> GetAsync(int id, DateOnly? occurrence = null, CancellationToken ct = default);

    Task<PlannerItemDto> CreateAsync(CreatePlannerItemRequest request, CancellationToken ct = default);

    Task<PlannerItemDto?> UpdateAsync(
        int id, UpdatePlannerItemRequest request, PlannerTarget? target = null, CancellationToken ct = default);

    /// <summary>Sets the status (planned / done / skipped); a repeating event needs the occurrence.</summary>
    Task<PlannerItemDto?> UpdateStatusAsync(
        int id, string status, DateOnly? occurrence = null, CancellationToken ct = default);

    /// <summary>Moves an item to another day, keeping everything else intact.</summary>
    Task<PlannerItemDto?> MoveAsync(int id, DateOnly date, PlannerTarget? target = null, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, PlannerTarget? target = null, CancellationToken ct = default);
}
