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
        unverifiedClaim
            ? ReplySentences.Without(content, s => ActionClaimDetector.FindClaim(s) is not null
                    || ActionClaimDetector.FindMemoryClaim(s) is not null
                    || ActionClaimDetector.FindProposalClaim(s) is not null)
                ?? content
            : content;
}
