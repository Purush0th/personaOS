namespace PersonaOS.Domain.Entities;

/// <summary>
/// A task on the user's daily planner for a specific <see cref="Date"/>.
/// Optionally linked to a <see cref="Goal"/> so day-to-day work ties back to
/// longer-horizon intentions.
/// </summary>
public class PlannerItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    /// <summary>The day this item is planned for (user's local date, not UTC-derived).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Optional time of day; null means the item is unscheduled within the day.</summary>
    public TimeOnly? ScheduledTime { get; set; }

    /// <summary>Manual ordering within a day (ascending). Ties break by Id.</summary>
    public int SortOrder { get; set; }

    /// <summary>One of <see cref="PlannerItemStatuses"/>.</summary>
    public string Status { get; set; } = PlannerItemStatuses.Planned;

    /// <summary>Optional link to the goal this task contributes to.</summary>
    public int? GoalId { get; set; }
    public Goal? Goal { get; set; }

    /// <summary>Optional link to the sprint-board task this item is a day's work on.</summary>
    public int? TaskId { get; set; }
    public BoardTask? Task { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Canonical planner item statuses.</summary>
public static class PlannerItemStatuses
{
    public const string Planned = "planned";
    public const string Done = "done";
    public const string Skipped = "skipped";

    public static readonly IReadOnlyList<string> All = [Planned, Done, Skipped];
}
