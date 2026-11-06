using PersonaOS.Application.Ai.Guards;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>
/// "When is my haircut appointment?" with no haircut anywhere, answered "today at 14:00" by the
/// trained qwen2.5:3b (training run 2026-11-06b). A time nothing gave is taken out.
/// </summary>
public class UngroundedTimeGuardTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 13, 4, 0);

    private static ChatTurnState Turn(string message, params string[] results)
    {
        var turn = new ChatTurnState("m", new HashSet<string>(), ["get_planner"], userMessage: message, userNow: Now,
            userToday: DateOnly.FromDateTime(Now));
        turn.Results.AddRange(results);
        return turn;
    }

    private static async Task<ReplyDraft> Review(string reply, ChatTurnState turn, Action<TestDbContext>? seed = null)
    {
        var db = TestDbContext.Create();
        seed?.Invoke(db);
        await db.SaveChangesAsync();
        var draft = new ReplyDraft(reply, turn);
        await new UngroundedTimeGuard(db).ReviewAsync(draft, CancellationToken.None);
        return draft;
    }

    [Fact]
    public async Task An_appointment_time_nothing_gave_is_taken_out()
    {
        var draft = await Review(
            "Your last planned haircut is scheduled for today at 14:00. Please check your planner for today's events to confirm.",
            Turn("When is my haircut appointment?", """{"date":"2026-11-06","items":[]}"""));

        Assert.Equal("Please check your planner for today's events to confirm.", draft.Text);
    }

    [Fact]
    public async Task Nothing_left_says_nothing_was_found()
    {
        var draft = await Review("Your haircut is at 2pm today.", Turn("When is my haircut appointment?"));

        Assert.Equal(UngroundedTimeGuard.NothingFound, draft.Text);
    }

    [Theory]
    [InlineData("Your haircut is at 14:00 on Saturday.", """{"items":[{"title":"Haircut","scheduledTime":"14:00"}]}""")]
    [InlineData("Your haircut is at 2pm on Saturday.", """{"items":[{"title":"Haircut","scheduledTime":"14:00"}]}""")]
    [InlineData("Your haircut is at 2:00 on Saturday.", """{"items":[{"title":"Haircut","scheduledTime":"14:00:00"}]}""")]
    [InlineData("It is 13:04 now and nothing is planned.", "")]
    public async Task A_time_a_tool_gave_or_the_time_now_stays(string reply, string result)
    {
        var draft = await Review(reply, Turn("When is my haircut appointment?", result));

        Assert.Equal(reply, draft.Text);
    }

    [Fact]
    public async Task A_time_in_the_planner_stays()
    {
        var draft = await Review("Your haircut is at 4:30pm today.", Turn("When is my haircut?"),
            db => db.PlannerItems.Add(new PlannerItem { Title = "Haircut", Date = DateOnly.FromDateTime(Now), ScheduledTime = new TimeOnly(16, 30) }));

        Assert.Equal("Your haircut is at 4:30pm today.", draft.Text);
    }

    [Theory]
    [InlineData("Remind me to call Sam", "When should I remind you? Tomorrow at 9am?")]
    [InlineData("Remind me to call Sam at 5pm", "I'll remind you at 6pm.")]
    public async Task Not_an_answer_to_a_question_is_left_alone(string message, string reply)
    {
        var draft = await Review(reply, Turn(message));

        Assert.Equal(reply, draft.Text);
    }
}
