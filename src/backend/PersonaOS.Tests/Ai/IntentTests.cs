using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.Intents;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The intent check before every message (docs/model-accuracy.md, owner 2026-10-12): a short call
/// held to a schema decides what the message wants, the router gives that intent its tool, and a
/// turn that made nothing the message asked for gets one focused try at that tool.
/// </summary>
public class IntentTests
{
    private static readonly AiRequest Model = new("key", "qwen2.5:3b-instruct", "http://ollama", "prompt", [], []);

    private static Task<IntentResult?> Classify(FakeAiMessageStreamer streamer, string message, params string[] offered) =>
        new IntentClassifier(NullLogger<IntentClassifier>.Instance).ClassifyAsync(
            streamer, Model, [new AiChatTurn(ChatRoles.User, message)], message, offered, default);

    [Fact]
    public async Task The_intent_comes_with_the_tool_the_router_allows_for_it()
    {
        var streamer = new FakeAiMessageStreamer().EnqueueIntent("create_reminder");

        var result = await Classify(streamer, "Ping me tomorrow at 8am to take the bins out", "create_reminder", "create_task");

        Assert.Equal(new IntentResult("create_reminder", "create_reminder"), result);
    }

    [Fact]
    public async Task The_check_is_held_to_a_schema_of_the_intents_this_turn_can_serve()
    {
        var streamer = new FakeAiMessageStreamer().EnqueueIntent("question");

        await Classify(streamer, "What is on today?", "create_task", "get_board");

        var request = Assert.Single(streamer.ReceivedStructuredRequests);
        using var schema = JsonDocument.Parse(request.ResponseSchemaJson!);
        var allowed = schema.RootElement.GetProperty("properties").GetProperty("intent").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("create_task", allowed);
        Assert.Contains("question", allowed);
        Assert.Contains("ambiguous", allowed);
        // Reminders are not offered this turn, so they cannot be the answer.
        Assert.DoesNotContain("create_reminder", allowed);
        Assert.Empty(request.Tools);
        Assert.Equal(0, request.ModelOptions.Temperature);
    }

    [Fact]
    public async Task An_intent_outside_the_schema_or_prose_reads_as_no_answer()
    {
        Assert.Null(await Classify(new FakeAiMessageStreamer().EnqueueIntent("create_reminder"), "hi", "create_task"));
        Assert.Null(await Classify(new FakeAiMessageStreamer().EnqueueStructured("It is a task."), "hi", "create_task"));
    }

    [Fact]
    public async Task With_nothing_that_can_be_made_no_check_is_asked_for()
    {
        var streamer = new FakeAiMessageStreamer();

        Assert.Null(await Classify(streamer, "What is on today?", "get_board", "update_task"));
        Assert.Empty(streamer.ReceivedStructuredRequests);
    }

    [Fact]
    public async Task A_short_answer_is_classified_with_the_messages_before_it()
    {
        var streamer = new FakeAiMessageStreamer().EnqueueIntent("create_reminder");

        await new IntentClassifier(NullLogger<IntentClassifier>.Instance).ClassifyAsync(streamer, Model,
            [
                new AiChatTurn(ChatRoles.User, "Remind me to buy milk."),
                new AiChatTurn(ChatRoles.Assistant, "What time?"),
                new AiChatTurn(ChatRoles.User, "tomorrow 9 a.m."),
            ],
            "tomorrow 9 a.m.", ["create_reminder"], default);

        var sent = Assert.Single(Assert.Single(streamer.ReceivedStructuredRequests).Turns).Content;
        Assert.Contains("User: Remind me to buy milk.", sent);
        Assert.Contains("Assistant: What time?", sent);
        Assert.EndsWith("Latest message: tomorrow 9 a.m.", sent);
    }

    // --- In the chat loop -----------------------------------------------------------------------

