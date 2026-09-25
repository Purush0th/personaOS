using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Reasoning a thinking model wrote as content is its private notes, not an answer. Stored, it
/// would be shown, and fed back as history on the next turn.
/// </summary>
public sealed class ThinkingGuard : IReplyGuard
{
    public string Name => "thinking";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var stripped = ThinkingBlock.Strip(draft.Text);
        if (stripped == draft.Text) return ValueTask.FromResult<string?>(null);

        draft.Rewrite(stripped);
        return ValueTask.FromResult<string?>("removed reasoning written as content");
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

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var scrubbed = LeakedToolCallScrubber.Scrub(draft.Text, draft.Turn.ToolNames);
        if (scrubbed == draft.Text) return ValueTask.FromResult<string?>(null);

        // The whole reply was the leaked call: say so, rather than showing an empty bubble.
        draft.Rewrite(scrubbed.Length == 0 && draft.Text.Trim().Length > 0 ? NothingLeft : scrubbed);
        return ValueTask.FromResult<string?>("removed tool-call syntax or a stray code fence from the text");
    }
}

/// <summary>
/// Small models tell the user to "call create_goal" or explain which function answered, which
/// means nothing to them: tool names are internals, and the chat already shows which tools ran.
/// Any sentence naming one of this turn's tools is dropped. The prompt asks for this too; this is
/// the backstop for models that do not listen.
/// </summary>
public sealed class ToolNameGuard : IReplyGuard
{
    public string Name => "tool-name";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        if (draft.Turn.ToolNames.Count == 0) return ValueTask.FromResult<string?>(null);

        var names = new Regex(
            $@"\b({string.Join('|', draft.Turn.ToolNames.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase);
        if (ReplySentences.Without(draft.Text, names.IsMatch) is not { } text) return ValueTask.FromResult<string?>(null);

        draft.Rewrite(text);
        return ValueTask.FromResult<string?>("removed a sentence naming a tool");
    }
}

/// <summary>
/// Asked to correct a claim, models answer conversationally, and the user would read "Sure, here is
/// the corrected message:" above their reply. Only a correction is checked.
/// </summary>
public sealed class CorrectionPreambleGuard : IReplyGuard
{
    public string Name => "correction-preamble";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        if (!draft.IsCorrection) return ValueTask.FromResult<string?>(null);

        var stripped = CorrectionPreamble.Strip(draft.Text);
        if (stripped == draft.Text) return ValueTask.FromResult<string?>(null);

        draft.Rewrite(stripped);
        return ValueTask.FromResult<string?>("removed the preamble from a corrected reply");
    }
}

/// <summary>
/// A card has Confirm and Discard buttons, yet small models end with "Reply 'yes' to confirm" or
/// "just say yes and I'll add it", whatever the prompt says. Typing yes does nothing, so the
/// sentence is replaced with a pointer to the buttons. Only when the turn proposed something:
/// otherwise a yes-or-no question is ordinary conversation.
/// </summary>
public sealed partial class CardInstructionGuard : IReplyGuard
{
    public const string Pointer = "Use the Confirm or Discard button below.";

    /// <summary>
    /// Tells the user to answer in words: "reply 'yes'", "just say confirm", "respond with ok",
    /// "(yes/no)", "yes or no". The word must follow the verb directly, so "tell me if you want no
    /// reminders" is not read as one.
    /// </summary>
    [GeneratedRegex(@"\b(reply|respond|answer|type|say|write|send)(\s+(with|back))?\s*[""'“‘]?(yes|confirm|ok(ay)?)\b|\(\s*y(es)?\s*/\s*no?\s*\)|\byes\s+or\s+no\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForAWord();

    public string Name => "card-instruction";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        if (draft.Turn.Proposals.Count == 0) return ValueTask.FromResult<string?>(null);

        if (ReplySentences.Without(draft.Text, AsksForAWord().IsMatch) is not { } text) return ValueTask.FromResult<string?>(null);

        draft.Rewrite(text.Length == 0 ? Pointer : $"{text}\n\n{Pointer}");
        return ValueTask.FromResult<string?>("replaced an instruction to answer the card in words");
    }
}

/// <summary>
/// The reply says a change was made when it was not. With nothing proposed, that is "I've set a
/// reminder" with no tool call and an empty reminders table; the chat loop gives the model one
/// chance to make the change or take the claim back, and flags the reply if the claim survives.
/// With something proposed, only done-tense claims count ("I've created a goal called Learn Rust"
/// above a card still waiting), and the model is asked to describe the card instead.
/// </summary>
public sealed class ClaimCheckGuard : IReplyGuard
{
    public string Name => "claim-check";

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        draft.UnbackedClaim = draft.Turn.Proposals.Count == 0
            ? ActionClaimDetector.FindClaim(draft.Text)
            : ActionClaimDetector.FindDoneClaim(draft.Text);

