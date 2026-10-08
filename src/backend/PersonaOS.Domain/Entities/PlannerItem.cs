namespace PersonaOS.Domain.Entities;

/// <summary>
/// An event on the user's planner: a task or appointment on a <see cref="Date"/>, all day or from a
/// start time to an end time, optionally repeating and optionally announcing itself beforehand.
/// Optionally linked to a <see cref="Goal"/> so day-to-day work ties back to
/// longer-horizon intentions.
///
/// A repeating event is ONE row, the series; its occurrences are worked out when read and a change
/// to a single one lives in a <see cref="PlannerOccurrence"/>. Times are the user's local time.
/// </summary>
public class PlannerItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    /// <summary>
    /// The day this item is planned for (user's local date, not UTC-derived). For a repeating
    /// event, the day of its first occurrence.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>The start time of day; null means the event lasts all day.</summary>
    public TimeOnly? ScheduledTime { get; set; }

    /// <summary>
    /// The last day of an event that lasts several days; null when it ends on <see cref="Date"/>.
    /// For a repeating event, the last day of its first occurrence: every occurrence lasts as many
    /// days.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// The end time of day: later than the start on the same day, or any time on
    /// <see cref="EndDate"/> for an event over several days (which needs one). Null on a timed
    /// one-day event means "no end set" and it is shown as
    /// <see cref="PlannerEvents.DefaultDurationMinutes"/> long.
    /// </summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>
    /// Minutes before the start to send a notification to the phone; null means no notification.
    /// An all-day event counts from <see cref="PlannerEvents.AllDayAlertReference"/>.
    /// </summary>
    public int? NotifyBeforeMinutes { get; set; }

    /// <summary>How the event repeats, as <c>RecurrenceRule</c> text; null means it happens once.</summary>
    public string? Recurrence { get; set; }

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

/// <summary>Limits and defaults every planner client and the server agree on.</summary>
public static class PlannerEvents
{
    /// <summary>How long a timed event with no end time is shown and treated as lasting.</summary>
    public const int DefaultDurationMinutes = 60;

    /// <summary>The longest lead for a notification: four weeks, as in Google Calendar.</summary>
    public const int MaxNotifyBeforeMinutes = 4 * 7 * 24 * 60;

    /// <summary>The lead a new event starts with when the person does not choose one.</summary>
    public const int DefaultNotifyBeforeMinutes = 30;

    /// <summary>The time of day an all-day event's notification counts from.</summary>
    public static readonly TimeOnly AllDayAlertReference = new(9, 0);
}

/// <summary>
/// A change to one occurrence of a repeating <see cref="PlannerItem"/>, found by the day the
/// rule would have put it on. Empty (null) fields mean "as the series says".
/// </summary>
public class PlannerOccurrence
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public PlannerItem? Item { get; set; }

    /// <summary>The day the series' rule put this occurrence on, whatever it was moved to.</summary>
    public DateOnly OriginalDate { get; set; }

    /// <summary>The occurrence was deleted ("this event" only).</summary>
    public bool IsCancelled { get; set; }

    public string? Title { get; set; }
    public string? Notes { get; set; }

    /// <summary>Moved to this day.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>
    /// Ends on this day (made longer or shorter than the series); null means it lasts as many days
    /// as the series' occurrences do, from whichever day it is on.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    public TimeOnly? ScheduledTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    /// <summary>True makes this one all-day (the times above are ignored); false uses the times above.</summary>
    public bool? AllDay { get; set; }

    /// <summary>One of <see cref="PlannerItemStatuses"/>; null means planned.</summary>
    public string? Status { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Remembers that the notification for one occurrence, at one start time, was sent, so it goes out
/// once. Moving the occurrence changes the start time and so allows a new notification.
/// </summary>
public class PlannerAlertLog
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public PlannerItem? Item { get; set; }

    public DateOnly OccurrenceDate { get; set; }

    /// <summary>The start the notification was sent for.</summary>
    public DateTime StartUtc { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
