using System.Globalization;
using System.Text.RegularExpressions;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Board;

/// <summary>
/// "Is TASK-17 in the running sprint or in the backlog?", "Where is TASK-4?", "Is TASK-9 in the
/// sprint?". One right answer, in the data: the trained qwen2.5:3b said TASK-17 was in the backlog
/// and then that it "was added mid-sprint to TASK-16, which is in the running sprint" (training run
/// 2026-11-06b, bench-5). The answer is worked out here and replaces the model's.
/// </summary>
public static partial class TaskWhereQuestion
{
    [GeneratedRegex(
        @"^\s*(?:is|was)\s+(?<key>TASK-\d+)\s+(?:still\s+)?in\s+(?:the|a|my)\s+(?:(?:running|current|active)\s+)?(?:sprint|backlog)\b|^\s*where(?:'s|\s+is)\s+(?<key>TASK-\d+)\s*\??\s*$|^\s*which\s+sprint\s+is\s+(?<key>TASK-\d+)\s+in\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksWhere();

    /// <summary>The task number the message asks the place of, or null when it is not such a question.</summary>
    public static int? Parse(string? message) =>
        string.IsNullOrWhiteSpace(message) || AsksWhere().Match(message) is not { Success: true } m
            ? null
            : int.Parse(m.Groups["key"].Value[5..], CultureInfo.InvariantCulture);

    /// <summary>Where the task is, in one sentence; null for a task that does not exist.</summary>
    public static string? Answer(int number, BoardTask? task)
    {
        if (task is null) return null;
        var name = $"TASK-{number} “{task.Title}”";
        var column = task.Status switch
        {
            BoardTaskStatuses.Done => "done",
            BoardTaskStatuses.InProgress => "in progress",
            _ => "to do",
        };
        if (task.Sprint is not { } sprint) return $"{name} is in the backlog, not in a sprint.";

        var sprintName = $"SPRINT-{sprint.Number}" + (string.IsNullOrWhiteSpace(sprint.Name) ? string.Empty : $" “{sprint.Name}”");
        return sprint.Status switch
        {
            SprintStatuses.Active => $"{name} is in the running sprint, {sprintName}, and is {column}.",
            SprintStatuses.Planned => $"{name} is in the planned sprint {sprintName}, not the running one.",
            _ => $"{name} was in {sprintName}, which is closed, and is {column}.",
        };
    }
}
