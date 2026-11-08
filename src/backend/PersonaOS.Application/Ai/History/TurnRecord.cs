using PersonaOS.Application.Ai.Guards;

namespace PersonaOS.Application.Ai.History;

/// <summary>
/// Stored replies as the model reads them back. A reply flagged for claiming a change nothing made
/// goes back without that claim: in chat dawdsabu (owner, 2026-10-11) qwen2.5:3b answered "I have
/// scheduled a reminder" three turns running, the correction round included, copying its own
/// earlier replies, and no reminder was ever proposed.
///
/// Notes of what each reply really did ("[Done in this reply: …]") were tried too and made it worse:
/// the model wrote such notes itself ("Reminder created: …") instead of calling the tool, 0 of 6
/// replays against 1 of 4 without them.
/// </summary>
public static class TurnRecord
{
    /// <summary>The reply without the sentences that claim a change when it was flagged for one; may come back empty.</summary>
    public static string ForModel(string content, bool unverifiedClaim) =>
        unverifiedClaim ? WithoutClaims(content) : content;

    /// <summary>Said when a claimed change was not made and nothing explains why.</summary>
    public const string NotMade = "I haven't made that change yet.";

    /// <summary>
    /// A reply that claimed a change nothing made, said honestly: the claims go, and the reply says
    /// the change was not made and offers to make it, or gives the reason it was refused. It
    /// replaces the amber "nothing was saved, ask again" note (owner, 2026-11-07: "handle it like a
    /// frontier model"), which left the false claim on screen with a warning under it.
    /// </summary>
    public static string Honest(string content, string? refusal)
    {
        var rest = WithoutClaims(content).Trim();
        var asks = rest.EndsWith('?');
        var said = refusal is { Length: > 0 }
            ? $"I couldn't do that: {refusal.TrimEnd()}"
            : asks ? NotMade : $"{NotMade} Want me to go ahead?";
        return rest.Length == 0 ? said : $"{said}\n\n{rest}";
    }

    private static string WithoutClaims(string content) =>
        ReplySentences.Without(content, s => ActionClaimDetector.FindClaim(s) is not null
                || ActionClaimDetector.FindMemoryClaim(s) is not null
                || ActionClaimDetector.FindProposalClaim(s) is not null)
            ?? content;
}
