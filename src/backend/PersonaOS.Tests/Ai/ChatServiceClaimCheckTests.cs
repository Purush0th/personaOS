using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// A reply that says a change was made when no tool made one. The model gets one corrective
/// round; if the claim survives, the reply is flagged so the client can say nothing was saved.
/// Scripted with the real failure: "I've set a reminder…" with no <c>create_reminder</c> call.
/// </summary>
public class ChatServiceClaimCheckTests
{
    private const string FalseClaim = "I've set a reminder to remind you to submit the report at 19:00 today.";

    private static (TestDbContext Db, ChatService Chat, FakeAiMessageStreamer Streamer) Setup(params IPersonaTool[] tools)
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
    public async Task A_claim_the_model_turns_into_a_proposal_ends_as_a_real_card()
    {
        var reminder = new FakeTool("create_reminder", mutates: true);
        var (db, chat, streamer) = Setup(reminder);
        streamer
            .EnqueueText(FalseClaim)
            // Told nothing was saved, it now calls the tool — which becomes a proposal.
            .EnqueueToolCall("toolu_1", "create_reminder", """{"message":"Submit the report","dueAtLocal":"2026-09-15T19:00"}""")
            .EnqueueText("I'd like to set that reminder for 19:00 — please confirm it below.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Remind me to submit the report at 7pm today"));

        var done = Assert.Single(events, e => e.Type == "done");
        Assert.NotNull(done.Pending);
        Assert.Single(done.Pending!);
        Assert.Null(done.UnverifiedClaim);
        // The stored reply replaces the false claim the user watched stream in.
        Assert.Equal("I'd like to set that reminder for 19:00 — please confirm it below.", done.Text);
        Assert.Empty(reminder.Invocations); // still gated — nothing ran without confirmation

