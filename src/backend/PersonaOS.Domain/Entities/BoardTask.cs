namespace PersonaOS.Domain.Entities;

/// <summary>
/// A piece of work on the sprint board — the story or task under a goal (the epic), or a
/// standalone task with no goal. Which board column it sits in follows from
/// <see cref="SprintId"/> and <see cref="Status"/>: no sprint means Backlog.
/// </summary>
public class BoardTask
{
    public int Id { get; set; }

    /// <summary>The n in the user-facing key <c>TASK-n</c>. The lowest free number is reused.</summary>
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The goal (epic) this task belongs to, if any.</summary>
    public int? GoalId { get; set; }
    public Goal? Goal { get; set; }

    /// <summary>Story points, one of <see cref="StoryPoints.Allowed"/>; null while unestimated.</summary>
    public int? Points { get; set; }

    /// <summary>The sprint holding this task; null while it is in the Backlog.</summary>
    public int? SprintId { get; set; }
    public Sprint? Sprint { get; set; }

    /// <summary>One of <see cref="BoardTaskStatuses"/>. Always <c>todo</c> in the Backlog.</summary>
    public string Status { get; set; } = BoardTaskStatuses.Todo;

    /// <summary>Order within its column (ascending). Ties break by number.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Added to its sprint after the sprint started. Such work is reported as added scope, not
    /// as part of what was committed at planning.
    /// </summary>
    public bool AddedMidSprint { get; set; }

    /// <summary>How many times the task was unfinished when a sprint closed and moved on.</summary>
    public int CarryOverCount { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Canonical task statuses.</summary>
public static class BoardTaskStatuses
{
    public const string Todo = "todo";
    public const string InProgress = "in_progress";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [Todo, InProgress, Done];
}

/// <summary>The Fibonacci story-point scale.</summary>
public static class StoryPoints
{
    public static readonly IReadOnlyList<int> Allowed = [1, 2, 3, 5, 8, 13, 21];

    /// <summary>From this size up, suggest splitting the task.</summary>
    public const int SplitHint = 13;
}
