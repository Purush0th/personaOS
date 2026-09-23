using PersonaOS.Application.Ai;
using PersonaOS.Application.Board;
using PersonaOS.Application.Board.Tools;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Application.Planner;
using PersonaOS.Application.Planner.Tools;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// A card has to say which item it changes, by name. The call itself only carries an id or a key,
/// and a model that passes a list position ("2") or the wrong key would otherwise be confirmed
/// blind: "Update planner item status — done" does not say which item.
/// </summary>
public class ProposalTargetTests
{
    private static async Task<List<ChatStreamEvent>> CollectAsync(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var evt in stream) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task A_planner_card_names_the_item_and_its_day()
    {
        var db = TestDbContext.Create();
        var planner = new PlannerService(db);
        var gym = await planner.CreateAsync(new CreatePlannerItemRequest("Gym", new DateOnly(2026, 9, 23)));
        var streamer = new FakeAiMessageStreamer()
            .EnqueueToolCall("t1", "update_planner_item_status", $$"""{"itemId":{{gym.Id}},"status":"done"}""")
            .EnqueueText("Marking the gym as done — confirm below.");
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, new UpdatePlannerItemStatusTool(planner));

        var events = await CollectAsync(chat.StreamChatAsync(null, "I went to the gym"));

        var card = Assert.Single(Assert.Single(events, e => e.Type == "done").Pending!);
        Assert.Equal("Update planner item status “Gym” on 2026-09-23 — done", card.Summary);
    }

    [Fact]
    public async Task A_goal_card_names_the_goal_behind_the_key()
    {
        var db = TestDbContext.Create();
        var goals = new GoalService(db);
        await goals.CreateAsync(new CreateGoalRequest("Learn Rust", null, GoalPeriods.Month, new DateOnly(2026, 9, 1)));
        var tool = new DeleteGoalTool(goals);

        var target = await tool.DescribeTargetAsync(System.Text.Json.JsonDocument.Parse("""{"goalKey":"GOAL-1"}""").RootElement);

        Assert.Equal("GOAL-1 “Learn Rust”", target);
        Assert.Equal("Delete goal GOAL-1 “Learn Rust”", ProposedActionSummary.Describe("delete_goal", """{"goalKey":"GOAL-1"}""", target));
    }

    [Fact]
    public async Task A_task_card_names_the_task_and_keeps_the_details()
    {
        var db = TestDbContext.Create();
        var board = new BoardService(db, new FakeInstanceConfigService(db), new FakePushSender(), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BoardService>.Instance);
        await board.CreateTaskAsync(new CreateTaskRequest("File taxes"));
        var tool = new MoveTaskTool(board, new GoalService(db));
        const string input = """{"taskKey":"TASK-1","column":"done"}""";

        var target = await tool.DescribeTargetAsync(System.Text.Json.JsonDocument.Parse(input).RootElement);

        Assert.Equal("Move task TASK-1 “File taxes” — to done", ProposedActionSummary.Describe("move_task", input, target));
    }

    [Fact]
    public async Task A_key_that_names_nothing_gives_no_target_and_the_card_falls_back_to_the_fields()
    {
        var db = TestDbContext.Create();
        var target = await new DeleteGoalTool(new GoalService(db))
            .DescribeTargetAsync(System.Text.Json.JsonDocument.Parse("""{"goalKey":"GOAL-9"}""").RootElement);

        Assert.Null(target);
        Assert.Contains("GOAL-9", ProposedActionSummary.Describe("delete_goal", """{"goalKey":"GOAL-9"}""", target));
    }
}