        var stored = await db.ChatMessages.SingleAsync(m => m.Role == ChatRoles.Assistant);
        Assert.DoesNotContain("I've set a reminder", stored.Content);
        Assert.False(stored.UnverifiedClaim);
    }

    [Fact]
    public async Task A_claim_the_model_takes_back_is_replaced_and_not_flagged()
    {
        var (db, chat, streamer) = Setup(new FakeTool("create_reminder", mutates: true));
        streamer
            .EnqueueText(FalseClaim)
            .EnqueueText("I haven't set anything yet. Would you like me to create that reminder for 19:00?");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Remind me to submit the report at 7pm"));

        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Null(done.UnverifiedClaim);
        Assert.StartsWith("I haven't set anything yet.", done.Text);
        Assert.False((await db.ChatMessages.SingleAsync(m => m.Role == ChatRoles.Assistant)).UnverifiedClaim);
    }

    [Fact]
    public async Task A_claim_that_survives_correction_is_flagged_for_the_user()
    {
        var (db, chat, streamer) = Setup(new FakeTool("create_reminder", mutates: true));
        streamer
            .EnqueueText(FalseClaim)
            .EnqueueText("Your reminder has been set for 19:00.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Remind me at 7pm"));

        var done = Assert.Single(events, e => e.Type == "done");
        Assert.True(done.UnverifiedClaim);
        Assert.Null(done.Pending);

        var stored = await db.ChatMessages.SingleAsync(m => m.Role == ChatRoles.Assistant);
        Assert.True(stored.UnverifiedClaim);

        // And the flag survives into conversation history, so a reopened chat still warns.
        var detail = await chat.GetConversationAsync(stored.ConversationId.ToString());
        Assert.True(detail!.Messages.Single(m => m.Role == ChatRoles.Assistant).UnverifiedClaim);
    }

    [Fact]
    public async Task The_correction_request_says_what_was_claimed_and_is_never_stored()
    {
        var (db, chat, streamer) = Setup(new FakeTool("create_reminder", mutates: true));
        streamer.EnqueueText(FalseClaim).EnqueueText("I haven't done that yet — shall I?");

        await CollectAsync(chat.StreamChatAsync(null, "Remind me at 7pm"));

        // The corrective round received the false claim and a request naming it.
        var correctionTurns = streamer.ReceivedTurns[1];
        Assert.Contains(correctionTurns, t => t.Role == ChatRoles.User && t.Content.Contains("did not call any tool"));
        Assert.Contains(correctionTurns, t => t.Content.Contains("I've set a reminder"));

        // Only the user's message and the final reply are kept — never the nudge.
        var stored = await db.ChatMessages.OrderBy(m => m.Id).Select(m => m.Content).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.DoesNotContain(stored, c => c.Contains("did not call any tool"));
    }

    [Fact]
    public async Task An_honest_reply_costs_no_extra_round()
    {
        var (_, chat, streamer) = Setup(new FakeTool("create_reminder", mutates: true));
        streamer.EnqueueText("I can set a reminder for 7pm if you like.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Can you remind me at 7pm?"));

        Assert.Equal(1, streamer.CallCount);
        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Null(done.UnverifiedClaim);
        Assert.Null(done.Text);
    }

    [Fact]
    public async Task A_reply_describing_a_real_proposal_is_not_second_guessed()
    {
        // "I've set up a reminder for you to confirm" is honest when a proposal exists this turn.
        var (_, chat, streamer) = Setup(new FakeTool("create_reminder", mutates: true));
        streamer
            .EnqueueToolCall("toolu_1", "create_reminder", """{"message":"Stretch"}""")
            .EnqueueText("I've added a reminder to stretch — it's waiting for you below.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Remind me to stretch"));

        Assert.Equal(2, streamer.CallCount); // the tool round and its reply — no correction
        Assert.Null(Assert.Single(events, e => e.Type == "done").UnverifiedClaim);
    }

    [Fact]
    public async Task A_done_claim_above_a_waiting_card_is_rewritten_as_a_proposal()
    {
        // 2026-09-22, qwen2.5:3b-instruct: "I've created a goal called Learn Rust" above a card
        // the user had not confirmed.
        var goals = new FakeTool("create_goal", mutates: true);
        var (_, chat, streamer) = Setup(goals);
        streamer
            .EnqueueToolCall("toolu_1", "create_goal", """{"title":"Learn Rust"}""")
            .EnqueueText("I've created a goal called Learn Rust for you.")
            .EnqueueText("Here is Learn Rust as a monthly goal — confirm the card below to add it.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Create a goal called Learn Rust"));

        Assert.Equal(3, streamer.CallCount);
        Assert.Contains(streamer.ReceivedTurns[2], t => t.Role == ChatRoles.User && t.Content.Contains("waiting for the user to tap"));
        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Equal("Here is Learn Rust as a monthly goal — confirm the card below to add it.", done.Text);
        Assert.Single(done.Pending!);
        // The card is right there: no "nothing was saved" note on top of it.
        Assert.Null(done.UnverifiedClaim);
        Assert.Empty(goals.Invocations);
    }

    [Fact]
    public async Task A_done_claim_that_survives_the_correction_is_taken_out_of_the_reply()
    {
        // Seen live: qwen2.5:3b-instruct repeated the claim word for word when asked to correct it.
        const string claim = "I've created a new goal titled Learn Rust for this month.";
        var (db, chat, streamer) = Setup(new FakeTool("create_goal", mutates: true));
        streamer
            .EnqueueToolCall("toolu_1", "create_goal", """{"title":"Learn Rust"}""")
            .EnqueueText(claim + " You'll see a card with the details.")
            .EnqueueText(claim + " You'll see a card with the details.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Create a goal called Learn Rust"));

        var done = Assert.Single(events, e => e.Type == "done");
        Assert.Equal(EmptyReplyGuard.ProposalOnly + " You'll see a card with the details.", done.Text);
        Assert.Null(done.UnverifiedClaim);
        Assert.Equal(done.Text, (await db.ChatMessages.SingleAsync(m => m.Role == ChatRoles.Assistant)).Content);
    }

    [Fact]
    public async Task A_reply_naming_items_that_do_not_exist_says_so_and_keeps_it()
    {
        var (db, chat, streamer) = Setup();
        streamer.EnqueueText("TASK-6 is already done, and TASK-7 is next.");

        var events = await CollectAsync(chat.StreamChatAsync(null, "Anything in the todo?"));

        Assert.Equal(["TASK-6", "TASK-7"], Assert.Single(events, e => e.Type == "done").UnknownItems);
        var stored = await db.ChatMessages.SingleAsync(m => m.Role == ChatRoles.Assistant);
        Assert.Equal("TASK-6,TASK-7", stored.UnknownItems);
        var detail = await chat.GetConversationAsync(stored.ConversationId.ToString());
        Assert.Equal(["TASK-6", "TASK-7"], detail!.Messages.Single(m => m.Role == ChatRoles.Assistant).UnknownItems);
    }

    [Fact]
    public async Task A_failed_correction_keeps_the_reply_and_flags_it_without_an_error()
    {
        var (db, chat, streamer) = Setup(new FakeTool("create_reminder", mutates: true));
        streamer.EnqueueText(FalseClaim);
        streamer.ThrowOnCall = (2, new AiStreamException("The provider is unavailable."));

        var events = await CollectAsync(chat.StreamChatAsync(null, "Remind me at 7pm"));

        Assert.DoesNotContain(events, e => e.Type == "error");
        var done = Assert.Single(events, e => e.Type == "done");
        Assert.True(done.UnverifiedClaim);
        Assert.Equal(FalseClaim, (await db.ChatMessages.SingleAsync(m => m.Role == ChatRoles.Assistant)).Content);
    }
}
