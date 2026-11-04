using PersonaOS.Application.Ai.Guards;

namespace PersonaOS.Tests.Ai.Guards;

/// <summary>Owner's chat b4c7pvc2 (2026-11-04): the whole day pasted under "who is the developer?".</summary>
public class UnaskedListGuardTests
{
    private const string Day = """
        Here is your planner for today, November 4, 2026:

        Wake up, make bed (06:00)
        Prayer (3 min) + journaling (2 min) (06:05)
        Get ready, buffer (06:10)
        Drink water (06:30)
        Read 20 pages (06:30)
        Lunch (12:45)
        Transition (13:10)
        """;

    private static async Task<string> Review(string message, string reply)
    {
        var draft = new ReplyDraft(reply, new ChatTurnState("test-model", new HashSet<string>(), ["get_planner"], userMessage: message));
        await new UnaskedListGuard().ReviewAsync(draft, CancellationToken.None);
        return draft.Text;
    }

    [Fact]
    public async Task A_list_and_a_note_about_the_user_go_from_an_answer_about_something_else()
    {
        var text = await Review("Okay, that's great. So who is the developer of the personal OIS?",
            Day + "\nThe user is looking for the developer of PersonaOS, which is an open-source project. "
            + "You can explore the project's GitHub repository for more details about the development team.");

        Assert.Equal("You can explore the project's GitHub repository for more details about the development team.", text);
    }

    [Fact]
    public async Task A_list_asked_for_stays()
    {
        Assert.Equal(Day, await Review("Hey, what's my plan today?", Day));
    }

    [Fact]
    public async Task A_reply_that_is_only_the_list_is_left_alone()
    {
        Assert.Equal(Day, await Review("Who made you?", Day));
    }

    [Fact]
    public async Task A_short_list_stays()
    {
        const string reply = "Here are your goals:\n- GOAL-1 Get fit\n- GOAL-2 Read more";
        Assert.Equal(reply, await Review("How am I doing?", reply));
    }
}
