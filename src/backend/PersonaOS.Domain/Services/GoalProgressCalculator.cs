using PersonaOS.Domain.Entities;

namespace PersonaOS.Domain.Services;

/// <summary>
/// Pure progress-rollup rules: a goal with children derives its effective progress
/// from them (rounded average); a leaf goal's effective progress is its own
/// <see cref="Goal.Progress"/>. Dropped goals are excluded from a parent's average;
/// if every child is dropped the parent falls back to its own stored progress.
/// </summary>
public static class GoalProgressCalculator
{
    /// <summary>Computes the effective progress (0–100) for every goal in the set.</summary>
    public static IReadOnlyDictionary<int, int> ComputeEffectiveProgress(IReadOnlyCollection<Goal> goals)
    {
        var childrenByParent = goals
            .Where(g => g.ParentGoalId is not null)
            .GroupBy(g => g.ParentGoalId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Goal>)g.ToList());

        var result = new Dictionary<int, int>(goals.Count);

        int Effective(Goal goal)
        {
            if (result.TryGetValue(goal.Id, out var cached)) return cached;
            // Mark before recursing so a (never-expected) cycle cannot overflow the stack.
            result[goal.Id] = goal.Progress;

            var contributing = childrenByParent.TryGetValue(goal.Id, out var children)
                ? children.Where(c => c.Status != GoalStatuses.Dropped).ToList()
                : [];

            var value = contributing.Count == 0
                ? goal.Progress
                : (int)Math.Round(contributing.Average(c => (double)Effective(c)), MidpointRounding.AwayFromZero);

            result[goal.Id] = Math.Clamp(value, 0, 100);
            return result[goal.Id];
        }

        foreach (var goal in goals) Effective(goal);
        return result;
    }
}
