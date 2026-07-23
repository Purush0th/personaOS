namespace PersonaOS.Domain.Entities;

/// <summary>
/// A one-off reminder the assistant or user schedules. <see cref="DueAtUtc"/> is
/// always UTC; the user's configured time zone is only used to interpret and
/// display local times. Delivery is attempted by a background dispatcher.
/// </summary>
public class Reminder
{
    public int Id { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>When the reminder should fire, in UTC.</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>One of <see cref="ReminderStatuses"/>.</summary>
    public string Status { get; set; } = ReminderStatuses.Pending;

    /// <summary>Set once delivery succeeds (or is abandoned).</summary>
    public DateTime? DeliveredAtUtc { get; set; }

    /// <summary>Failed delivery attempts so far; used to give up after a threshold.</summary>
    public int DeliveryAttempts { get; set; }

    /// <summary>Last delivery error, for diagnostics.</summary>
    public string? LastError { get; set; }

    /// <summary>Optional link to the goal this reminder relates to.</summary>
    public int? GoalId { get; set; }
    public Goal? Goal { get; set; }

    /// <summary>Optional link to the planner item this reminder relates to.</summary>
    public int? PlannerItemId { get; set; }
    public PlannerItem? PlannerItem { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Canonical reminder statuses.</summary>
public static class ReminderStatuses
{
    /// <summary>Scheduled and not yet due/delivered.</summary>
    public const string Pending = "pending";

    /// <summary>Successfully pushed to at least one device.</summary>
    public const string Delivered = "delivered";

    /// <summary>User cancelled it before delivery.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Delivery failed too many times; no further attempts.</summary>
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [Pending, Delivered, Cancelled, Failed];
}
