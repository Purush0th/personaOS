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
        return (db, TestChat.Create(db, config, streamer, tools), streamer);
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
        var chat = TestChat.Create(db, config, streamer, docs);

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
        var chat = TestChat.Create(db, config, new FakeAiMessageStreamer());

        var events = await CollectAsync(chat.StreamChatAsync(null, "Hi"));

        var error = Assert.Single(events);
        Assert.Equal("error", error.Type);
        Assert.Contains("API key", error.Error);
    }

    [Fact]
    public async Task Tool_call_written_as_text_is_stripped_before_it_is_stored()
    {
        // A weak model prints the call instead of emitting it, so nothing runs. The raw
        // JSON must not reach the transcript: the next turn would read it back from
        // history and copy the mistake.
        var (db, chat, streamer) = Setup(new FakeTool("get_goals"));
        streamer.EnqueueText("""I will check. {"name": "get_goals", "arguments": {}}""");

        var events = await CollectAsync(chat.StreamChatAsync(null, "What are my goals?"));

        var stored = await db.ChatMessages.OrderBy(m => m.Id).LastAsync();
        Assert.Equal("I will check.", stored.Content);
        Assert.DoesNotContain("get_goals", stored.Content);

        // 'done' carries the corrected text so the client can replace what it streamed.
        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Equal("I will check.", done.Text);
    }

    [Fact]
    public async Task A_reply_that_was_only_a_leaked_call_is_replaced_with_an_explanation()
    {
        var (db, chat, streamer) = Setup(new FakeTool("get_goals"));
        streamer.EnqueueText("""{"name": "get_goals", "arguments": {}}""");

        var events = await CollectAsync(chat.StreamChatAsync(null, "What are my goals?"));

        var stored = await db.ChatMessages.OrderBy(m => m.Id).LastAsync();
        Assert.DoesNotContain("get_goals", stored.Content);
        Assert.Contains("nothing was changed", stored.Content);
        Assert.Equal(stored.Content, Assert.Single(events, e => e.Type == "done").Text);
    }

    [Fact]
    public async Task Executed_tools_are_recorded_on_the_message_and_returned_on_done()
    {
        var tool = new FakeTool("add_planner_item", """{"id":1,"title":"Buy milk","date":"2026-09-06"}""");
        var (db, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("call-1", "add_planner_item", """{"title":"Buy milk"}""");
        streamer.EnqueueText("Added it.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Add buy milk"));

        var done = Assert.Single(events, e => e.Type == "done");
        var receipt = Assert.Single(done.Actions!);
        Assert.Equal("add_planner_item", receipt.Tool);
        Assert.True(receipt.Ok);
        Assert.Contains("Buy milk", receipt.Summary);

        // Persisted, so the evidence survives a reload rather than living only in the stream.
        var stored = await db.ChatMessages.OrderBy(m => m.Id).LastAsync();
        Assert.NotNull(stored.ToolActionsJson);
        Assert.Contains("add_planner_item", stored.ToolActionsJson);
    }

    [Fact]
    public async Task Receipts_come_back_when_the_conversation_is_reopened()
    {
        var tool = new FakeTool("add_planner_item", """{"id":1,"title":"Buy milk","date":"2026-09-06"}""");
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("call-1", "add_planner_item", """{"title":"Buy milk"}""");
        streamer.EnqueueText("Added it.");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Add buy milk"));
        var conversationId = events[0].ConversationId!.Value;

        var detail = await chat.GetConversationAsync(conversationId.ToString());

        var assistant = detail!.Messages.Last(m => m.Role == ChatRoles.Assistant);
        var receipt = Assert.Single(assistant.ToolActions!);
        Assert.Contains("Buy milk", receipt.Summary);
    }

    [Fact]
    public async Task A_conversation_is_reachable_by_its_public_id_as_well_as_its_row_id()
    {
        // The public id is what appears in URLs; the numeric id keeps older links working.
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("Hello there");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Hi"));
        var id = events[0].ConversationId!.Value;
        var publicId = (await db.Conversations.SingleAsync(c => c.Id == id)).PublicId;

        Assert.Equal(8, publicId.Length);
        Assert.Matches("^[a-z0-9]+$", publicId);

        var byPublicId = await chat.GetConversationAsync(publicId);
        var byRowId = await chat.GetConversationAsync(id.ToString());

        Assert.NotNull(byPublicId);
        Assert.Equal(id, byPublicId!.Id);
        Assert.Equal(publicId, byPublicId.PublicId);
        Assert.Equal(id, byRowId!.Id);
    }

    [Fact]
    public async Task An_unknown_reference_returns_null_rather_than_throwing()
    {
        var (_, chat, _) = Setup();

        Assert.Null(await chat.GetConversationAsync("zzzzzzzz"));
        Assert.Null(await chat.GetConversationAsync("9999"));
        Assert.Null(await chat.GetConversationAsync("not-an-id"));
    }

    [Fact]
    public async Task A_tool_that_writes_is_proposed_rather_than_executed()
    {
        // The whole point of the gate: models have created goals and reminders nobody asked
        // for. Nothing may reach the database on the model's say-so.
        var tool = new FakeTool("create_goal", """{"created":{"id":1,"title":"Learn C#"}}""", mutates: true);
        var (db, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#","periodType":"month"}""");
        streamer.EnqueueText("I can create that — confirm when you're ready.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));

        Assert.Empty(tool.Invocations);
        var done = Assert.Single(events, e => e.Type == "done");
        var proposal = Assert.Single(done.Pending!);
        Assert.Equal("create_goal", proposal.Tool);
        Assert.Equal(PendingActionStatuses.Pending, proposal.Status);
        Assert.Contains("Learn C#", proposal.Summary);
        Assert.Null(done.Actions);

        Assert.Equal(PendingActionStatuses.Pending, (await db.PendingActions.SingleAsync()).Status);
    }

    [Fact]
    public async Task The_same_call_twice_is_answered_from_the_first_result()
    {
        // Seen live: "what are my tasks for today?" called get_planner eight times with the same
        // date and never wrote a reply. Running it again would not change the answer.
        var tool = new FakeTool("get_planner", """{"items":[]}""");
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "get_planner", """{"date":"2026-09-21"}""");
        streamer.EnqueueToolCall("c2", "get_planner", """{"date":"2026-09-21"}""");
        streamer.EnqueueText("Nothing planned today.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "What are my tasks for today?"));

        Assert.Single(tool.Invocations);
        var repeated = streamer.ReceivedTurns[^1]
            .Last(t => t.ToolResults is { Count: > 0 }).ToolResults![0];
        Assert.Contains("You already called this tool", repeated.Content);
        Assert.Equal("Nothing planned today.", Assert.Single(events, e => e.Type == "done").Text
            ?? string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text)));
    }

    [Fact]
    public async Task A_model_that_only_repeats_itself_is_stopped()
    {
        // Three identical calls and no answer: more rounds cost the user time and change nothing.
        var tool = new FakeTool("get_planner", """{"items":[]}""");
        var (_, chat, streamer) = Setup(tool);
        for (var i = 0; i < 6; i++)
        {
            streamer.EnqueueToolCall($"c{i}", "get_planner", """{"date":"2026-09-21"}""");
        }

        var events = await CollectAsync(chat.StreamChatAsync(null, "What are my tasks for today?"));

        Assert.Single(tool.Invocations);
        // Four rounds at most: the first call, then two repeats, then the loop gives up.
        Assert.True(streamer.CallCount <= 4, $"asked the model {streamer.CallCount} times");
        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Contains("did not manage to write an answer", done.Text);
    }

    [Fact]
    public async Task A_proposal_whose_target_does_not_exist_is_refused_before_the_user_sees_it()
    {
        // Reported from the phone: the model passed a list position, the card looked fine, and
        // only after tapping Confirm did it fail with "Goal 5 does not exist." Check first.
        var tool = new FakeTool("delete_goal", mutates: true) { ValidationError = "There is no goal GOAL-5." };
        var (db, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "delete_goal", """{"goalKey":"GOAL-5"}""");
        streamer.EnqueueText("That goal does not exist — which one did you mean?");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Delete goal 5"));

        Assert.Empty(tool.Invocations);
        Assert.Null(Assert.Single(events, e => e.Type == "done").Pending);
        Assert.Empty(await db.PendingActions.ToListAsync());
    }

    [Fact]
    public async Task A_refused_proposal_hands_the_model_the_reason()
    {
        // So it can correct the call in the same turn instead of repeating it.
        var tool = new FakeTool("delete_goal", mutates: true) { ValidationError = "There is no goal GOAL-5." };
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "delete_goal", """{"goalKey":"GOAL-5"}""");
        streamer.EnqueueText("ok");

        await CollectAsync(chat.StreamChatAsync(null, "Delete goal 5"));

        var toolTurn = streamer.ReceivedTurns[^1].Last(t => t.ToolResults is { Count: > 0 });
        var result = Assert.Single(toolTurn.ToolResults!);
        Assert.True(result.IsError);
        Assert.Contains("There is no goal GOAL-5.", result.Content);
    }

    [Fact]
    public async Task The_model_is_told_the_action_did_not_run()
    {
        // Otherwise it reports success for something still waiting on the user.
        var tool = new FakeTool("create_goal", mutates: true);
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#"}""");
        streamer.EnqueueText("ok");

        await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));

        var resultTurn = Assert.Single(streamer.ReceivedTurns[1], t => t.ToolResults is { Count: > 0 });
        var told = resultTurn.ToolResults![0].Content;
        Assert.Contains("not_executed", told);
        Assert.Contains("waiting for their confirmation", told);
        // Phrased as a status rather than as instructions: a 0.5B model read the old wording —
        // "Tell them what you are proposing" — as the reply and said it to the user verbatim.
        Assert.DoesNotContain("Tell them", told);
    }

    [Fact]
    public async Task Confirming_runs_the_tool_and_records_what_happened()
    {
        var tool = new FakeTool(
            "create_goal",
            """{"created":{"id":1,"title":"Learn C#","periodType":"month","periodStart":"2026-09-01"}}""",
            mutates: true);
        var (db, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#"}""");
        streamer.EnqueueText("Confirm?");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));
        var id = Assert.Single(Assert.Single(events, e => e.Type == "done").Pending!).Id;

        var confirmed = await chat.ConfirmActionAsync(id);

        Assert.Single(tool.Invocations);
        Assert.Equal(PendingActionStatuses.Confirmed, confirmed!.Status);
        Assert.True(confirmed.ResultOk);
        Assert.Contains("Learn C#", confirmed.ResultSummary);
        Assert.Equal(PendingActionStatuses.Confirmed, (await db.PendingActions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Confirming_twice_does_not_run_the_tool_twice()
    {
        var tool = new FakeTool("create_goal", mutates: true);
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#"}""");
        streamer.EnqueueText("Confirm?");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));
        var id = Assert.Single(Assert.Single(events, e => e.Type == "done").Pending!).Id;

        await chat.ConfirmActionAsync(id);
        await chat.ConfirmActionAsync(id);

        Assert.Single(tool.Invocations);
    }

    [Fact]
    public async Task Discarding_never_runs_the_tool()
    {
        var tool = new FakeTool("create_goal", mutates: true);
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#"}""");
        streamer.EnqueueText("Confirm?");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));
        var id = Assert.Single(Assert.Single(events, e => e.Type == "done").Pending!).Id;

        var discarded = await chat.DiscardActionAsync(id);

        Assert.Empty(tool.Invocations);
        Assert.Equal(PendingActionStatuses.Discarded, discarded!.Status);
        // And it stays discarded — confirming afterwards must not resurrect it.
        await chat.ConfirmActionAsync(id);
        Assert.Empty(tool.Invocations);
    }

    [Fact]
    public async Task A_reading_tool_still_runs_without_asking()
    {
        // Reads are not gated; the gate would be unusable if every question needed a tap.
        var tool = new FakeTool("get_goals", """{"goals":[]}""");
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "get_goals", "{}");
        streamer.EnqueueText("You have none.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "What are my goals?"));

        Assert.Single(tool.Invocations);
        Assert.Null(Assert.Single(events, e => e.Type == "done").Pending);
    }

    [Fact]
    public async Task Proposals_come_back_when_the_conversation_is_reopened()
    {
        var tool = new FakeTool("create_goal", mutates: true);
        var (_, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#"}""");
        streamer.EnqueueText("Confirm?");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));
        var conversationId = events[0].ConversationId!.Value;

        var detail = await chat.GetConversationAsync(conversationId.ToString());

        var assistant = detail!.Messages.Last(m => m.Role == ChatRoles.Assistant);
        var proposal = Assert.Single(assistant.PendingActions!);
        Assert.Equal(PendingActionStatuses.Pending, proposal.Status);
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_its_messages_and_proposals()
    {
        var tool = new FakeTool("create_goal", mutates: true);
        var (db, chat, streamer) = Setup(tool);
        streamer.EnqueueToolCall("c1", "create_goal", """{"title":"Learn C#"}""");
        streamer.EnqueueText("Confirm?");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Add a goal"));
        var publicId = (await db.Conversations.SingleAsync()).PublicId;
        Assert.NotEmpty(await db.ChatMessages.ToListAsync());
        Assert.NotEmpty(await db.PendingActions.ToListAsync());

        Assert.True(await chat.DeleteConversationAsync(publicId));

        Assert.Empty(await db.Conversations.ToListAsync());
        // Nothing orphaned: an unconfirmed proposal must not outlive the thread that made it.
        Assert.Empty(await db.ChatMessages.ToListAsync());
        Assert.Empty(await db.PendingActions.ToListAsync());
    }

    [Fact]
    public async Task Deleting_accepts_a_numeric_id_too_and_reports_the_unknown()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("hi");
        var events = await CollectAsync(chat.StreamChatAsync(null, "Hello"));
        var id = events[0].ConversationId!.Value;

        Assert.False(await chat.DeleteConversationAsync("zzzzzzzz"));
        Assert.True(await chat.DeleteConversationAsync(id.ToString()));
        Assert.Empty(await db.Conversations.ToListAsync());
    }

    [Fact]
    public async Task Deleting_one_conversation_leaves_the_others_alone()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("one");
        await CollectAsync(chat.StreamChatAsync(null, "first"));
        streamer.EnqueueText("two");
        await CollectAsync(chat.StreamChatAsync(null, "second"));
        var first = (await db.Conversations.OrderBy(c => c.Id).FirstAsync()).PublicId;

        await chat.DeleteConversationAsync(first);

        var remaining = await db.Conversations.SingleAsync();
        Assert.Equal("second", remaining.Title);
        Assert.All(await db.ChatMessages.ToListAsync(), m => Assert.Equal(remaining.Id, m.ConversationId));
    }

    [Fact]
    public async Task An_unknown_action_id_returns_null()
    {
        var (_, chat, _) = Setup();

        Assert.Null(await chat.ConfirmActionAsync("zzzzzzzz"));
        Assert.Null(await chat.DiscardActionAsync("zzzzzzzz"));
    }

    [Fact]
    public async Task Each_conversation_gets_its_own_public_id()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("one");
        await CollectAsync(chat.StreamChatAsync(null, "first"));
        streamer.EnqueueText("two");
        await CollectAsync(chat.StreamChatAsync(null, "second"));

        var ids = await db.Conversations.Select(c => c.PublicId).ToListAsync();

        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task A_turn_with_no_tools_records_none()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("Just talking.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Hi"));

        Assert.Null(Assert.Single(events, e => e.Type == "done").Actions);
        var stored = await db.ChatMessages.OrderBy(m => m.Id).LastAsync();
        Assert.Null(stored.ToolActionsJson);
    }

    [Fact]
    public async Task An_ordinary_reply_is_stored_verbatim_and_done_carries_no_text()
    {
        var (db, chat, streamer) = Setup(new FakeTool("get_goals"));
        streamer.EnqueueText("""Here is an example config: {"name": "my-service", "port": 8080}""");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Show me a config"));

        var stored = await db.ChatMessages.OrderBy(m => m.Id).LastAsync();
        Assert.Equal("""Here is an example config: {"name": "my-service", "port": 8080}""", stored.Content);
        Assert.Null(Assert.Single(events, e => e.Type == "done").Text);
    }
}
