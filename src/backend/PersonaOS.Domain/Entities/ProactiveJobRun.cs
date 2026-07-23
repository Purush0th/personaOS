namespace PersonaOS.Domain.Entities;

/// <summary>
/// Record that a proactive job ran for a given local date. Its existence is what
/// makes the scheduler idempotent: a restart, a clock tick, or a second instance
/// cannot send the same brief twice.
/// </summary>
public class ProactiveJobRun
{
    public int Id { get; set; }

    /// <summary>One of <see cref="ProactiveJobs"/>.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>The user's local date this run belongs to.</summary>
    public DateOnly LocalDate { get; set; }

    public DateTime RanAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Whether a push notification actually reached a device.</summary>
    public bool Pushed { get; set; }

    /// <summary>The message that was composed, kept for auditing and debugging.</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Canonical proactive job names.</summary>
public static class ProactiveJobs
{
    /// <summary>Start-of-day brief: today's plan, reminders, and goal nudges.</summary>
    public const string MorningBrief = "morning_brief";

    /// <summary>End-of-day rollup: what got done, what is still open.</summary>
    public const string EveningRollup = "evening_rollup";

    public static readonly IReadOnlyList<string> All = [MorningBrief, EveningRollup];
}
