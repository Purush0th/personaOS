using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

public class GoalProgressCalculatorTests
{
    private static Goal Goal(int id, int progress, int? parentId = null, string status = GoalStatuses.Active) =>
        new() { Id = id, Progress = progress, ParentGoalId = parentId, Status = status };

    [Fact]
    public void Leaf_goal_uses_its_own_progress()
    {
        var result = GoalProgressCalculator.ComputeEffectiveProgress([Goal(1, 40)]);

        Assert.Equal(40, result[1]);
    }

    [Fact]
    public void Parent_averages_its_children()
    {
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 80, parentId: 1),
            Goal(3, 20, parentId: 1),
        ]);

        Assert.Equal(50, result[1]);
    }

    [Fact]
    public void Dropped_children_are_excluded_from_the_average()
    {
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 80, parentId: 1),
            Goal(3, 20, parentId: 1, status: GoalStatuses.Dropped),
        ]);

        Assert.Equal(80, result[1]);
    }

    [Fact]
    public void Parent_whose_children_are_all_dropped_falls_back_to_own_progress()
    {
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 35),
            Goal(2, 80, parentId: 1, status: GoalStatuses.Dropped),
        ]);

        Assert.Equal(35, result[1]);
    }

    [Fact]
    public void Rollup_is_recursive_through_the_hierarchy()
    {
        // Year(1) -> Quarter(2) [Months 100 and 0 => 50], Quarter(3) [100]  => avg 75
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 0, parentId: 1),
            Goal(3, 100, parentId: 1),
            Goal(4, 100, parentId: 2),
            Goal(5, 0, parentId: 2),
        ]);

        Assert.Equal(50, result[2]);
        Assert.Equal(75, result[1]);
    }

    [Fact]
    public void Average_is_rounded_to_a_whole_percent()
    {
        // (10 + 10 + 11) / 3 = 10.33 -> 10
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 10, parentId: 1),
            Goal(3, 10, parentId: 1),
            Goal(4, 11, parentId: 1),
        ]);

        Assert.Equal(10, result[1]);
    }

    [Fact]
    public void Midpoint_rounds_away_from_zero()
    {
        // (10 + 11) / 2 = 10.5 -> 11
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 10, parentId: 1),
            Goal(3, 11, parentId: 1),
        ]);

        Assert.Equal(11, result[1]);
    }

    [Fact]
    public void Completed_children_count_toward_the_average()
    {
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 100, parentId: 1, status: GoalStatuses.Completed),
            Goal(3, 0, parentId: 1),
        ]);

        Assert.Equal(50, result[1]);
    }

    [Fact]
    public void Every_goal_in_the_set_gets_a_value()
    {
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 0),
            Goal(2, 60, parentId: 1),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(60, result[2]);
    }

    [Fact]
    public void A_parent_cycle_terminates_instead_of_overflowing()
    {
        // Defensive: never expected via the API (LinkAsync rejects cycles), but the
        // calculator must not stack-overflow if bad data ever reaches it.
        var result = GoalProgressCalculator.ComputeEffectiveProgress([
            Goal(1, 30, parentId: 2),
            Goal(2, 70, parentId: 1),
        ]);

        Assert.Equal(2, result.Count);
    }
}
