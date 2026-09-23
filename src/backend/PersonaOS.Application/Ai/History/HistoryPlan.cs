using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai.History;

/// <summary>A rough token count: about four characters a token for English, erring high for code.</summary>
public static class TokenEstimate
{
    public static int Of(string? text) => string.IsNullOrEmpty(text) ? 0 : text.Length / 4 + 1;

    public static int Of(IEnumerable<AiToolDefinition> tools) =>
        tools.Sum(t => Of(t.Name) + Of(t.Description) + Of(t.InputSchemaJson));
}

/// <summary>Which stored messages go to the model, and which fall out of the window this turn.</summary>
/// <param name="Kept">Sent to the model, oldest first.</param>
/// <param name="ToSummarize">Leaving the window and not yet in the conversation's summary, oldest first.</param>
/// <param name="PromptTooLarge">The prompt and tools alone do not fit the context; the model will see it truncated.</param>
public sealed record HistoryPlan(IReadOnlyList<ChatMessage> Kept, IReadOnlyList<ChatMessage> ToSummarize, bool PromptTooLarge);

/// <summary>
/// Budgets history by tokens instead of a flat count of messages. A 0.5B model answered a fresh
/// thread correctly and produced nonsense in a long one: twenty turns of a confused conversation
/// crowded the prompt's rules out of a small context.
///
/// The system prompt, the tools and the new message are always sent whole. Room is kept for the
/// reply. Whatever is left is filled with the newest messages that fit, skipping replies that
/// were only a fallback, which teach the next turn nothing. When messages have to be dropped that
/// the conversation's summary does not cover yet, the window is cut back further, to
/// <see cref="AfterSummaryShare"/> of the room, so one summary serves several turns before the
/// next one is needed.
/// </summary>
public static class HistoryPlanner
{
    /// <summary>After summarising, history fills this share of its room, leaving space to grow.</summary>
    public const double AfterSummaryShare = 0.6;

    /// <summary>Room kept for the reply: a quarter of the context, at most this much.</summary>
    private const int MaxReplyReserve = 2_048;

    public static HistoryPlan Plan(
        IReadOnlyList<ChatMessage> messages,
        long? summarizedThroughId,
        int fixedTokens,
        int contextTokens)
    {
        var room = contextTokens - Math.Min(MaxReplyReserve, contextTokens / 4) - fixedTokens;
        var useful = messages.Where(m => !IsFiller(m)).ToList();

        var kept = NewestThatFit(useful, room);
        var dropped = useful.Take(useful.Count - kept.Count);
        var unsummarized = dropped.Where(m => summarizedThroughId is not long through || m.Id > through).ToList();

        if (unsummarized.Count > 0)
        {
            kept = NewestThatFit(useful, (int)(room * AfterSummaryShare));
            unsummarized = useful.Take(useful.Count - kept.Count)
                .Where(m => summarizedThroughId is not long through || m.Id > through)
                .ToList();
        }

        return new HistoryPlan(kept, unsummarized, PromptTooLarge: room <= 0);
    }

    /// <summary>The newest messages whose estimated size fits the room, oldest first.</summary>
    private static List<ChatMessage> NewestThatFit(List<ChatMessage> messages, int room)
    {
        var kept = new List<ChatMessage>();
        var used = 0;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            used += TokenEstimate.Of(messages[i].Content);
            if (used > room) break;
            kept.Add(messages[i]);
        }

        kept.Reverse();
        return kept;
    }

    /// <summary>A stored reply that was the app filling a gap, not the model saying anything.</summary>
    public static bool IsFiller(ChatMessage message) =>
        message.Role == ChatRoles.Assistant
        && message.Content is EmptyReplyGuard.AfterTools or EmptyReplyGuard.NoAnswer or LeakedToolCallGuard.NothingLeft;
}
