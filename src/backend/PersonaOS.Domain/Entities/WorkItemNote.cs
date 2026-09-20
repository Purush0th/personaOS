namespace PersonaOS.Domain.Entities;

/// <summary>What a comment or attachment hangs off.</summary>
public static class WorkItemTypes
{
    public const string Task = "task";
    public const string Goal = "goal";

    public static readonly IReadOnlyList<string> All = [Task, Goal];
}

/// <summary>Who wrote a comment. A single-user app still has two voices in it.</summary>
public static class CommentAuthors
{
    public const string User = "user";
    public const string Assistant = "assistant";

    public static readonly IReadOnlyList<string> All = [User, Assistant];
}

/// <summary>A note on a task or goal, in the order it was written.</summary>
public class WorkItemComment
{
    public int Id { get; set; }

    /// <summary>One of <see cref="WorkItemTypes"/>.</summary>
    public string ItemType { get; set; } = WorkItemTypes.Task;

    /// <summary>Id of the task or goal this belongs to.</summary>
    public int ItemId { get; set; }

    /// <summary>One of <see cref="CommentAuthors"/>.</summary>
    public string Author { get; set; } = CommentAuthors.User;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the body last changed.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Starts unedited: both stamps are the same instant. Two separate <c>UtcNow</c> defaults
    /// differed by a few ticks, which made every new comment show as edited.
    /// </summary>
    public WorkItemComment() => UpdatedAtUtc = CreatedAtUtc;
}

/// <summary>
/// A file attached to a task or goal. The bytes live in the same store as documents, under a
/// server-generated name, so nothing user-supplied ever reaches a filesystem path.
/// </summary>
public class WorkItemAttachment
{
    public int Id { get; set; }

    /// <summary>One of <see cref="WorkItemTypes"/>.</summary>
    public string ItemType { get; set; } = WorkItemTypes.Task;

    public int ItemId { get; set; }

    /// <summary>Display name (the user's original file name, sanitized).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Server-generated on-disk name. Never user-controlled.</summary>
    public string StorageName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>How urgent a task or goal is, highest first.</summary>
public static class WorkItemPriorities
{
    public const string Highest = "highest";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Lowest = "lowest";

    public static readonly IReadOnlyList<string> All = [Highest, High, Medium, Low, Lowest];

    /// <summary>Sort weight: 0 is the most urgent.</summary>
    public static int Rank(string priority)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i] == priority) return i;
        }
        return 2;
    }
}
