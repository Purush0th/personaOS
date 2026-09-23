using Microsoft.Extensions.Logging;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>A finished reply on its way to being stored and shown, with what the guards found.</summary>
public sealed class ReplyDraft(string text, ChatTurnState turn, bool isCorrection = false, bool interrupted = false)
{
    public string Text { get; private set; } = text;

    public ChatTurnState Turn { get; } = turn;

    /// <summary>This is the model's second attempt, after the claim check asked it to correct itself.</summary>
    public bool IsCorrection { get; } = isCorrection;

    /// <summary>The stream broke part way; the text is whatever arrived before it did.</summary>
    public bool Interrupted { get; } = interrupted;

    /// <summary>The text no longer matches what streamed to the user, who must be sent the stored version.</summary>
    public bool Rewritten { get; private set; }

    /// <summary>A sentence saying something was changed when nothing was, found by <see cref="ClaimCheckGuard"/>.</summary>
    public string? UnbackedClaim { get; set; }

    public void Rewrite(string text)
    {
        if (text == Text) return;
        Text = text;
        Rewritten = true;
    }
}

/// <summary>Reviews a finished reply. Returns why it acted, or null when it had nothing to do.</summary>
public interface IReplyGuard
{
    /// <summary>Short name, used in logs and metrics.</summary>
    string Name { get; }

    string? Review(ReplyDraft draft);
}

/// <summary>
/// Every reply passes these, in order, before it is stored:
/// <list type="number">
/// <item><see cref="ThinkingGuard"/> removes reasoning a thinking model wrote as content.</item>
/// <item><see cref="LeakedToolCallGuard"/> removes tool calls printed as text.</item>
/// <item><see cref="CorrectionPreambleGuard"/> removes "Sure, here is the corrected message:".</item>
/// <item><see cref="ClaimCheckGuard"/> finds a claimed change nothing backs.</item>
/// <item><see cref="EmptyReplyGuard"/> says what happened when nothing is left to show.</item>
/// </list>
/// Cleaning comes before the claim check so it reads only what the user would, and the empty
/// check comes last because any of the others can leave nothing behind.
/// </summary>
public sealed class ReplyPipeline(ILogger<ReplyPipeline> logger)
{
    private readonly IReplyGuard[] _guards =
    [
        new ThinkingGuard(),
        new LeakedToolCallGuard(),
        new CorrectionPreambleGuard(),
        new ClaimCheckGuard(),
        new EmptyReplyGuard(),
    ];

    public ReplyDraft Review(ReplyDraft draft)
    {
        foreach (var guard in _guards)
        {
            if (guard.Review(draft) is { } reason) GuardTelemetry.Record(logger, guard.Name, draft.Turn.Model, reason);
        }

        return draft;
    }
}
