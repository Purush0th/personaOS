using System.Globalization;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Planner;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Asked "Anything planned for tomorrow?", qwen2.5:3b listed today's "Read a book today" as
/// tomorrow's, two tries in three, after reading tomorrow's empty day (holdout3, 2026-11-02). Its
/// prompt lists today's items, and it copied them. When the message asks about one day that is not
/// today, the reply's sentences that name an item of today's and not of that day are taken out; if
/// nothing is left, the reply says what that day really holds.
/// </summary>
public sealed class OtherDayItemGuard(IAppDbContext db) : IReplyGuard
{
    public string Name => "other-day-item";

    public async ValueTask<string?> ReviewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var turn = draft.Turn;
        if (turn.Proposals.Count > 0 || !turn.ToolNames.Contains(PlannerRead) || turn.UserNow is not DateTime now) return null;
        if (WhenPhrase.Read(turn.UserMessage, now) is not { Day: DateOnly day }) return null;
        var today = DateOnly.FromDateTime(now);
        if (day == today) return null;

        var todays = await db.EventsAsync(today, today, ct: ct);
        if (todays.Count == 0) return null;
        var asked = (await db.EventsAsync(day, day, ct: ct)).Select(e => e.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wrong = todays.Select(e => e.Title)
            .Where(t => !asked.Contains(t, StringComparer.OrdinalIgnoreCase) && draft.Text.Contains(t, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (wrong.Count == 0) return null;

        var dayName = day.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
        var rest = ReplySentences.Without(draft.Text, s => wrong.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase))) ?? draft.Text;
        if (asked.Count == 0) draft.Rewrite($"Nothing is planned for {dayName}.");
        else draft.Rewrite(rest.Trim().Length > 0 ? rest : $"Planned for {dayName}: {string.Join(", ", asked)}.");
        return $"took today's {string.Join(", ", wrong)} out of an answer about {day:yyyy-MM-dd}";
    }

    private const string PlannerRead = "get_planner";
}
