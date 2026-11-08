using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.Planner;

/// <summary>The planner suite's day (2026-11-07), which stock and fine-tuned qwen3:4b both read wrong.</summary>
public class PlannerSlotQuestionTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);
    private static readonly DateTime Now = Today.ToDateTime(new TimeOnly(10, 0));

    private static PlannerOccurrenceView Item(string title, string start, string? end) =>
        new(new PlannerItem { Title = title, Date = Today }, Today, Today, Today, title, null,
            TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
            end is null ? null : TimeOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), "planned", false);

    private static readonly List<PlannerOccurrenceView> Day =
    [
        Item("Morning walk", "06:15", "06:45"), Item("School run", "08:30", "09:00"), Item("Deep work", "09:00", "12:00"),
        Item("Lunch with Sam", "12:00", "12:45"), Item("Client calls", "13:00", "15:00"), Item("Admin and email", "15:00", "16:30"),
        Item("Gym session", "18:30", "19:45"), Item("Evening walk with the dog", "20:00", "20:30"), Item("Bedtime", "22:30", null),
    ];

    private static string Ask(string message)
    {
        var question = PlannerSlotQuestion.Read(message, Now);
        Assert.NotNull(question);
        return PlannerSlotQuestion.Answer(question, Day, Today, Today);
    }

    [Theory]
    [InlineData("What's in my day at 7:20pm?", "At 19:20 today you're in “Gym session” (18:30 to 19:45).")]
    [InlineData("What's on at 9 in the morning?", "At 09:00 today you have “Deep work” (09:00 to 12:00). “School run” ends then.")]
    [InlineData("Read it again and tell me what is at 1pm", "At 13:00 today you have “Client calls” (13:00 to 15:00).")]
    [InlineData("What's at half past seven in the evening?", "At 19:30 today you're in “Gym session” (18:30 to 19:45).")]
    [InlineData("10 at night today, what's that slot for?", "Nothing is planned at 22:00 today. Next is “Bedtime” at 22:30.")]
    [InlineData("What am I doing at 8:15?", "Nothing is planned at 08:15 today. Next is “School run” at 08:30.")]
    public void A_time_gets_what_is_on_then(string message, string answer)
    {
        Assert.Equal(answer, Ask(message));
    }

    [Theory]
    [InlineData("What happens from 3 to 5pm today?", "Between 15:00 and 17:00 today you have “Admin and email” (15:00 to 16:30).")]
    [InlineData("What's on between noon and 2pm?", "Between 12:00 and 14:00 today you have “Lunch with Sam” (12:00 to 12:45) and “Client calls” (13:00 to 15:00).")]
    [InlineData("What fills 6:30 to 8 today?", "Between 06:30 and 08:00 today you have “Morning walk” (06:15 to 06:45).")]
    public void A_stretch_gets_what_overlaps_it(string message, string answer)
    {
        Assert.Equal(answer, Ask(message));
    }

    [Theory]
    [InlineData("How many walks are on today's plan?", "You have 2 walks today: “Morning walk” at 06:15 and “Evening walk with the dog” at 20:00.")]
    [InlineData("Count my lunches for today", "You have 1 lunch today: “Lunch with Sam” at 12:00.")]
    [InlineData("How many meetings today?", "You have no meetings in your planner today.")]
    public void A_count_lists_what_it_counted(string message, string answer)
    {
        Assert.Equal(answer, Ask(message));
    }

    [Theory]
    [InlineData("Add a gym session at 7pm")]
    [InlineData("Remind me at 5pm to call mum")]
    [InlineData("What's on my schedule today?")]
    [InlineData("Be critical of my schedule today")]
    [InlineData("How many points is TASK-7?")]
    [InlineData("When is my gym session?")]
    [InlineData("In 30 minutes, nudge me to move the laundry")]
    // holdout3 (2026-11-07): "20" was read as 20:00.
    [InlineData("Can you get a haircut into my calendar on Monday at 8pm?")]
    [InlineData("Block Thursday at 11am for the parents' evening?")]
    [InlineData("How much is 20% of 250?")]
    [InlineData("What is 17 times 6?")]
    [InlineData("How many minutes are in a day?")]
    [InlineData("What are 3 good habits?")]
    public void Other_messages_are_left_to_the_model(string message)
    {
        Assert.Null(PlannerSlotQuestion.Read(message, Now));
    }
}
