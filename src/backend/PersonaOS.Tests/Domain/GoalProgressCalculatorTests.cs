using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

public class GoalProgressCalculatorTests
{
    private static BoardTask Task(int? points, bool done = false) => new()
    {
        Points = points,
        Status = done ? BoardTaskStatuses.Done : BoardTaskStatuses.Todo,
    };

    [Fact]
    public void A_goal_without_tasks_uses_its_manual_progress()
    {
        Assert.Equal(40, GoalProgressCalculator.Compute(40, []));
    }

    [Fact]
    public void Progress_is_done_points_over_estimated_points()
    {
        // 5 of 8 points done.
        var tasks = new[] { Task(5, done: true), Task(3) };

        Assert.Equal(63, GoalProgressCalculator.Compute(0, tasks));
    }

    [Fact]
    public void Unestimated_tasks_do_not_drag_the_points_ratio_down()
    {
        var tasks = new[] { Task(3, done: true), Task(null), Task(null) };

        Assert.Equal(100, GoalProgressCalculator.Compute(0, tasks));
    }

    [Fact]
    public void With_nothing_estimated_it_counts_tasks()
    {
        var tasks = new[] { Task(null, done: true), Task(null), Task(null), Task(null) };

        Assert.Equal(25, GoalProgressCalculator.Compute(90, tasks));
    }

    [Fact]
    public void Manual_progress_is_ignored_once_tasks_exist()
    {
        Assert.Equal(0, GoalProgressCalculator.Compute(80, [Task(2)]));
    }
}