    private static (ChatService Chat, FakeAiMessageStreamer Streamer, FakeTool Reminder) Setup()
    {
        var db = TestDbContext.Create();
        var streamer = new FakeAiMessageStreamer();
        var reminder = new FakeTool("create_reminder", mutates: true);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer, clock,
            reminder, new FakeTool("create_task", mutates: true), new FakeTool("get_reminders"), new FakeTool("create_note"));
        return (chat, streamer, reminder);
    }

    private static async Task<ChatStreamEvent> DoneAsync(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        ChatStreamEvent? done = null;
        await foreach (var evt in stream) if (evt.Type == "done") done = evt;
        return done!;
    }

    [Fact]
    public async Task The_chat_model_is_told_what_the_message_wants()
    {
        var (chat, streamer, _) = Setup();
        streamer.EnqueueIntent("create_reminder").EnqueueText("What time would you like it?");

        await DoneAsync(chat.StreamChatAsync(null, "Remind me to buy milk"));

        var prompt = streamer.ReceivedRequests[0].SystemPrompt;
        Assert.Contains("Intent check:", prompt);
        Assert.Contains("Call create_reminder", prompt);
    }

    [Fact]
    public async Task A_reminder_asked_for_and_not_made_gets_one_try_offered_only_its_tool()
    {
        // "Put 'renew the passport' on my board" drew a goal or a question before (suite, 2026-10-12).
        var (chat, streamer, reminder) = Setup();
        streamer
            .EnqueueIntent("create_reminder")
            .EnqueueText("Sure thing, you will hear from me then.")
            .EnqueueToolCall("toolu_1", "create_reminder", """{"message":"Take the bins out","dueAtLocal":"2026-09-16T08:00"}""")
            .EnqueueText("Done.");

        var done = await DoneAsync(chat.StreamChatAsync(null, "Ping me tomorrow at 8am to take the bins out"));

        Assert.Equal(["create_reminder"], streamer.ReceivedTools[1].Select(t => t.Name));
        Assert.Single(done.Pending!);
        Assert.Equal(EmptyReplyGuard.ProposalOnly, done.Text);
        Assert.Empty(reminder.Invocations);
    }

    [Fact]
    public async Task A_reply_that_asks_for_something_missing_is_left_to_the_user()
    {
        var (chat, streamer, _) = Setup();
        streamer.EnqueueIntent("create_reminder").EnqueueText("What time would you like the reminder?");

        var done = await DoneAsync(chat.StreamChatAsync(null, "Remind me to buy milk"));

        Assert.Equal(1, streamer.CallCount);
        Assert.Null(done.Pending);
    }

    [Fact]
    public async Task An_answer_from_a_lookup_is_not_second_guessed_by_a_wrong_classification()
    {
        // "What are my tasks for today?" was classified create_task, and a task card followed the answer.
        var (chat, streamer, _) = Setup();
        streamer
            .EnqueueIntent("create_reminder")
            .EnqueueToolCall("toolu_1", "get_reminders", "{}")
            .EnqueueText("You have one reminder: call the bank at 5 PM.");

        var done = await DoneAsync(chat.StreamChatAsync(null, "What reminders do I have today?"));

        Assert.Equal(2, streamer.CallCount);
        Assert.Null(done.Pending);
    }

    [Fact]
    public async Task A_message_that_wants_no_change_never_gets_a_focused_try()
    {
        var (chat, streamer, _) = Setup();
        streamer.EnqueueIntent("conversation").EnqueueText("You're welcome!");

        var done = await DoneAsync(chat.StreamChatAsync(null, "Thanks, that's all for now"));

        Assert.Equal(1, streamer.CallCount);
        Assert.Null(done.Pending);
        Assert.Contains("do not change anything", streamer.ReceivedRequests[0].SystemPrompt);
    }

    [Fact]
    public async Task A_note_made_by_the_focused_try_is_reported_from_what_was_saved()
    {
        var (chat, streamer, _) = Setup();
        streamer
            .EnqueueIntent("create_note")
            .EnqueueText("Sure.")
            .EnqueueToolCall("toolu_1", "create_note", """{"text":"Renew the passport"}""")
            .EnqueueText("Saved.");

        var done = await DoneAsync(chat.StreamChatAsync(null, "Jot this down somewhere: renew the passport"));

        Assert.Equal("create_note", Assert.Single(done.Actions!).Tool);
        Assert.StartsWith(ToolLabel.Done("create_note"), done.Text);
        Assert.Null(done.UnverifiedClaim);
    }

    [Fact]
    public async Task Where_nothing_can_be_changed_the_message_is_not_classified()
    {
        var (chat, streamer, _) = Setup();
        streamer.EnqueueText("Here are some ideas.");

        await DoneAsync(chat.StreamChatAsync(null, "Let's think about a holiday", ChatModes.Brainstorm));

        Assert.Empty(streamer.ReceivedStructuredRequests);
    }
}
