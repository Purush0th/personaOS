using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// Exercises the agentic loop end to end with a scripted model: tool calls are
/// executed, results handed back, and the exchange persisted. This is the path
/// every module's Claude tools depend on.
/// </summary>
public class ChatServiceToolLoopTests
{
    private static (TestDbContext Db, ChatService Chat, FakeAiMessageStreamer Streamer) Setup(
        params IPersonaTool[] tools)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var streamer = new FakeAiMessageStreamer();
        var registry = new PersonaToolRegistry(tools, config, NullLogger<PersonaToolRegistry>.Instance);
        var chat = new ChatService(
            db, config, new FakeSystemPromptBuilder(), streamer, registry,
            NullLogger<ChatService>.Instance);
        return (db, chat, streamer);
    }

    private static async Task<List<ChatStreamEvent>> CollectAsync(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var evt in stream) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task Plain_reply_streams_and_persists_with_usage()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("Hello there", inputTokens: 42, outputTokens: 7);

        var events = await CollectAsync(chat.StreamChatAsync(null, "Hi"));

        Assert.Equal("start", events[0].Type);
        Assert.Equal("Hello there", string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text)));

        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Equal(42, done.InputTokens);
        Assert.Equal(7, done.OutputTokens);

        var messages = await db.ChatMessages.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRoles.User, messages[0].Role);
        Assert.Equal("Hi", messages[0].Content);
        Assert.Equal(ChatRoles.Assistant, messages[1].Role);
        Assert.Equal("Hello there", messages[1].Content);
        Assert.Equal(7, messages[1].OutputTokens);
    }

    [Fact]
    public async Task Tool_call_is_executed_and_result_fed_back()
    {
        var tool = new FakeTool("get_goals", """{"goals":[{"id":1,"title":"Ship v1"}]}""");
        var (db, chat, streamer) = Setup(tool);
        streamer
            .EnqueueToolCall("toolu_1", "get_goals", """{"includeDropped":false}""")
            .EnqueueText("You have one goal: Ship v1.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "What are my goals?"));

        // The tool ran, and the client was told which one.
        Assert.Single(tool.Invocations);
        var toolEvent = Assert.Single(events, e => e.Type == "tool");
        Assert.Equal("get_goals", toolEvent.ToolName);

        // Two model rounds: the tool request, then the final answer.
        Assert.Equal(2, streamer.CallCount);

        // The second round carried the assistant tool-call turn and the result turn.
        var secondRound = streamer.ReceivedTurns[1];
        var assistantTurn = Assert.Single(secondRound, t => t.ToolCalls is { Count: > 0 });
        Assert.Equal("get_goals", assistantTurn.ToolCalls![0].Name);
        var resultTurn = Assert.Single(secondRound, t => t.ToolResults is { Count: > 0 });
        Assert.Equal("toolu_1", resultTurn.ToolResults![0].ToolUseId);
        Assert.Contains("Ship v1", resultTurn.ToolResults[0].Content);
        Assert.False(resultTurn.ToolResults[0].IsError);

        // Only the user-visible text is persisted, not the tool plumbing.
        var assistantMessage = await db.ChatMessages
            .Where(m => m.Role == ChatRoles.Assistant).SingleAsync();
        Assert.Equal("You have one goal: Ship v1.", assistantMessage.Content);
    }

    [Fact]
    public async Task Multiple_tool_calls_in_one_round_all_execute()
    {
        var goals = new FakeTool("get_goals");
        var planner = new FakeTool("get_planner");
        var (_, chat, streamer) = Setup(goals, planner);
        streamer.Enqueue([
            new AiStreamChunk(ToolCall: new AiToolCall("t1", "get_goals", "{}")),
            new AiStreamChunk(ToolCall: new AiToolCall("t2", "get_planner", "{}")),
            new AiStreamChunk(StopReason: AiStopReasons.ToolUse),
        ]);
        streamer.EnqueueText("Here's your day.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Brief me"));

        Assert.Single(goals.Invocations);
        Assert.Single(planner.Invocations);
        Assert.Equal(2, events.Count(e => e.Type == "tool"));

        var resultTurn = Assert.Single(streamer.ReceivedTurns[1], t => t.ToolResults is { Count: 2 });
        Assert.Equal(["t1", "t2"], resultTurn.ToolResults!.Select(r => r.ToolUseId));
    }

    [Fact]
    public async Task Unknown_tool_returns_an_error_result_instead_of_throwing()
    {
        var (_, chat, streamer) = Setup();
        streamer
            .EnqueueToolCall("toolu_1", "no_such_tool", "{}")
            .EnqueueText("Sorry, I can't do that.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Do something"));

        // The loop survives and the model is told the tool failed.
        var resultTurn = Assert.Single(streamer.ReceivedTurns[1], t => t.ToolResults is { Count: > 0 });
        Assert.True(resultTurn.ToolResults![0].IsError);
        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.Contains(events, e => e.Type == "done");
    }

    [Fact]
    public async Task Disabled_feature_hides_the_tool_from_the_model()
    {
        var docs = new FakeTool("read_document", requiredFeature: InstanceConfig.Modules.Docs);
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.UpdateAsync(c => c.Features[InstanceConfig.Modules.Docs] = false);

        var streamer = new FakeAiMessageStreamer();
        streamer.EnqueueText("ok");
        var chat = new ChatService(
            db, config, new FakeSystemPromptBuilder(), streamer,
            new PersonaToolRegistry([docs], config, NullLogger<PersonaToolRegistry>.Instance),
            NullLogger<ChatService>.Instance);

        await CollectAsync(chat.StreamChatAsync(null, "Read my notes"));

        Assert.Empty(streamer.ReceivedTools[0]);
    }

    [Fact]
    public async Task Stream_failure_before_any_text_surfaces_an_error_event()
    {
        var (db, chat, streamer) = Setup();
        streamer.ThrowOnFirstCall = new AiStreamException("The Anthropic API rejected the configured API key.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Hi"));

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Contains("rejected the configured API key", error.Error);
        Assert.DoesNotContain(events, e => e.Type == "done");
        // Nothing half-written is persisted.
        Assert.Empty(await db.ChatMessages.ToListAsync());
    }

    [Fact]
    public async Task History_is_replayed_to_the_model_on_a_follow_up()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("First answer");
        var first = await CollectAsync(chat.StreamChatAsync(null, "First question"));
        var conversationId = first[0].ConversationId;

        streamer.EnqueueText("Second answer");
        await CollectAsync(chat.StreamChatAsync(conversationId, "Second question"));

        var secondCallTurns = streamer.ReceivedTurns[1];
        Assert.Equal(
            ["First question", "First answer", "Second question"],
            secondCallTurns.Select(t => t.Content));
        Assert.Equal(4, await db.ChatMessages.CountAsync());
    }

    [Fact]
    public async Task Missing_api_key_reports_a_clear_error()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db, apiKey: null);
        var chat = new ChatService(
            db, config, new FakeSystemPromptBuilder(), new FakeAiMessageStreamer(),
            new PersonaToolRegistry([], config, NullLogger<PersonaToolRegistry>.Instance),
            NullLogger<ChatService>.Instance);

        var events = await CollectAsync(chat.StreamChatAsync(null, "Hi"));

        var error = Assert.Single(events);
        Assert.Equal("error", error.Type);
        Assert.Contains("API key", error.Error);
    }
}
