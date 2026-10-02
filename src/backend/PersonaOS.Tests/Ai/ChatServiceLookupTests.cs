using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// "I'll list all the tasks in this sprint… Hold tight" with no tool called: the user waits for an
/// answer that never comes. Seen on qwen2.5:3b (2026-09-27). The model gets one round to make the
/// call it promised.
/// </summary>
public class ChatServiceLookupTests
{
    private static async Task<ChatStreamEvent> DoneAsync(ChatService chat, string message)
    {
        ChatStreamEvent? done = null;
        await foreach (var evt in chat.StreamChatAsync(null, message)) if (evt.Type == "done") done = evt;
        return done!;
    }

    [Fact]
    public async Task A_promised_lookup_is_made_and_answered()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var board = new FakeTool("get_board", """{"board":{"sprint":{"key":"SPRINT-1","unestimated":"none"}}}""");
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, board);
        streamer
            .EnqueueText("I'll list all the tasks in this sprint and review their value points. Hold tight while I do that for you.")
            .EnqueueToolCall("toolu_1", "get_board", "{}")
            .EnqueueText("Every task in SPRINT-1 has value points.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Single(board.Invocations);
        Assert.Equal("Every task in SPRINT-1 has value points.", done.Text);
    }

    [Fact]
    public async Task A_lookup_round_that_only_deflects_again_does_not_replace_the_reply()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, new FakeTool("get_board", """{"board":{}}"""));
        streamer
            .EnqueueText("Sure, let's look at the tasks in SPRINT-1:\n- TASK-1: File the tax return [todo, 5 pts]")
            .EnqueueText("I do not have the detailed information for each task. Please call the tools to get the details.");

        var done = await DoneAsync(chat, "can you check the task?");

        Assert.Null(done.Text); // what streamed stands
    }

    [Fact]
    public async Task The_lookup_instruction_names_the_tool_the_question_needs()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer,
            new FakeTool("get_board", """{"board":{}}"""), new FakeTool("get_goals", """{"goals":[]}"""));
        streamer
            .EnqueueText("Could you please provide the task key so I can check its status?")
            .EnqueueToolCall("toolu_1", "get_board", "{}")
            .EnqueueText("Every task has value points.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Equal("Every task has value points.", done.Text);
        Assert.Contains(streamer.ReceivedRequests[1].Turns, t => t.Content.Contains("Call get_board now"));
    }

    [Fact]
    public async Task A_lookup_answer_naming_an_item_that_does_not_exist_is_dropped()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, new FakeTool("get_board", """{"board":{}}"""));
        streamer
            .EnqueueText("I'll confirm its current status.")
            .EnqueueText("The task with key TASK-7 is already estimated.");

        var done = await DoneAsync(chat, "can you check the task?");

        Assert.DoesNotContain("TASK-7", done.Text ?? "I'll confirm its current status.");
    }

    [Theory]
    [InlineData("which one task is not estimated?", "get_board")]
    [InlineData("how are my goals?", "get_goals")]
    [InlineData("what's on today?", "get_planner")]
    [InlineData("hello", null)]
    public void Picks_the_tool_a_lookup_needs(string text, string? tool)
    {
        Assert.Equal(tool, ChatService.LookupToolFor(text, ["get_board", "get_goals", "get_planner", "get_reminders"]));
    }

    [Theory]
    [InlineData("how many points does TASK-11 have?", "get_task")]
    [InlineData("which one task is not estimated?", "get_board")]
    public void A_task_named_by_key_is_looked_up_on_its_own(string text, string tool)
    {
        Assert.Equal(tool, ChatService.LookupToolFor(text, ["get_task", "get_board", "get_goals"]));
    }

    [Fact]
    public void Names_no_tool_that_is_not_enabled()
    {
        Assert.Null(ChatService.LookupToolFor("which task?", ["get_goals"]));
    }

    [Fact]
    public async Task An_answer_that_looked_it_up_is_left_alone()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, new FakeTool("get_board", """{"board":{}}"""));
        streamer
            .EnqueueToolCall("toolu_1", "get_board", "{}")
            .EnqueueText("Every task has value points. Let me know if you want to change one.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Null(done.Text);
        Assert.Equal(2, streamer.ReceivedRequests.Count); // the tool round and the answer, nothing more
    }

    [Theory]
    [InlineData("I'll list all the tasks in this sprint. Hold tight while I do that for you.", true)]
    [InlineData("Please wait, I'll run this for you.", true)]
    [InlineData("Let me check the board for you.", true)]
    [InlineData("I don't have the specific task details for the sprint \"Paperwork week\".", true)]
    [InlineData("You can find their current value points by reviewing them in the sprint board.", true)]
    [InlineData("Let's review the current sprint.", true)]
    [InlineData("To check which task is not yet estimated, I need to see the sprint board.", true)]
    [InlineData("I do not have that detail available directly.", true)]
    [InlineData("I do not have the detailed information for each task in SPRINT-1 currently.", true)]
    [InlineData("Please give me a task key from the current sprint.", true)]
    [InlineData("I can get the sprint report which will show a list of all tasks with their points.", true)]
    [InlineData("Please provide the task keys or names so I can review the points for you.", true)]
    [InlineData("Could you please tell me the title or key of the task you want me to check?", true)]
    [InlineData("Sure, let's review the current points for each task:\n- TASK-1 File the tax return: 5 points.", false)]
    [InlineData("Would you like me to add a task?", false)]
    [InlineData("Let me know if you want to change anything.", false)]
    public async Task Finds_a_promise_to_look_something_up(string reply, bool promised)
    {
        var draft = new ReplyDraft(reply, new ChatTurnState("m", new HashSet<string>(), []));

        await new PromisedLookupGuard().ReviewAsync(draft, CancellationToken.None);

        Assert.Equal(promised, draft.PromisedLookup is not null);
    }
}
