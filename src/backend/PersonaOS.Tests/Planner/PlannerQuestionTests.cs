using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.Planner;

/// <summary>Owner's chat b4c7pvc2 (2026-11-04): the questions qwen2.5:3b answered wrong, with the plan it had just read.</summary>
public class PlannerQuestionTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static PlannerOccurrenceView Item(string title, int h, int m, int endH, int endM, DateOnly? day = null)
    {
        var date = day ?? Today;
        return new PlannerOccurrenceView(new PlannerItem { Title = title, Date = date }, date, date, date, title, null,
            new TimeOnly(h, m), new TimeOnly(endH, endM), "planned", false);
    }

    private static readonly List<PlannerOccurrenceView> Plan =
    [
        Item("Learn a new skill", 7, 30, 8, 0),
        Item("Yoga / mobility", 8, 0, 8, 15),
        Item("Lunch", 12, 45, 13, 10),
        Item("Transition", 13, 10, 13, 15),
        Item("Plan tomorrow", 21, 30, 21, 40),
        Item("Meditation, lights out", 22, 0, 22, 30),
    ];

    [Theory]
    [InlineData("No, just check for my tomorrow planner and find me when I have planned the transitions event.", "Transition")]
    [InlineData("Have any meditation session tomorrow?", "Meditation, lights out")]
    // Speech-to-text heard "medication".
    [InlineData("actually there is an event plan for medication you can find that in my tomorrow planner just get me the time for the event", "Meditation, lights out")]
    [InlineData("When is lunch?", "Lunch")]
    public void A_when_question_finds_the_item_it_is_about(string message, string title)
    {
        var topic = PlannerQuestion.TopicOf(message);
        Assert.Equal([title], Plan.Where(e => PlannerQuestion.Matches(e.Title, topic)).Select(e => e.Title));
    }

    [Fact]
    public void A_yes_or_no_question_is_answered_yes_with_the_time()
    {
        var found = Plan.Where(e => e.Title.StartsWith("Meditation", StringComparison.Ordinal)).ToList();
        Assert.Equal("Yes: “Meditation, lights out” at 22:00 to 22:30 today.",
            PlannerQuestion.WhenAnswer("Have any meditation session tomorrow?", found, Today));
    }

    [Theory]
    [InlineData("Add meditation tomorrow at 7pm")]
    [InlineData("What's my next schedule")]
    [InlineData("Who is your creator?")]
    public void Other_messages_are_not_when_questions(string message)
    {
        Assert.Empty(PlannerQuestion.TopicOf(message));
    }

    [Theory]
    [InlineData("what's my next schedule")]
    [InlineData("What's next?")]
    [InlineData("what is coming up next")]
    public void Next_is_read(string message) => Assert.True(PlannerQuestion.AsksNext(message));

    [Fact]
    public void Next_is_the_first_item_after_now_not_the_evening()
    {
        Assert.Equal("Right now it's “Learn a new skill”, until 08:00. Next is “Yoga / mobility” at 08:00 to 08:15.",
            PlannerQuestion.NextAnswer(Plan, [], Today.ToDateTime(new TimeOnly(7, 40))));
    }

    [Fact]
    public void After_the_last_item_next_is_tomorrows_first()
    {
        var tomorrow = Today.AddDays(1);
        Assert.Equal("Nothing else is planned for today. Tomorrow starts with “Wake up” at 06:00 to 06:05.",
            PlannerQuestion.NextAnswer(Plan, [Item("Wake up", 6, 0, 6, 5, tomorrow)], Today.ToDateTime(new TimeOnly(23, 0))));
    }
}
