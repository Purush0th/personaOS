using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Spots a reply that tells the user a change was made.
///
/// Models claim work they did not do. On 2026-09-11, asked to "remind me to submit the report at
/// 7pm today", qwen2.5 replied "I've set a reminder to remind you to submit the report at 19:00
/// today" and never called <c>create_reminder</c> — the reminders table stayed empty. Prompt rules
/// did not stop it, and a missing receipt is too weak a signal: a user reading a confident "I've
/// set a reminder" will not notice that nothing appeared beneath it. So the claim is detected in
/// code, and the chat loop acts on it.
///
/// Deliberately narrow, because a false positive costs a corrective model round (slow on a local
/// model) or a wrong warning. A sentence counts only when it is first-person (or a completed
/// passive: "your reminder has been set"), names a change verb, names something the assistant can
/// actually change, and is not a question, an offer or a proposal. It is a heuristic, English only,
/// and it only runs when no change was proposed that turn — the callers make that decision.
/// </summary>
public static partial class ActionClaimDetector
{
    /// <summary>Things the assistant's tools can change. A claim about anything else is ignored.</summary>
    [GeneratedRegex(@"\b(reminders?|alarms?|goals?|sub-?goals?|tasks?|to-?dos?|planner|plans?|schedule|calendar|items?|notes?|documents?|progress|appointments?|events?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DomainObject();

    /// <summary>
    /// "I've set", "I have created", "I just added", "I went ahead and scheduled", "I set",
    /// "I've now updated", and the future form "I'll create" / "I will add" / "I'm adding".
    /// Up to three words may sit between the subject and the verb ("I have now gone ahead and").
    /// </summary>
    [GeneratedRegex(@"\bi(?:'ve|’ve| have| had|'ll|’ll| will|'m|’m| am)?(?:\s+\w+){0,3}?\s+(set|created?|creating|add(?:ed|ing)?|schedul(?:ed|e|ing)|sav(?:ed|e|ing)|updat(?:ed|e|ing)|chang(?:ed|e|ing)|mark(?:ed|ing)?|delet(?:ed|e|ing)|remov(?:ed|e|ing)|cancel(?:l?ed|l?ing)?|mov(?:ed|e|ing)|reschedul(?:ed|e|ing)|logg?(?:ed|ing)?|record(?:ed|ing)?|book(?:ed|ing)?|put)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstPersonChange();

    /// <summary>"Your reminder has been set", "the goal is now created", "it's all set".</summary>
    [GeneratedRegex(@"\b(?:has|have)\s+been\s+(set|created|added|scheduled|saved|updated|marked|deleted|removed|cancell?ed|moved|rescheduled|logged|recorded|booked)\b|\b(?:is|are)\s+(?:now\s+|all\s+)(set|created|added|scheduled|saved|updated)\b|\bit'?s\s+all\s+set\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompletedPassive();

    /// <summary>
    /// Wording that turns a sentence into an offer, a question or a proposal rather than a claim:
    /// "I can set a reminder", "shall I create it?", "I propose adding", "once you confirm".
    /// </summary>
    [GeneratedRegex(@"\b(can|could|would|should|shall|may|might)\s+i\b|\bi\s+(can|could|would|might|may)\b|\b(would|do)\s+you\s+(like|want)\b|\bwant\s+me\s+to\b|\bif\s+you\b|\blet\s+me\s+know\b|\bpropos(e|ed|ing|al)\b|\bconfirm|\bonce\s+you\b|\bneed\s+your\b|\bnot\s+(yet|been)\b|\bhaven'?t\b|\bhave\s+not\b|\bdidn'?t\b|\bcan'?t\b|\bcannot\b|\bunable\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAClaim();

    [GeneratedRegex(@"(?<=[.!?])\s+|\n+")]
    private static partial Regex SentenceBreak();

    /// <summary>
    /// Returns the first sentence of <paramref name="reply"/> that claims a change was made, or null.
    /// </summary>
    public static string? FindClaim(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;

        foreach (var raw in SentenceBreak().Split(reply))
        {
            var sentence = raw.Trim();
            if (sentence.Length == 0 || sentence.EndsWith('?')) continue;
            if (NotAClaim().IsMatch(sentence)) continue;
            if (!DomainObject().IsMatch(sentence)) continue;

            if (FirstPersonChange().IsMatch(sentence) || CompletedPassive().IsMatch(sentence))
                return sentence;
        }

        return null;
    }
}
