using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

public class GoalProgressCalculatorTests
{
    private static BoardTask Task(bool done = false, int? points = null) => new()
    {
        Points = points,
        Status = done ? BoardTaskStatuses.Done : BoardTaskStatuses.Todo,
    };

    [Fact]
    public void A_goal_with_nothing_under_it_uses_its_manual_progress()
    {
        Assert.Equal(40, GoalProgressCalculator.Compute(GoalStatuses.Active, 40, [], []));
    }

    [Fact]
    public void A_completed_goal_is_100_whatever_is_under_it()
    {
        Assert.Equal(100, GoalProgressCalculator.Compute(GoalStatuses.Completed, 0, [Task()], []));
    }

    [Fact]
    public void Tasks_count_equally_and_points_do_not_matter()
    {
        Assert.Equal(50, GoalProgressCalculator.Compute(GoalStatuses.Active, 0, [Task(true, 1), Task(false, 21)], []));
    }

    [Fact]
    public void Child_goals_are_averaged_and_win_over_manual_progress()
    {
        Assert.Equal(67, GoalProgressCalculator.Compute(GoalStatuses.Active, 10, [], [100, 0, 100]));
    }

    [Fact]
    public void Progress_rolls_up_from_months_through_quarters_to_the_year()
    {
        var year = new Goal { Id = 1, Status = GoalStatuses.Active };
        var quarter = new Goal { Id = 2, ParentGoalId = 1, Status = GoalStatuses.Active };
        var october = new Goal { Id = 3, ParentGoalId = 2, Status = GoalStatuses.Active, Tasks = [Task(true), Task()] };
        var november = new Goal { Id = 4, ParentGoalId = 2, Status = GoalStatuses.Active, Progress = 30 };

        var all = GoalProgressCalculator.ComputeAll([year, quarter, october, november]);

        Assert.Equal(50, all[3]);
        Assert.Equal(30, all[4]);
        Assert.Equal(40, all[2]);
        Assert.Equal(40, all[1]);
    }
}
