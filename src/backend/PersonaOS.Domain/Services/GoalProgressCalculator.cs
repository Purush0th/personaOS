using PersonaOS.Domain.Entities;

namespace PersonaOS.Domain.Services;

/// <summary>
/// A goal's effective progress (0–100), derived from its tasks:
/// <list type="bullet">
/// <item>done points ÷ estimated points, when any of its tasks is estimated;</item>
/// <item>otherwise done tasks ÷ tasks;</item>
/// <item>with no tasks at all, the goal's own manually tracked <see cref="Goal.Progress"/>.</item>
/// </list>
/// Unestimated tasks are left out of the points ratio rather than counted as zero, so adding a
/// task you have not sized yet does not make the goal look further behind than it is.
/// </summary>
public static class GoalProgressCalculator
{
    public static int Compute(int manualProgress, IReadOnlyCollection<BoardTask> tasks)
    {
        if (tasks.Count == 0) return Math.Clamp(manualProgress, 0, 100);

        var estimated = tasks.Where(t => t.Points is > 0).ToList();
        double ratio = estimated.Count > 0
            ? (double)estimated.Where(IsDone).Sum(t => t.Points!.Value) / estimated.Sum(t => t.Points!.Value)
            : (double)tasks.Count(IsDone) / tasks.Count;

        return Math.Clamp((int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero), 0, 100);
    }

    private static bool IsDone(BoardTask task) => task.Status == BoardTaskStatuses.Done;
}
