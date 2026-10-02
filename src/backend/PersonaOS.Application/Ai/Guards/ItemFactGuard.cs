using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Board;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Checks what a reply says about a task's value points and status, and about a sprint's
/// estimates, against the data. Seen live on qwen2.5:3b (2026-09-27): it read "unestimatedCount": 0
/// in a sprint report as one unestimated task, named TASK-1 as that task although the prompt
/// listed "TASK-1 … [in_progress, 3 pts]", and then defended the claim three times from its own
/// earlier reply. The prompt cannot make a small model read numbers right; a check against the
/// database can catch the result.
///
/// What a sentence is about:
/// <list type="bullet">
/// <item>the one task it names by key (TASK-3), or else by title, when exactly one running-sprint
/// task's title is in it ("Task 3, “Read chapter 4”, is not estimated");</item>
/// <item>else the one sprint it names (SPRINT-1), or else, when it talks about tasks and not about
/// the backlog or a later sprint, the running sprint ("since all tasks are unestimated").</item>
/// </list>
/// A sentence naming two tasks or two sprints is not checked, so a fact is never pinned on the
/// wrong item, and status is checked only in sentences without a negation ("is not done yet" is
/// not a claim that it is done). For each thing a reply gets wrong, the true fact is recorded, and
/// so is the sentence: the chat loop asks the model once to correct itself and, if it does not,
/// takes the wrong sentences out and says the facts.
/// </summary>
public sealed partial class ItemFactGuard(IAppDbContext db) : IReplyGuard
{
    [GeneratedRegex(@"\bTASK-(\d{1,6})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TaskKey();

    [GeneratedRegex(@"\bSPRINT-(\d{1,6})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SprintKey();

    /// <summary>"unestimated", "not estimated", "no value points", "does not have a points value", "without points", "Value: None".</summary>
    [GeneratedRegex(
        @"\b(?:value|points?|estimate)\W{0,3}\s*[:=]\s*\W{0,3}(?:none|null|n/a)\b|\b(?:unestimated|not\s+(?:yet\s+)?(?:been\s+)?estimated|no\s+(?:value\s+)?points?\b|without\s+(?:any\s+)?(?:value\s+)?points?\b|(?:does\s*n[o']t|do\s*n[o']t|did\s*n[o']t|ha(?:s|ve)\s+no)\s+(?:have\s+)?(?:a\s+|any\s+)?(?:value\s+|story\s+)?points?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnestimatedClaim();

    /// <summary>"not unestimated", meant as estimated.</summary>
    [GeneratedRegex(@"\bnot\s+unestimated\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoubleNegative();

    /// <summary>"3 points", "3 value points", "3 pts", "a value point of 3", "points value of 3".</summary>
    [GeneratedRegex(
        @"\b(\d{1,3})\s*(?:value\s+|story\s+)?(?:points?|pts)\b|\b(?:value\s+)?points?\s+(?:value\s+)?of\s+(\d{1,3})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PointsClaim();

    [GeneratedRegex(@"\b(?:not|n't|no|never|yet)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();

    [GeneratedRegex(@"\b(?:is|was|has\s+been|are)\s+(?:now\s+|already\s+)?(?:done|completed|complete|finished)\b|\bmarked\s+(?:as\s+)?(?:done|complete|completed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoneClaim();

    [GeneratedRegex(@"\b(?:is|are)\s+(?:currently\s+|now\s+|still\s+)?in\s+progress\b", RegexOptions.IgnoreCase)]
    private static partial Regex InProgressClaim();

    [GeneratedRegex(@"\b(?:is|are)\s+(?:currently\s+|now\s+|still\s+)?in\s+(?:the\s+)?backlog\b", RegexOptions.IgnoreCase)]
    private static partial Regex BacklogClaim();

    /// <summary>"is in the running sprint", "is part of the current sprint": said of a backlog task, wrong.</summary>
    [GeneratedRegex(@"\b(?:is|are)\s+(?:currently\s+|now\s+|still\s+|already\s+)?(?:in|part\s+of)\s+(?:the\s+|your\s+|this\s+)?(?:running\s+|current\s+|active\s+)?sprint\b", RegexOptions.IgnoreCase)]
    private static partial Regex InSprintClaim();

    /// <summary>"no tasks are in the sprint", "the sprint is empty", "not pulled into the sprint".</summary>
    [GeneratedRegex(@"\b(?:no|zero|none\s+of\s+the|none\s+of\s+your)\s+tasks?\s+(?:are\s+|is\s+|have\s+been\s+)?(?:in|pulled\s+into|added\s+to)\s+(?:the\s+|your\s+)?(?:running\s+|current\s+|active\s+)?sprint\b|\bsprint\s+(?:is\s+(?:currently\s+)?empty|has\s+no\s+tasks)\b|\bnot\s+(?:yet\s+)?(?:been\s+)?(?:pulled|moved|added)\s+(?:in)?to\s+(?:the\s+|your\s+)?(?:running\s+|current\s+|active\s+)?sprint\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmptySprintClaim();

    /// <summary>"the tasks are still in the backlog": tasks, unnamed, said to be in the backlog.</summary>
    [GeneratedRegex(@"\b(?:the|these|those|all|your|all\s+the|all\s+your)\s+(?:\w+\s+){0,2}tasks\s+(?:are\s+)?(?:still\s+|currently\s+|all\s+)?in\s+(?:the\s+|your\s+)?backlog\b", RegexOptions.IgnoreCase)]
    private static partial Regex TasksInBacklogClaim();

    /// <summary>"none of the tasks", "no tasks", "zero": no task is meant. "no value points" is not one.</summary>
    [GeneratedRegex(@"\b(?:none|zero)\b|\bno\b(?!\s+(?:value\s+|story\s+)?points?)", RegexOptions.IgnoreCase)]
    private static partial Regex NoneOf();

    /// <summary>"not all", "not every": at least one is meant.</summary>
    [GeneratedRegex(@"\bnot\s+(?:all|every|each)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAll();

    [GeneratedRegex(@"\b(?:all|every|each)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AllOf();

    /// <summary>"estimated", "sized", "has value points", "has a point value".</summary>
    [GeneratedRegex(@"\b(?:estimated|sized|ha(?:ve|s)\s+(?:been\s+)?(?:given\s+|assigned\s+)?(?:a\s+|their\s+|its\s+)?(?:value\s+|story\s+)?points?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Estimated();

    /// <summary>Where one clause ends and the next starts: ", ", "; ", "(", "and", "but", "since".</summary>
    [GeneratedRegex(@"[,;:()]|\b(?:and|but|since|because|while|so|though|although)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseBreak();

    /// <summary>"can", "should", "not allowed", "let's": about what could be, not what is.</summary>
    [GeneratedRegex(@"\b(?:allowed|permitted|can|could|should|would|will|must|might|may|needs?|let'?s|let\s+us|suggest|want|like)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Modal();

    /// <summary>"all 3 tasks have been completed", "every task is done".</summary>
    [GeneratedRegex(@"\b(?:all|every|each)\b[^.]*?\b(?:are|were|is|was|have\s+been|has\s+been)\s+(?:now\s+|already\s+)?(?:done|completed|complete|finished)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AllDone();

    /// <summary>"none have been completed", "no tasks are done".</summary>
    [GeneratedRegex(@"\b(?:none|zero|no\s+tasks?)\b[^.]*?\b(?:done|completed|complete|finished)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoneDone();

    /// <summary>"once all tasks are done", "if none is finished": not a claim about now.</summary>
    [GeneratedRegex(@"\b(?:once|when|if|until|after|before|unless)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Conditional();

    [GeneratedRegex(@"\btasks?\b", RegexOptions.IgnoreCase)]
    private static partial Regex MentionsTasks();

    /// <summary>A sentence about the backlog or a later sprint is not about the running one.</summary>
    [GeneratedRegex(@"\b(?:backlog|planned|next\s+sprint|upcoming|later\s+sprint)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherThanRunning();

    public string Name => "item-fact";

    public async ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var sentences = ReplySentences.Split(draft.Text).ToList();
        if (sentences.Count == 0) return null;

        // The running sprint and its tasks, for sentences that name neither by key.
        var running = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        var runningTasks = running is null ? []
            : await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == running.Id).ToListAsync(ct);

        // How many tasks wait in the backlog, for sentences that say where unnamed tasks are.
        var backlogCount = (await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == null).ToListAsync(ct))
            .Count(t => BoardColumns.Of(t) == BoardColumns.Backlog);

        var wrongFacts = new List<string>();
        var wrongSentences = new List<string>();
        foreach (var sentence in sentences)
        {
            var fact = await CheckAsync(sentence, running, runningTasks, ct)
                ?? CheckWhereTasksAre(sentence, running, runningTasks.Count, backlogCount);
            if (fact is null) continue;
            if (!wrongFacts.Contains(fact)) wrongFacts.Add(fact);
            wrongSentences.Add(sentence);
        }

        draft.WrongFacts = wrongFacts;
        draft.WrongSentences = wrongSentences;
        return wrongFacts.Count == 0 ? null : $"got item facts wrong; true: {string.Join("; ", wrongFacts)}";
    }

    /// <summary>The true fact a sentence contradicts, or null when it is right or says nothing checkable.</summary>
    private async Task<string?> CheckAsync(string sentence, Sprint? running, IReadOnlyList<BoardTask> runningTasks, CancellationToken ct)
    {
        var taskKeys = TaskKey().Matches(sentence).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToList();
        var sprintKeys = SprintKey().Matches(sentence).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToList();

        BoardTask? task = null;
        if (taskKeys.Count == 1)
        {
            task = await db.BoardTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Number == taskKeys[0], ct);
        }
        else if (taskKeys.Count == 0 && sprintKeys.Count == 0)
        {
            var titled = runningTasks.Where(t => t.Title.Length >= 4 && sentence.Contains(t.Title, StringComparison.OrdinalIgnoreCase)).ToList();
            if (titled.Count == 1) task = titled[0];
        }
        if (task is not null)
        {
            var column = BoardColumns.Of(task);
            return Contradicts(sentence, task.Points, column) ? Fact(ItemKeys.Task(task.Number), task.Title, task.Points, column) : null;
        }
        if (taskKeys.Count > 0) return null;

        Sprint? sprint = null;
        if (sprintKeys.Count == 1)
        {
            sprint = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Number == sprintKeys[0], ct);
        }
        else if (sprintKeys.Count == 0 && running is not null && MentionsTasks().IsMatch(sentence) && !OtherThanRunning().IsMatch(sentence))
        {
            sprint = running;
        }
        if (sprint is null) return null;

        var tasks = sprint.Id == running?.Id ? runningTasks
            : await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == sprint.Id).ToListAsync(ct);
        var unestimated = tasks.Count(t => t.Points is null);
        var done = tasks.Count(t => BoardColumns.Of(t) == BoardColumns.Done);
        return SprintContradicts(sentence, tasks.Count, unestimated, done)
            ? SprintFact(ItemKeys.Sprint(sprint.Number), tasks.Count, unestimated, done)
            : null;
    }

    /// <summary>
    /// For a sentence naming no task and no sprint: the true fact when it says the running sprint
    /// has no tasks while it has some ("the tasks are in the backlog, not pulled into the
    /// sprint"), or that the tasks are in the backlog while it is empty.
    /// </summary>
    private static string? CheckWhereTasksAre(string sentence, Sprint? running, int runningCount, int backlogCount)
    {
        if (TaskKey().IsMatch(sentence) || SprintKey().IsMatch(sentence)) return null;
        return LocationContradicts(sentence, running is not null, runningCount, backlogCount)
            ? LocationFact(running is null ? null : ItemKeys.Sprint(running.Number), runningCount, backlogCount)
            : null;
    }

    /// <summary>Whether a sentence gets wrong where unnamed tasks are: in the running sprint or the backlog.</summary>
    public static bool LocationContradicts(string sentence, bool sprintRunning, int runningCount, int backlogCount)
    {
        foreach (var clause in ClauseBreak().Split(sentence))
        {
            if (clause.Trim().Length == 0 || Conditional().IsMatch(clause) || Modal().IsMatch(clause)) continue;
            if (sprintRunning && runningCount > 0 && EmptySprintClaim().IsMatch(clause)) return true;
            if (backlogCount == 0 && TasksInBacklogClaim().IsMatch(clause) && !Negation().IsMatch(clause)) return true;
        }
        return false;
    }

    /// <summary>"SPRINT-2 is running with 3 tasks, and the backlog has 0."</summary>
    public static string LocationFact(string? runningKey, int runningCount, int backlogCount) =>
        (runningKey is null
            ? "No sprint is running"
            : $"{runningKey} is running with {runningCount} {(runningCount == 1 ? "task" : "tasks")}")
        + $", and the backlog has {backlogCount}.";

    /// <summary>Whether the sentence says something about the task's points or column that is not so.</summary>
    public static bool Contradicts(string sentence, int? points, string column)
    {
        var text = DoubleNegative().Replace(sentence, "estimated");
        if (UnestimatedClaim().IsMatch(text) && points is not null) return true;

        foreach (Match m in PointsClaim().Matches(text))
        {
            var said = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (said != points) return true;
        }

        if (Negation().IsMatch(text)) return false;
        return (DoneClaim().IsMatch(text) && column != BoardColumns.Done)
            || (InProgressClaim().IsMatch(text) && column != BoardColumns.InProgress)
            || (BacklogClaim().IsMatch(text) && column != BoardColumns.Backlog)
            || (InSprintClaim().IsMatch(text) && column == BoardColumns.Backlog);
    }

    /// <summary>
    /// Whether a sentence about a sprint gets its tasks wrong, read clause by clause. Estimates:
    /// "none of the tasks are unestimated" and "all tasks are estimated" (none unestimated), "all
    /// tasks are unestimated" and "none are estimated" (every one), "has tasks that are not yet
    /// estimated" and "not all are estimated" (at least one). Done: "all 3 tasks have been
    /// completed" and "none are done yet". A clause about what could, should or will be ("once all
    /// tasks are done", "unestimated tasks are not allowed") claims nothing about now.
    /// </summary>
    public static bool SprintContradicts(string sentence, int tasks, int unestimated, int done)
    {
        foreach (var clause in ClauseBreak().Split(DoubleNegative().Replace(sentence, "estimated")))
        {
            if (clause.Trim().Length == 0 || Conditional().IsMatch(clause) || Modal().IsMatch(clause)) continue;

            if (NoneDone().IsMatch(clause) && done > 0) return true;
            if (AllDone().IsMatch(clause) && !NotAll().IsMatch(clause) && done != tasks) return true;

            // What the clause says the number of unestimated tasks is, if anything.
            bool? wrong = null;
            if (UnestimatedClaim().IsMatch(clause))
            {
                wrong = NoneOf().IsMatch(clause) ? unestimated > 0
                    : AllOf().IsMatch(clause) && !NotAll().IsMatch(clause) ? unestimated != tasks
                    : unestimated == 0;
            }
            else if (Estimated().IsMatch(clause))
            {
                wrong = NotAll().IsMatch(clause) ? unestimated == 0
                    : NoneOf().IsMatch(clause) ? unestimated != tasks
                    : AllOf().IsMatch(clause) ? unestimated > 0
                    : null;
            }
            if (wrong == true) return true;
        }
        return false;
    }

    /// <summary>"TASK-1 “5 hours strength training” has 3 value points and is In progress."</summary>
    public static string Fact(string key, string title, int? points, string column)
    {
        var size = points is int p ? $"has {p} value {(p == 1 ? "point" : "points")}" : "has no value points yet (unestimated)";
        var where = column switch
        {
            BoardColumns.Backlog => "is in the backlog",
            BoardColumns.Todo => "is To do",
            BoardColumns.InProgress => "is In progress",
            _ => "is Done",
        };
        return $"{key} “{title}” {size} and {where}.";
    }

    /// <summary>"SPRINT-1 has 3 tasks, 0 of them done, and every one has value points (none is unestimated)."</summary>
    public static string SprintFact(string key, int tasks, int unestimated, int done) =>
        $"{key} has {tasks} {(tasks == 1 ? "task" : "tasks")}, {done} of {(tasks == 1 ? "it" : "them")} done, and "
        + (unestimated == 0
            ? "every one has value points (none is unestimated)."
            : $"{unestimated} {(unestimated == 1 ? "has" : "have")} no value points yet.");
}