        return ValueTask.FromResult(draft.UnbackedClaim is null
            ? null
            : draft.ClaimAwaitsConfirmation
                ? $"called a change done that is still waiting on a card: \"{draft.UnbackedClaim}\""
                : $"claimed a change nothing made: \"{draft.UnbackedClaim}\"");
    }
}

/// <summary>
/// Small models invent item keys: a 0.5B model announced TASK-6 as completed with no tool call,
/// and others answer from list positions. Every GOAL-n, TASK-n and SPRINT-n in the reply is looked
/// up, and the ones that do not exist are recorded, so the client can say the reply names things
/// that are not there. A check against the data, not a guess about the wording.
/// </summary>
public sealed partial class ItemReferenceGuard(IAppDbContext db) : IReplyGuard
{
    [GeneratedRegex(@"\b(GOAL|TASK|SPRINT)-(\d{1,6})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ItemKey();

    public string Name => "item-reference";

    public async ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var named = ItemKey().Matches(draft.Text)
            .Select(m => (Prefix: m.Groups[1].Value.ToUpperInvariant(), Number: int.Parse(m.Groups[2].Value)))
            .Distinct()
            .ToList();
        if (named.Count == 0) return null;

        var goals = await ExistingAsync(named, ItemKeys.GoalPrefix, db.Goals.Select(g => g.Number), ct);
        var tasks = await ExistingAsync(named, ItemKeys.TaskPrefix, db.BoardTasks.Select(t => t.Number), ct);
        var sprints = await ExistingAsync(named, ItemKeys.SprintPrefix, db.Sprints.Select(s => s.Number), ct);

        draft.UnknownItems = named
            .Where(k => !(k.Prefix switch
            {
                ItemKeys.GoalPrefix => goals,
                ItemKeys.TaskPrefix => tasks,
                _ => sprints,
            }).Contains(k.Number))
            .Select(k => $"{k.Prefix}-{k.Number}")
            .ToList();

        return draft.UnknownItems.Count == 0 ? null : $"named items that do not exist: {string.Join(", ", draft.UnknownItems)}";
    }

    /// <summary>Which of the named numbers with this prefix exist; one query per kind, and none when the reply names none.</summary>
    private static async Task<HashSet<int>> ExistingAsync(
        IReadOnlyList<(string Prefix, int Number)> named, string prefix, IQueryable<int> numbers, CancellationToken ct)
    {
        var wanted = named.Where(k => k.Prefix == prefix).Select(k => k.Number).ToList();
        return wanted.Count == 0 ? [] : (await numbers.Where(n => wanted.Contains(n)).ToListAsync(ct)).ToHashSet();
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

    public ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        if (draft.Text.Trim().Length > 0 || draft.Interrupted) return ValueTask.FromResult<string?>(null);

        var turn = draft.Turn;
        var filler = draft.IsCorrection
            ? turn.Proposals.Count > 0 ? ProposalOnly : null
            : turn.Receipts.Count > 0 ? AfterTools : NoAnswer;
        if (filler is null) return ValueTask.FromResult<string?>(null);

        draft.Rewrite(filler);
        return ValueTask.FromResult<string?>($"filled an empty reply after {turn.Receipts.Count} tool results");
    }
}

/// <summary>A reply taken apart into sentences, for guards that remove the ones they object to.</summary>
internal static partial class ReplySentences
{
    /// <summary>A sentence ends at ". ", "! " or "? ", and every line starts a new one.</summary>
    [GeneratedRegex(@"(?<=[.!?])[ \t]+|(?=\n)")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();

    /// <summary>
    /// The reply without the sentences <paramref name="drop"/> matches; null when nothing matched,
    /// empty when every sentence went. A dropped sentence gives back the line break it started
    /// with, so the paragraphs around it stay apart instead of running together.
    /// </summary>
    public static string? Without(string text, Func<string, bool> drop)
    {
        var result = new StringBuilder();
        var dropped = false;
        foreach (var sentence in SentenceBreak().Split(text))
        {
            string piece;
            if (drop(sentence))
            {
                dropped = true;
                piece = sentence[..(sentence.Length - sentence.TrimStart('\n').Length)];
            }
            else
            {
                piece = sentence.Trim(' ', '\t');
            }

            if (piece.Length == 0) continue;
            if (result.Length > 0 && result[^1] != '\n' && piece[0] != '\n') result.Append(' ');
            result.Append(piece);
        }

        return dropped ? ExtraBlankLines().Replace(result.ToString(), "\n\n").Trim() : null;
    }
}
