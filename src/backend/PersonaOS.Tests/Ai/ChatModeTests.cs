using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The mode switch in the chat box: Brainstorm and Reflect change nothing, Plan proposes nested
/// plans, and the choice stays with the conversation.
/// </summary>
public class ChatModeTests
{
    private static async Task<ChatStreamEvent> DoneAsync(ChatService chat, string message, string? mode = null, int? conversationId = null)
    {
        ChatStreamEvent? done = null;
        await foreach (var evt in chat.StreamChatAsync(conversationId, message, mode)) if (evt.Type is "done" or "error") done = evt;
        return done!;
    }

    [Theory]
    [InlineData(ChatModes.Brainstorm)]
    [InlineData(ChatModes.Reflect)]
    public async Task Modes_that_change_nothing_are_offered_no_write_tools(string mode)
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer,
            new FakeTool("get_goals"), new FakeTool("create_goal", mutates: true));
        streamer.EnqueueText("Here are some ideas.");

        await DoneAsync(chat, "ideas for next quarter?", mode);

        Assert.Equal(["get_goals"], streamer.ReceivedRequests[0].Tools.Select(t => t.Name));
    }

    [Fact]
    public async Task Chat_offers_every_tool()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer,
            new FakeTool("get_goals"), new FakeTool("create_goal", mutates: true));
        streamer.EnqueueText("Sure.");

        await DoneAsync(chat, "hi", ChatModes.Chat);

        Assert.Equal(["get_goals", "create_goal"], streamer.ReceivedRequests[0].Tools.Select(t => t.Name));
    }

    [Fact]
    public async Task A_write_tool_called_in_brainstorm_anyway_is_refused_not_carded()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var create = new FakeTool("create_goal", mutates: true);
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, create);
        streamer
            .EnqueueToolCall("toolu_1", "create_goal", """{"title":"Run a 10k"}""")
            .EnqueueText("You could aim for a 10k.");

        var done = await DoneAsync(chat, "what could I aim for?", ChatModes.Brainstorm);

        Assert.Null(done.Pending);
        Assert.Empty(create.Invocations);
        var refusal = streamer.ReceivedRequests[1].Turns.Last().ToolResults!.Single();
        Assert.True(refusal.IsError);
        Assert.Contains("Brainstorm mode", refusal.Content);
    }

    [Fact]
    public async Task The_mode_stays_with_the_conversation_until_changed()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, new FakeTool("create_goal", mutates: true));
        streamer.EnqueueText("One.").EnqueueText("Two.").EnqueueText("Three.");

        var first = await DoneAsync(chat, "hi", ChatModes.Reflect);
        await DoneAsync(chat, "and?", mode: null, conversationId: first.ConversationId);
        Assert.Empty(streamer.ReceivedRequests[1].Tools);

        await DoneAsync(chat, "now do it", ChatModes.Act, first.ConversationId);
        Assert.Single(streamer.ReceivedRequests[2].Tools);

        var conversation = db.Conversations.Single();
        Assert.Equal(ChatModes.Act, (await chat.GetConversationAsync(conversation.PublicId))!.Mode);
        Assert.Equal(ChatModes.Act, (await chat.ListConversationsAsync()).Single().Mode);
    }

    [Fact]
    public async Task An_unknown_mode_is_chat()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, new FakeTool("create_goal", mutates: true));
        streamer.EnqueueText("Hi.");

        await DoneAsync(chat, "hi", "yolo");

        Assert.Equal(ChatModes.Chat, db.Conversations.Single().Mode);
        Assert.Single(streamer.ReceivedRequests[0].Tools);
    }

    [Theory]
    [InlineData(ChatModes.Brainstorm, "Mode: Brainstorm")]
    [InlineData(ChatModes.Plan, "create_plan")]
    [InlineData(ChatModes.Act, "Mode: Act")]
    [InlineData(ChatModes.Reflect, "Mode: Reflect")]
    public async Task Each_mode_adds_its_instructions(string mode, string expected)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.GetOrCreateAsync();

        var prompt = await new SystemPromptBuilder(config, db, TestPrompts.Library()).BuildAsync(new PromptRequest("hi", mode));
        var plain = await new SystemPromptBuilder(config, db, TestPrompts.Library()).BuildAsync(new PromptRequest("hi", ChatModes.Chat));

        Assert.Contains(expected, prompt);
        Assert.DoesNotContain("Mode:", plain);
    }
}

