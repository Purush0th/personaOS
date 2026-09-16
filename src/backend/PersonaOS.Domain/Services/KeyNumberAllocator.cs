namespace PersonaOS.Domain.Services;

/// <summary>
/// Picks the number for a new <c>GOAL-n</c> or <c>TASK-n</c> key: the lowest positive number not
/// in use. Deleting <c>TASK-3</c> therefore frees "3" for the next task, which the owner asked for
/// (unlike Jira, where numbers are never reused).
/// </summary>
public static class KeyNumberAllocator
{
    public static int LowestFree(IEnumerable<int> used)
    {
        var taken = used.Where(n => n > 0).ToHashSet();
        var candidate = 1;
        while (taken.Contains(candidate)) candidate++;
        return candidate;
    }
}

/// <summary>Formats and parses the user-facing keys.</summary>
public static class ItemKeys
{
    public const string GoalPrefix = "GOAL";
    public const string TaskPrefix = "TASK";
    public const string SprintPrefix = "SPRINT";

    public static string Goal(int number) => $"{GoalPrefix}-{number}";
    public static string Task(int number) => $"{TaskPrefix}-{number}";
    public static string Sprint(int number) => $"{SprintPrefix}-{number}";

    /// <summary>
    /// Reads "TASK-12", "task 12", "#12" or "12" as 12 for the given prefix. A key with the other
    /// prefix ("GOAL-12" when a task was expected) is rejected rather than silently accepted.
    /// </summary>
    public static int? Parse(string? raw, string prefix)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().TrimStart('#');

        var letters = new string(text.TakeWhile(char.IsLetter).ToArray());
        if (letters.Length > 0)
        {
            if (!letters.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            text = text[letters.Length..].TrimStart('-', ' ', '_');
        }

        return int.TryParse(text, out var number) && number > 0 ? number : null;
    }
}
