namespace PersonaOS.Domain.Entities;

/// <summary>
/// A yearly / quarterly / monthly goal. Goals nest in one fixed shape, year → quarter → month,
/// or stand alone; tasks (<see cref="BoardTask"/>) sit only under monthly goals. Dates follow the
/// calendar (see GoalCalendar). The effective progress is derived (see GoalProgressCalculator);
/// <see cref="Progress"/> is the manually tracked value, used only while the goal has neither
/// tasks nor child goals.
/// </summary>
public class Goal
{
    public int Id { get; set; }

    /// <summary>The n in the user-facing key <c>GOAL-n</c>. The lowest free number is reused.</summary>
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>One of <see cref="GoalPeriods"/>.</summary>
    public string PeriodType { get; set; } = GoalPeriods.Year;

    /// <summary>
    /// The day the goal starts: the first day of its calendar slot, or the day it was created
    /// when that was later (a goal never starts in the past).
    /// </summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>The day the goal ends, inclusive: the last day of its year, quarter or month.</summary>
    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// The goal this one sits under: a year for a quarter, a quarter for a month. Null for a
    /// standalone goal (and always for a year).
    /// </summary>
    public int? ParentGoalId { get; set; }
    public Goal? ParentGoal { get; set; }
    public ICollection<Goal> ChildGoals { get; set; } = new List<Goal>();

    /// <summary>One of <see cref="GoalStatuses"/>.</summary>
    public string Status { get; set; } = GoalStatuses.Active;

    /// <summary>0–100. Manually tracked; used only while the goal has no tasks and no child goals.</summary>
    public int Progress { get; set; }

    /// <summary>One of <see cref="WorkItemPriorities"/>.</summary>
    public string Priority { get; set; } = WorkItemPriorities.Medium;

    public ICollection<BoardTask> Tasks { get; set; } = new List<BoardTask>();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Canonical goal period types.</summary>
public static class GoalPeriods
{
    public const string Year = "year";
    public const string Quarter = "quarter";
    public const string Month = "month";

    public static readonly IReadOnlyList<string> All = [Year, Quarter, Month];

    /// <summary>"Monthly", "Quarterly", "Yearly": the type as the user reads it (owner, 2026-09-27).</summary>
    public static string Label(string type) => type switch
    {
        Year => "Yearly",
        Quarter => "Quarterly",
        Month => "Monthly",
        _ => type,
    };
}

/// <summary>
/// Canonical goal statuses. There is no "dropped": a goal that no longer matters is deleted
/// (owner's call, 2026-09-27).
/// </summary>
public static class GoalStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> All = [Active, Completed];
}
