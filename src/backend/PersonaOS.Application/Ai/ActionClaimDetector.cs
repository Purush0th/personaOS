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
    /// Done, or claimed as done: "I've set", "I have created", "I just added",
    /// "I went ahead and scheduled", "I set", "I've now updated". Up to three words may sit
    /// between the subject and the verb ("I have now gone ahead and").
    /// </summary>
    [GeneratedRegex(@"\bi(?:'ve|’ve| have| had)?(?:\s+\w+){0,3}?\s+(set|created?|add(?:ed)?|schedul(?:ed|e)|sav(?:ed|e)|updat(?:ed|e)|chang(?:ed|e)|mark(?:ed)?|delet(?:ed|e)|remov(?:ed|e)|cancel(?:l?ed)?|mov(?:ed|e)|reschedul(?:ed|e)|logg?(?:ed)?|record(?:ed)?|book(?:ed)?|put)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstPersonDone();

    /// <summary>
    /// Still to come: "I'll create", "I will add", "I'm adding". Kept because models say
    /// "I'll create it" and then never call the tool — but see <see cref="AsksPermission"/>.
    /// </summary>
    [GeneratedRegex(@"\bi(?:'ll|’ll| will|'m|’m| am)(?:\s+\w+){0,3}?\s+(set|creat(?:e|ing)|add(?:ing)?|schedul(?:e|ing)|sav(?:e|ing)|updat(?:e|ing)|chang(?:e|ing)|mark(?:ing)?|delet(?:e|ing)|remov(?:e|ing)|cancel(?:l?ing)?|mov(?:e|ing)|reschedul(?:e|ing)|logg?(?:ing)?|record(?:ing)?|book(?:ing)?|put)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstPersonIntent();

    /// <summary>
    /// The reply asks the user whether to go ahead: "Would you like to proceed with these
    /// details?", "Shall I go ahead?", "Let me know if that looks right."
    /// </summary>
    [GeneratedRegex(@"\b(shall|should|can|may)\s+i\b|\b(would|do)\s+you\s+(like|want)\b|\bwant\s+me\s+to\b|\bproceed\b|\bgo\s+ahead\b|\blet\s+me\s+know\b|\bconfirm\b|\bsound\s+(good|right)\b|\blook\s+(good|right)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksPermission();

    /// <summary>"Your reminder has been set", "the goal is now created", "it's all set".</summary>
    [GeneratedRegex(@"\b(?:has|have)\s+been\s+(set|created|added|scheduled|saved|updated|marked|deleted|removed|cancell?ed|moved|rescheduled|logged|recorded|booked)\b|\b(?:is|are)\s+(?:now\s+|all\s+)(set|created|added|scheduled|saved|updated)\b|\bit'?s\s+all\s+set\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompletedPassive();

    /// <summary>
    /// Reporting what already exists rather than saying the assistant did something: "All tasks
    /// are either in progress or have been scheduled for future sprints", "as of the last check",
    /// "there are no reminders due". Read back from a tool, these are the honest answer to a
    /// question — and the warning on them is a false alarm the user has to learn to ignore.
    /// </summary>
    [GeneratedRegex(@"\bas\s+of\b|\bcurrently\b|\balready\b|\bthere\s+(is|are|was|were)\b|\b(all|both|each|none)\s+(of\s+)?(the\s+|your\s+)?\w+\s+(is|are|were|have|has)\b|\bso\s+far\b|\bat\s+the\s+moment\b|\byou\s+have\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportsState();

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

        var sentences = SentenceBreak().Split(reply);

        // "Sure, I'll add a new goal … Would you like to proceed with these details?" is a
        // proposal, not a report, and the amber warning on it was wrong. So a sentence that only
        // says what the assistant is *about to* do counts as a claim when the reply asks for
        // nothing. Anything claimed as already done still counts, question or no question.
        var awaitsTheUser = sentences.Any(s => AsksPermission().IsMatch(s));

        foreach (var raw in sentences)
        {
            var sentence = raw.Trim();
            if (sentence.Length == 0 || sentence.EndsWith('?')) continue;
            if (NotAClaim().IsMatch(sentence)) continue;
            if (!DomainObject().IsMatch(sentence)) continue;

            if (FirstPersonDone().IsMatch(sentence)) return sentence;

            // A passive sentence is the weakest signal, so it loses to any sign that the sentence
            // is describing the user's data rather than announcing a change to it.
            if (CompletedPassive().IsMatch(sentence) && !ReportsState().IsMatch(sentence))
                return sentence;

            if (!awaitsTheUser && FirstPersonIntent().IsMatch(sentence))
                return sentence;
        }

        return null;
    }
}