public class CreatePlanToolTests
{
    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (CreatePlanTool Tool, TestDbContext Db) Setup()
    {
        var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(TestGoals.Today);
        var config = new FakeInstanceConfigService(db);
        var board = new BoardService(db, config, new FakePushSender(), clock, NullLogger<BoardService>.Instance);
        var goals = new GoalService(db, config, board, clock);
        return (new CreatePlanTool(goals, board, config), db);
    }

    private const string Quarter = """
        {"goal": {"title": "Get fit", "periodType": "quarter", "year": 2026, "quarter": 4, "goals": [
          {"title": "Build a base", "periodType": "month", "month": 10, "tasks": [{"title": "Run 3 times a week", "points": 3}, "Buy shoes"]},
          {"title": "Go longer", "periodType": "month", "month": 11},
          {"title": "Race", "periodType": "month", "month": 12, "tasks": [{"title": "Run a 10k"}]}
        ]}}
        """;

    [Fact]
    public async Task A_quarter_with_its_months_and_tasks_is_one_card_that_says_what_it_holds()
    {
        var (tool, _) = Setup();

        await tool.ValidateAsync(Input(Quarter));
        var target = await tool.DescribeTargetAsync(Input(Quarter));

        Assert.Equal("“Get fit” (Quarterly) with 3 goals and 3 tasks under it", target);
    }

    [Fact]
    public async Task Confirming_creates_every_goal_under_its_parent_and_every_task_under_its_month()
    {
        var (tool, db) = Setup();

        var result = await tool.ExecuteAsync(Input(Quarter));

        Assert.Equal(4, db.Goals.Count());
        var quarter = db.Goals.Single(g => g.Title == "Get fit");
        Assert.All(db.Goals.Where(g => g.PeriodType == GoalPeriods.Month), m => Assert.Equal(quarter.Id, m.ParentGoalId));
        var october = db.Goals.Single(g => g.Title == "Build a base");
        Assert.Equal(["Buy shoes", "Run 3 times a week"], db.BoardTasks.Where(t => t.GoalId == october.Id).Select(t => t.Title).OrderBy(t => t));
        Assert.Equal(3, db.BoardTasks.Single(t => t.Title == "Run 3 times a week").Points);
        Assert.Contains("\"taskCount\":3", result);
    }

    [Fact]
    public async Task A_month_straight_under_a_year_is_refused_before_any_card()
    {
        var (tool, db) = Setup();

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => tool.ValidateAsync(Input("""
            {"goal": {"title": "2027", "periodType": "year", "year": 2027, "goals": [{"title": "Jan", "periodType": "month", "month": 1}]}}
            """)));

        Assert.Contains("year > quarter > month", ex.Message);
        Assert.Empty(db.Goals);
    }

    [Fact]
    public async Task Tasks_under_a_quarter_are_refused()
    {
        var (tool, _) = Setup();

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => tool.ValidateAsync(Input("""
            {"goal": {"title": "Q4", "periodType": "quarter", "quarter": 4, "tasks": ["Run"]}}
            """)));

        Assert.Contains("Tasks sit only under monthly goals", ex.Message);
    }

    [Fact]
    public async Task Two_goals_for_the_same_month_are_refused()
    {
        var (tool, _) = Setup();

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => tool.ValidateAsync(Input("""
            {"goal": {"title": "Q4", "periodType": "quarter", "quarter": 4, "goals": [
              {"title": "A", "periodType": "month", "month": 10}, {"title": "B", "periodType": "month", "month": 10}]}}
            """)));

        Assert.Contains("two goals for", ex.Message);
    }

    [Fact]
    public async Task Points_off_the_scale_are_refused()
    {
        var (tool, _) = Setup();

        var ex = await Assert.ThrowsAsync<GoalValidationException>(() => tool.ValidateAsync(Input("""
            {"goal": {"title": "October", "periodType": "month", "month": 10, "tasks": [{"title": "Run", "points": 4}]}}
            """)));

        Assert.Contains("value points are one of", ex.Message);
    }
}
