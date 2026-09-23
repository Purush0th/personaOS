using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai.Guards;

public class ReplyGuardTests
{
    private static ChatTurnState Turn() => new("test-model", new HashSet<string>(), ["create_reminder"]);

    private static ChatTurnState TurnWithCard()
    {
        var turn = Turn();
        turn.Proposals.Add(new ProposedAction("create_goal", """{"title":"Learn Rust"}"""));
        return turn;
    }

    private static ReplyDraft Draft(string text, ChatTurnState? turn = null, bool correction = false, bool interrupted = false) =>
        new(text, turn ?? Turn(), correction, interrupted);

    private static async Task<ReplyDraft> Review(IReplyGuard guard, ReplyDraft draft)
    {
        await guard.ReviewAsync(draft, default);
        return draft;
    }

    [Fact]
    public async Task Thinking_guard_removes_reasoning_and_keeps_the_answer()
    {
        var draft = await Review(new ThinkingGuard(), Draft("<think>the user wants goals</think>You have two goals."));

        Assert.Equal("You have two goals.", draft.Text);
        Assert.True(draft.Rewritten);
    }

    [Fact]
    public async Task Leaked_call_guard_removes_a_printed_tool_call()
    {
        var draft = await Review(new LeakedToolCallGuard(), Draft("""Done. {"name":"create_reminder","arguments":{"message":"x"}}"""));

        Assert.Equal("Done.", draft.Text);
    }

    [Fact]
    public async Task Leaked_call_guard_says_what_happened_when_the_call_was_the_whole_reply()
    {
        var draft = await Review(new LeakedToolCallGuard(), Draft("""{"name":"create_reminder","arguments":{"message":"x"}}"""));

        Assert.Equal(LeakedToolCallGuard.NothingLeft, draft.Text);
    }

    [Fact]
    public async Task Leaked_call_guard_leaves_ordinary_json_alone()
    {
        var draft = await Review(new LeakedToolCallGuard(), Draft("""Your config is {"theme":"dark"}."""));

        Assert.False(draft.Rewritten);
    }

    [Fact]
    public async Task Preamble_guard_only_touches_a_correction()
    {
        const string text = "Sure, here is the corrected message:\n\nI can set that reminder if you like.";

        Assert.False((await Review(new CorrectionPreambleGuard(), Draft(text))).Rewritten);
        Assert.Equal("I can set that reminder if you like.",
            (await Review(new CorrectionPreambleGuard(), Draft(text, correction: true))).Text);
    }

