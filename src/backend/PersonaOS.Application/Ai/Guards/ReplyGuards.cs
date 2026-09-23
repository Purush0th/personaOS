namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Reasoning a thinking model wrote as content is its private notes, not an answer. Stored, it
/// would be shown, and fed back as history on the next turn.
/// </summary>
public sealed class ThinkingGuard : IReplyGuard
{
    public string Name => "thinking";

    public string? Review(ReplyDraft draft)
    {
        var stripped = ThinkingBlock.Strip(draft.Text);
        if (stripped == draft.Text) return null;

        draft.Rewrite(stripped);
        return "removed reasoning written as content";
    }
}

/// <summary>
/// A weak model may print a tool call as text instead of making it. Nothing ran, so the text goes:
/// otherwise the user is shown internals, and the next turn reads it back and copies the mistake.
/// </summary>
public sealed class LeakedToolCallGuard : IReplyGuard
{
    public const string NothingLeft =
        "I tried to use one of my tools but formed the request incorrectly, so nothing was changed. "
        + "Could you rephrase that?";

    public string Name => "leaked-tool-call";

    public string? Review(ReplyDraft draft)
    {
        var scrubbed = LeakedToolCallScrubber.Scrub(draft.Text, draft.Turn.ToolNames);
        if (scrubbed == draft.Text) return null;

        // The whole reply was the leaked call: say so, rather than showing an empty bubble.
        draft.Rewrite(scrubbed.Length == 0 && draft.Text.Trim().Length > 0 ? NothingLeft : scrubbed);
        return "removed tool-call syntax or a stray code fence from the text";
    }
}

/// <summary>
/// Asked to correct a claim, models answer conversationally, and the user would read "Sure, here is
/// the corrected message:" above their reply. Only a correction is checked.
/// </summary>
public sealed class CorrectionPreambleGuard : IReplyGuard
{
    public string Name => "correction-preamble";

    public string? Review(ReplyDraft draft)
    {
        if (!draft.IsCorrection) return null;

        var stripped = CorrectionPreamble.Strip(draft.Text);
        if (stripped == draft.Text) return null;

        draft.Rewrite(stripped);
        return "removed the preamble from a corrected reply";
    }
}

/// <summary>
/// The reply says a change was made, yet nothing was proposed this turn: "I've set a reminder" with
/// no tool call and an empty reminders table. The chat loop gives the model one chance to make the
/// change properly or take the claim back, and flags the reply if the claim survives. Skipped when
/// something was proposed: "I've proposed a reminder" is then an honest description.
/// </summary>
public sealed class ClaimCheckGuard : IReplyGuard
{
    public string Name => "claim-check";

    public string? Review(ReplyDraft draft)
    {
        if (draft.Turn.Proposals.Count > 0) return null;

        draft.UnbackedClaim = ActionClaimDetector.FindClaim(draft.Text);
        return draft.UnbackedClaim is null ? null : $"claimed a change nothing made: \"{draft.UnbackedClaim}\"";
    }
}

/// <summary>
/// Tools ran and the model never wrote a word: it spent the turn calling them. An empty bubble tells
/// the user nothing, so say what happened. A correction that came back empty is left empty when
/// there is nothing to show instead, and the chat loop keeps the first reply.
/// </summary>
public sealed class EmptyReplyGuard : IReplyGuard
{
    public const string AfterTools =
        "I looked that up but did not manage to write an answer. What the tools returned is listed "
        + "below — ask me again and I will summarise it.";

    public const string NoAnswer = "I did not manage to write an answer to that. Could you ask me again?";

    public const string ProposalOnly = "Here is the change I would make — confirm it below if it looks right.";

    public string Name => "empty-reply";

    public string? Review(ReplyDraft draft)
    {
        if (draft.Text.Trim().Length > 0 || draft.Interrupted) return null;

        var turn = draft.Turn;
        var filler = draft.IsCorrection
            ? turn.Proposals.Count > 0 ? ProposalOnly : null
            : turn.Receipts.Count > 0 ? AfterTools : NoAnswer;
        if (filler is null) return null;

        draft.Rewrite(filler);
        return $"filled an empty reply after {turn.Receipts.Count} tool results";
    }
}
