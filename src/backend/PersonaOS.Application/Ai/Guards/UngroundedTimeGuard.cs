using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Asked "When is my haircut appointment?" with no haircut anywhere, the trained qwen2.5:3b answered
/// "Your last planned haircut is scheduled for today at 14:00" once in three (training run
/// 2026-11-06b). A clock time in an answer to a question must come from somewhere: what the user
/// said, what a tool returned this turn, the time now, or a time in their planner or reminders.
/// Sentences giving any other time are taken out; if nothing is left, the reply says it found nothing.
/// Questions the reply asks back ("tomorrow at 9am?") are left alone: they are examples, not facts.
/// </summary>
public sealed partial class UngroundedTimeGuard(IAppDbContext db) : IReplyGuard
{
    public const string NothingFound = "I can't find that in your planner or reminders.";

    [GeneratedRegex(@"(?<![\d:])(?<h>[01]?\d|2[0-3]):(?<m>[0-5]\d)(?:\s*(?<ap>[ap])\.?m\.?)?(?![\d])|(?<![\d:])(?<h>1[0-2]|0?[1-9])\s*(?<ap>[ap])\.?m\.?(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex ClockTime();

    [GeneratedRegex(@"^\s*(?:when|what|where|which|who|how|is|are|am|do|does|did|have|has|any|anything)\b|\?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Question();

    public string Name => "ungrounded-time";

    public async ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var turn = draft.Turn;
        if (turn.Proposals.Count > 0 || turn.UserNow is not DateTime now || turn.UserMessage is not { } message
            || !Question().IsMatch(message))
            return null;

        var stated = ReplySentences.Split(draft.Text)
            .Where(s => !s.TrimEnd().EndsWith('?'))
            .SelectMany(Times)
            .ToHashSet();
        if (stated.Count == 0) return null;

        var known = turn.EarlierUserMessages.Append(message).Concat(turn.Results).SelectMany(Times).ToHashSet();
        known.Add(now.Hour * 60 + now.Minute);
        // "2:00" and "14:00" are the same time to most people: either half of the day counts.
        bool Known(int t) => known.Contains(t) || known.Contains((t + 720) % 1440);
        if (stated.All(Known)) return null;

        await AddStoredTimesAsync(known, now, ct);
        var madeUp = stated.Where(t => !Known(t)).ToHashSet();
        if (madeUp.Count == 0) return null;

        var rest = ReplySentences.Without(draft.Text, s => !s.TrimEnd().EndsWith('?') && Times(s).Any(madeUp.Contains)) ?? draft.Text;
        draft.Rewrite(rest.Trim().Length > 0 ? rest : NothingFound);
        return "took out times nothing gave: " + string.Join(", ", madeUp.Order().Select(t => $"{t / 60:00}:{t % 60:00}"));
    }

    /// <summary>Every time of an event or a pending reminder, in the user's local time.</summary>
    private async Task AddStoredTimesAsync(HashSet<int> known, DateTime now, CancellationToken ct)
    {
        var items = await db.PlannerItems.Select(p => new { p.ScheduledTime, p.EndTime }).ToListAsync(ct);
        var moved = await db.PlannerOccurrences.Select(o => new { o.ScheduledTime, o.EndTime }).ToListAsync(ct);
        foreach (var time in items.SelectMany(i => new[] { i.ScheduledTime, i.EndTime }).Concat(moved.SelectMany(o => new[] { o.ScheduledTime, o.EndTime })))
            if (time is TimeOnly t) known.Add(t.Hour * 60 + t.Minute);

        // The user's offset from UTC, to the quarter hour, from the time now they sent.
        var offset = TimeSpan.FromMinutes(Math.Round((now - DateTime.UtcNow).TotalMinutes / 15) * 15);
        var dues = await db.Reminders.Where(r => r.Status == PersonaOS.Domain.Entities.ReminderStatuses.Pending)
            .Select(r => r.DueAtUtc).ToListAsync(ct);
        foreach (var due in dues)
        {
            var local = due + offset;
            known.Add(local.Hour * 60 + local.Minute);
        }
    }

    /// <summary>The clock times in <paramref name="text"/>, as minutes after midnight.</summary>
    private static IEnumerable<int> Times(string text)
    {
        foreach (Match m in ClockTime().Matches(text))
        {
            var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            if (m.Groups["ap"].Success)
            {
                if (hour > 12) continue;
                var pm = char.ToLowerInvariant(m.Groups["ap"].Value[0]) == 'p';
                hour = hour % 12 + (pm ? 12 : 0);
            }
            yield return hour * 60 + minute;
        }
    }
}
