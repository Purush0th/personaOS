namespace PersonaOS.Application.Goals;

/// <summary>A task in a proposed plan: it goes under the monthly goal that lists it, in the backlog.</summary>
public record PlanTask(string Title, int? Points = null);

/// <summary>
/// A goal with the goals and tasks nested under it, proposed and created together. A quarter and
/// its months cannot be three cards: a month's card would need the quarter's key, which does not
/// exist until the first card is confirmed. So Plan mode proposes the whole tree as one card.
/// </summary>
/// <param name="ParentId">The existing goal the top of the plan goes under, if any.</param>
public record GoalPlan(
    string Title,
    string PeriodType,
    int? Year = null,
    int? Quarter = null,
    int? Month = null,
    DateOnly? PeriodStart = null,
    int? ParentId = null,
    IReadOnlyList<GoalPlan>? Children = null,
    IReadOnlyList<PlanTask>? Tasks = null,
    string? Description = null)
{
    public IReadOnlyList<GoalPlan> ChildGoals => Children ?? [];

    public IReadOnlyList<PlanTask> PlanTasks => Tasks ?? [];

    /// <summary>Every goal in the plan, this one first.</summary>
    public IEnumerable<GoalPlan> AllGoals() => ChildGoals.SelectMany(c => c.AllGoals()).Prepend(this);

    public int GoalCount => AllGoals().Count();

    public int TaskCount => AllGoals().Sum(g => g.PlanTasks.Count);

    public CreateGoalRequest ToRequest(int? parentId) =>
        new(Title, Description, PeriodType, Year, Quarter, Month, PeriodStart, parentId);
}
