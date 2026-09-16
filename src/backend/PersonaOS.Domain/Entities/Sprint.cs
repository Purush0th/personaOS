namespace PersonaOS.Domain.Entities;

/// <summary>
/// A one-week sprint, Sunday 20:00 to the next Sunday 18:00 in the user's time zone. At most one
/// sprint is <c>active</c>; the next one is <c>planned</c> so work can be lined up ahead of time.
/// The totals are frozen when the sprint starts (committed) and closes (the rest), so later edits
/// or deletions never rewrite a finished sprint's report.
/// </summary>
public class Sprint
{
    public int Id { get; set; }

    /// <summary>Sequential number behind the key SPRINT-n. Never reused.</summary>
    public int Number { get; set; }

    /// <summary>Optional name the user gave this sprint, e.g. "Paperwork week".</summary>
    public string? Name { get; set; }

    /// <summary>One of <see cref="SprintStatuses"/>.</summary>
    public string Status { get; set; } = SprintStatuses.Planned;

    /// <summary>When the sprint starts (or started, if started early from planning).</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>When the sprint closes.</summary>
    public DateTime EndsAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Set once the planning nudge for this sprint was handled (sent or skipped).</summary>
    public DateTime? PlanningNudgedAtUtc { get; set; }

    /// <summary>Points in the sprint when it started. Null until then.</summary>
    public int? CommittedPoints { get; set; }

    /// <summary>Points of tasks added after the start. Frozen at close.</summary>
    public int? AddedPoints { get; set; }

    /// <summary>Points of unfinished tasks taken out after the start. Counted as it happens.</summary>
    public int RemovedPoints { get; set; }

    /// <summary>Points of Done tasks. Frozen at close.</summary>
    public int? CompletedPoints { get; set; }

    /// <summary>Points of unfinished tasks moved to the next sprint at close.</summary>
    public int? CarriedOverPoints { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Canonical sprint statuses.</summary>
public static class SprintStatuses
{
    public const string Planned = "planned";
    public const string Active = "active";
    public const string Closed = "closed";

    public static readonly IReadOnlyList<string> All = [Planned, Active, Closed];
}
