namespace PersonaOS.Domain.Entities;

/// <summary>
/// A yearly / quarterly / monthly goal — the epic of the sprint board. Goals do not nest: work
/// under a goal is broken into <see cref="BoardTask"/>s, and the goal's effective progress is
/// derived from them (see GoalProgressCalculator). <see cref="Progress"/> is the manually
/// tracked value, used only while the goal has no tasks.
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

    /// <summary>First day of the period this goal belongs to (e.g. 2026-07-01 for July 2026).</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>One of <see cref="GoalStatuses"/>.</summary>
    public string Status { get; set; } = GoalStatuses.Active;

    /// <summary>0–100. Manually tracked; used only while the goal has no tasks.</summary>
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
}

/// <summary>Canonical goal statuses.</summary>
public static class GoalStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Dropped = "dropped";

    public static readonly IReadOnlyList<string> All = [Active, Completed, Dropped];
}
