namespace PersonaOS.Domain.Entities;

/// <summary>
/// A yearly / quarterly / monthly goal. Goals form a hierarchy via
/// <see cref="ParentGoalId"/> (e.g. a monthly goal contributing to a quarterly one).
/// <see cref="Progress"/> is the manually-tracked value for leaf goals; parents
/// derive their effective progress from children (see GoalProgressCalculator).
/// </summary>
public class Goal
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? ParentGoalId { get; set; }
    public Goal? Parent { get; set; }
    public ICollection<Goal> Children { get; set; } = new List<Goal>();

    /// <summary>One of <see cref="GoalPeriods"/>.</summary>
    public string PeriodType { get; set; } = GoalPeriods.Year;

    /// <summary>First day of the period this goal belongs to (e.g. 2026-07-01 for July 2026).</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>One of <see cref="GoalStatuses"/>.</summary>
    public string Status { get; set; } = GoalStatuses.Active;

    /// <summary>0–100. Manually tracked; authoritative for leaf goals only.</summary>
    public int Progress { get; set; }

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