    [Theory]
    [InlineData("I'll add Learn Rust as a monthly goal. Reply 'yes' to confirm.")]
    [InlineData("I'll add Learn Rust as a monthly goal. Just say yes and it's done!")]
    [InlineData("I'll add Learn Rust as a monthly goal. Should I go ahead (yes/no)?")]
    [InlineData("I'll add Learn Rust as a monthly goal.\nPlease respond with confirm.")]
    public async Task Card_guard_replaces_an_instruction_to_answer_in_words(string text)
    {
        var draft = await Review(new CardInstructionGuard(), Draft(text, TurnWithCard()));

        Assert.StartsWith("I'll add Learn Rust as a monthly goal.", draft.Text);
        Assert.EndsWith(CardInstructionGuard.Pointer, draft.Text);
        Assert.DoesNotContain("yes", draft.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Card_guard_leaves_a_yes_or_no_question_alone_when_there_is_no_card()
    {
        // Without a card, asking the user to answer is ordinary conversation.
        var draft = await Review(new CardInstructionGuard(), Draft("Do you want a reminder too? Reply yes or no."));

        Assert.False(draft.Rewritten);
    }

    [Fact]
    public async Task Card_guard_does_not_mistake_an_ordinary_no_for_an_instruction()
    {
        var draft = await Review(new CardInstructionGuard(),
            Draft("I'll add it. Tell me if you want no reminders for it.", TurnWithCard()));

        Assert.False(draft.Rewritten);
    }

    [Fact]
    public async Task Claim_guard_finds_a_change_nothing_made()
    {
        var draft = await Review(new ClaimCheckGuard(), Draft("I've set a reminder for 7pm."));

        Assert.NotNull(draft.UnbackedClaim);
        Assert.False(draft.ClaimAwaitsConfirmation);
    }

    [Fact]
    public async Task Claim_guard_catches_a_done_claim_above_a_card_that_still_waits()
    {
        // 2026-09-22, qwen2.5:3b-instruct: said above an unconfirmed card.
        var draft = await Review(new ClaimCheckGuard(), Draft("I've created a goal called Learn Rust for you.", TurnWithCard()));

        Assert.Equal("I've created a goal called Learn Rust for you.", draft.UnbackedClaim);
        Assert.True(draft.ClaimAwaitsConfirmation);
    }

    [Theory]
    [InlineData("I've proposed a goal called Learn Rust.")]
    [InlineData("I'll create the Learn Rust goal once you confirm.")]
    [InlineData("I've prepared a card to add the Learn Rust goal.")]
    [InlineData("The Learn Rust goal is waiting below.")]
    public async Task Claim_guard_trusts_honest_wording_above_a_card(string text)
    {
        var draft = await Review(new ClaimCheckGuard(), Draft(text, TurnWithCard()));

        Assert.Null(draft.UnbackedClaim);
    }

    [Fact]
    public async Task Item_guard_names_keys_that_do_not_exist_and_accepts_real_ones()
    {
        var db = TestDbContext.Create();
        db.Goals.Add(new Goal { Number = 1, Title = "Ship", PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31) });
        db.BoardTasks.Add(new BoardTask { Number = 3, Title = "Real task" });
        await db.SaveChangesAsync();

        var draft = await Review(new ItemReferenceGuard(db),
            Draft("GOAL-1 is on track. TASK-3 is in progress, and task-6 is done. SPRINT-2 starts Sunday; TASK-3 again."));

        Assert.Equal(["TASK-6", "SPRINT-2"], draft.UnknownItems);
    }

    [Fact]
    public async Task Item_guard_does_nothing_when_the_reply_names_no_items()
    {
        var draft = await Review(new ItemReferenceGuard(TestDbContext.Create()), Draft("You have two goals this month."));

        Assert.Empty(draft.UnknownItems);
    }

    [Fact]
    public async Task Empty_guard_explains_a_turn_spent_calling_tools()
    {
        var turn = Turn();
        turn.RecordExecution(new AiToolCall("1", "get_goals", "{}"), new AiToolResult("1", "[]"));

        Assert.Equal(EmptyReplyGuard.AfterTools, (await Review(new EmptyReplyGuard(), Draft("  ", turn))).Text);
    }

    [Fact]
    public async Task Empty_guard_asks_again_when_nothing_ran_at_all()
    {
        Assert.Equal(EmptyReplyGuard.NoAnswer, (await Review(new EmptyReplyGuard(), Draft(string.Empty))).Text);
    }

    [Fact]
    public async Task Empty_guard_points_at_the_card_when_a_correction_only_proposed()
    {
        var draft = await Review(new EmptyReplyGuard(), Draft(string.Empty, TurnWithCard(), correction: true));

        Assert.Equal(EmptyReplyGuard.ProposalOnly, draft.Text);
    }

    [Fact]
    public async Task Empty_guard_leaves_an_empty_correction_for_the_loop_to_replace_with_the_first_reply()
    {
        var draft = await Review(new EmptyReplyGuard(), Draft(string.Empty, correction: true));

        Assert.Equal(string.Empty, draft.Text);
    }

    [Fact]
    public async Task Empty_guard_does_not_paper_over_a_broken_stream()
    {
        var draft = await Review(new EmptyReplyGuard(), Draft(string.Empty, interrupted: true));

        Assert.False(draft.Rewritten);
    }

    [Fact]
    public async Task Pipeline_cleans_before_checking_claims_and_fills_what_is_left_empty()
    {
        // Only reasoning, which the thinking guard removes: nothing to claim, so the empty guard fills it.
        var draft = await new ReplyPipeline(TestDbContext.Create(), NullLogger<ReplyPipeline>.Instance)
            .ReviewAsync(Draft("<think>I've set a reminder already</think>"), default);

        Assert.Null(draft.UnbackedClaim);
        Assert.Equal(EmptyReplyGuard.NoAnswer, draft.Text);
        Assert.True(draft.Rewritten);
    }
}
