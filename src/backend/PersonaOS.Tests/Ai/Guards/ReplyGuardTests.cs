using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Tests.Ai.Guards;

public class ReplyGuardTests
{
    private static ChatTurnState Turn() => new("test-model", new HashSet<string>(), ["create_reminder"]);

    private static ReplyDraft Draft(string text, ChatTurnState? turn = null, bool correction = false, bool interrupted = false) =>
        new(text, turn ?? Turn(), correction, interrupted);

    [Fact]
    public void Thinking_guard_removes_reasoning_and_keeps_the_answer()
    {
        var draft = Draft("<think>the user wants goals</think>You have two goals.");

        Assert.NotNull(new ThinkingGuard().Review(draft));
        Assert.Equal("You have two goals.", draft.Text);
        Assert.True(draft.Rewritten);
    }

    [Fact]
    public void Leaked_call_guard_removes_a_printed_tool_call()
    {
        var draft = Draft("""Done. {"name":"create_reminder","arguments":{"message":"x"}}""");

        Assert.NotNull(new LeakedToolCallGuard().Review(draft));
        Assert.Equal("Done.", draft.Text);
    }

    [Fact]
    public void Leaked_call_guard_says_what_happened_when_the_call_was_the_whole_reply()
    {
        var draft = Draft("""{"name":"create_reminder","arguments":{"message":"x"}}""");

        new LeakedToolCallGuard().Review(draft);

        Assert.Equal(LeakedToolCallGuard.NothingLeft, draft.Text);
    }

    [Fact]
    public void Leaked_call_guard_leaves_ordinary_json_alone()
    {
        var draft = Draft("""Your config is {"theme":"dark"}.""");

        Assert.Null(new LeakedToolCallGuard().Review(draft));
        Assert.False(draft.Rewritten);
    }

    [Fact]
    public void Preamble_guard_only_touches_a_correction()
    {
        const string text = "Sure, here is the corrected message:\n\nI can set that reminder if you like.";

        var first = Draft(text);
        Assert.Null(new CorrectionPreambleGuard().Review(first));

        var correction = Draft(text, correction: true);
        Assert.NotNull(new CorrectionPreambleGuard().Review(correction));
        Assert.Equal("I can set that reminder if you like.", correction.Text);
    }

    [Fact]
    public void Claim_guard_finds_a_change_nothing_made()
    {
        var draft = Draft("I've set a reminder for 7pm.");

        Assert.NotNull(new ClaimCheckGuard().Review(draft));
        Assert.NotNull(draft.UnbackedClaim);
    }

    [Fact]
    public void Claim_guard_trusts_the_wording_when_something_was_proposed()
    {
        var turn = Turn();
        turn.Proposals.Add(new ProposedAction("create_reminder", "{}"));
        var draft = Draft("I've proposed a reminder for 7pm.", turn);

        Assert.Null(new ClaimCheckGuard().Review(draft));
        Assert.Null(draft.UnbackedClaim);
    }

    [Fact]
    public void Empty_guard_explains_a_turn_spent_calling_tools()
    {
        var turn = Turn();
        turn.RecordExecution(new AiToolCall("1", "get_goals", "{}"), new AiToolResult("1", "[]"));
        var draft = Draft("  ", turn);

        new EmptyReplyGuard().Review(draft);

        Assert.Equal(EmptyReplyGuard.AfterTools, draft.Text);
    }

    [Fact]
    public void Empty_guard_asks_again_when_nothing_ran_at_all()
    {
        var draft = Draft(string.Empty);

        new EmptyReplyGuard().Review(draft);

        Assert.Equal(EmptyReplyGuard.NoAnswer, draft.Text);
    }

    [Fact]
    public void Empty_guard_points_at_the_card_when_a_correction_only_proposed()
    {
        var turn = Turn();
        turn.Proposals.Add(new ProposedAction("create_reminder", "{}"));
        var draft = Draft(string.Empty, turn, correction: true);

        new EmptyReplyGuard().Review(draft);

        Assert.Equal(EmptyReplyGuard.ProposalOnly, draft.Text);
    }

    [Fact]
    public void Empty_guard_leaves_an_empty_correction_for_the_loop_to_replace_with_the_first_reply()
    {
        var draft = Draft(string.Empty, correction: true);

        Assert.Null(new EmptyReplyGuard().Review(draft));
        Assert.Equal(string.Empty, draft.Text);
    }

    [Fact]
    public void Empty_guard_does_not_paper_over_a_broken_stream()
    {
        var draft = Draft(string.Empty, interrupted: true);

        Assert.Null(new EmptyReplyGuard().Review(draft));
    }

    [Fact]
    public void Pipeline_cleans_before_checking_claims_and_fills_what_is_left_empty()
    {
        // Only reasoning, which the thinking guard removes: nothing to claim, so the empty guard fills it.
        var draft = new ReplyPipeline(NullLogger<ReplyPipeline>.Instance)
            .Review(Draft("<think>I've set a reminder already</think>"));

        Assert.Null(draft.UnbackedClaim);
        Assert.Equal(EmptyReplyGuard.NoAnswer, draft.Text);
        Assert.True(draft.Rewritten);
    }
}
