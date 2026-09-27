using PersonaOS.Domain.Entities;

namespace PersonaOS.Domain.Services;

/// <summary>
/// A goal's effective progress (0–100), by the owner's rules of 2026-09-27:
/// <list type="bullet">
/// <item>a completed goal is 100%;</item>
/// <item>a goal with child goals is the average of their progress, each child counting equally
/// whether or not it has tasks (a month with no tasks still counts, at its own value);</item>
/// <item>a goal with tasks is done tasks ÷ tasks, each task counting equally — value points
/// measure sprint workload and never count here;</item>
/// <item>otherwise the goal's own manually set <see cref="Goal.Progress"/>.</item>
/// </list>
/// Elapsed time never counts either.
/// </summary>
public static class GoalProgressCalculator
{
    public static int Compute(string status, int manualProgress, IReadOnlyCollection<BoardTask> tasks, IReadOnlyCollection<int> childProgress)
    {
        if (status == GoalStatuses.Completed) return 100;
        if (childProgress.Count > 0) return Round(childProgress.Average() / 100);
        if (tasks.Count > 0) return Round((double)tasks.Count(t => t.Status == BoardTaskStatuses.Done) / tasks.Count);
        return Math.Clamp(manualProgress, 0, 100);
    }

    /// <summary>
    /// Every goal's progress, keyed by id. Each goal needs its <see cref="Goal.Tasks"/> loaded;
    /// children are found through <see cref="Goal.ParentGoalId"/>, so the list must hold every
    /// descendant of any goal whose progress is wanted.
    /// </summary>
    public static IReadOnlyDictionary<int, int> ComputeAll(IReadOnlyCollection<Goal> goals)
    {
        var children = goals.Where(g => g.ParentGoalId is not null).ToLookup(g => g.ParentGoalId!.Value);
        var result = new Dictionary<int, int>();

        int Of(Goal goal)
        {
            if (result.TryGetValue(goal.Id, out var known)) return known;
            var kids = children[goal.Id].Select(Of).ToList();
            return result[goal.Id] = Compute(goal.Status, goal.Progress, goal.Tasks.ToList(), kids);
        }

        foreach (var goal in goals) Of(goal);
        return result;
    }

    private static int Round(double ratio) =>
        Math.Clamp((int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero), 0, 100);
}
