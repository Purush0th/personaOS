using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Planner;

/// <summary>
/// One event as it actually happens: a one-off item, or one occurrence of a repeating item with its
/// own changes applied.
/// </summary>
/// <param name="Item">The item, or for a repeating event the series.</param>
/// <param name="OriginalDate">The day the series' rule put it on (the item's own day for a one-off).</param>
/// <param name="Date">The day it is on now (differs from <paramref name="OriginalDate"/> when it was moved).</param>
/// <param name="EndDate">Its last day: <paramref name="Date"/> unless it lasts several days.</param>
/// <param name="Start">Start time; null for an all-day event.</param>
/// <param name="End">End time as stored (on <paramref name="EndDate"/>); null when none was set (see <see cref="EffectiveEnd"/>).</param>
/// <param name="IsChanged">This occurrence differs from the series.</param>
public sealed record PlannerOccurrenceView(
    PlannerItem Item,
    DateOnly OriginalDate,
    DateOnly Date,
    DateOnly EndDate,
    string Title,
    string? Notes,
    TimeOnly? Start,
    TimeOnly? End,
    string Status,
    bool IsChanged)
{
    public bool IsRecurring => Item.Recurrence is not null;

    /// <summary>It goes on past the day it starts.</summary>
    public bool IsMultiDay => EndDate > Date;

    /// <summary>The end time to show and count with (see <see cref="PlannerCalendar.EffectiveEnd"/>).</summary>
    public TimeOnly? EffectiveEnd => PlannerCalendar.EffectiveEnd(Start, End, IsMultiDay);
}

/// <summary>
/// Works out what a set of planner items looks like over a range of days: expands the repeating
/// ones, applies the changes made to single occurrences, and drops the deleted ones. Pure, so the
/// service that reads and the dispatcher that sends notifications agree on what happens when.
/// </summary>
public static class PlannerCalendar
{
    /// <summary>
    /// The occurrences on any day from <paramref name="from"/> to <paramref name="to"/> (inclusive),
    /// including one over several days that started earlier, by first day then time of day (all-day
    /// last), then the order set by the user.
    /// </summary>
    /// <param name="changes">Changes to occurrences of these items that touch the range, whichever day they started on.</param>
    public static List<PlannerOccurrenceView> Expand(
        IEnumerable<PlannerItem> items, IEnumerable<PlannerOccurrence> changes, DateOnly from, DateOnly to)
    {
        var changesByItem = changes.ToLookup(c => c.ItemId);
        var result = new List<PlannerOccurrenceView>();

        foreach (var item in items)
        {
            var span = SpanDays(item);
            RecurrenceRule? rule = null;
            if (item.Recurrence is null || !RecurrenceRule.TryParse(item.Recurrence, out rule, out _))
            {
                // A rule that cannot be read (edited by hand?) must not hide the event.
                var once = new PlannerOccurrenceView(
                    item, item.Date, item.Date, item.Date.AddDays(span), item.Title, item.Notes, item.ScheduledTime,
                    item.EndTime, item.Recurrence is null ? item.Status : PlannerItemStatuses.Planned, IsChanged: false);
                if (Overlaps(once, from, to)) result.Add(once);
                continue;
            }

            var changed = changesByItem[item.Id].ToDictionary(c => c.OriginalDate);
            // An occurrence over several days that starts before the range still reaches into it.
            var originals = rule.Dates(item.Date, from.AddDays(-span), to).ToHashSet();
            // One moved or lengthened into the range from outside it is not one the rule gives there.
            foreach (var change in changed.Values.Where(c => !c.IsCancelled && (c.Date is not null || c.EndDate is not null)))
                originals.Add(change.OriginalDate);

            foreach (var original in originals.Order())
            {
                changed.TryGetValue(original, out var change);
                if (change is { IsCancelled: true }) continue;

                var view = Apply(item, original, span, change);
                if (Overlaps(view, from, to)) result.Add(view);
            }
        }

        return result
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Start is null)
            .ThenBy(o => o.Start)
            .ThenBy(o => o.Item.SortOrder)
            .ThenBy(o => o.Item.Id)
            .ToList();
    }

    /// <summary>
    /// A timed event's end: the one set, or the default length after the start, kept within the day.
    /// Over several days the end is on the last day, so any time will do (a missing one, which the
    /// service does not allow, reads as the end of that day). Null for an all-day event.
    /// </summary>
    public static TimeOnly? EffectiveEnd(TimeOnly? start, TimeOnly? end, bool multiDay = false)
    {
        if (start is not TimeOnly s) return null;
        if (multiDay) return end ?? new TimeOnly(23, 59);
        if (end is TimeOnly e && e > s) return e;
        return WithinDay(s, PlannerEvents.DefaultDurationMinutes);
    }

    /// <summary>How many days an event goes on past its first: 0 for an event within one day.</summary>
    public static int SpanDays(PlannerItem item) =>
        item.EndDate is DateOnly end && end > item.Date ? end.DayNumber - item.Date.DayNumber : 0;

    /// <summary>The time <paramref name="minutes"/> after <paramref name="start"/>, or the last minute of the day if that is past midnight.</summary>
    public static TimeOnly WithinDay(TimeOnly start, int minutes)
    {
        var total = start.Hour * 60 + start.Minute + minutes;
        return total >= 24 * 60 ? new TimeOnly(23, 59) : new TimeOnly(total / 60, total % 60);
    }

    /// <summary>The moment an occurrence starts, as a UTC instant; an all-day event counts from the reference hour.</summary>
    public static DateTime StartUtc(DateOnly date, TimeOnly? start, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(start ?? PlannerEvents.AllDayAlertReference);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A time inside the spring-forward gap does not exist locally: move to the hour after it.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static bool Overlaps(PlannerOccurrenceView view, DateOnly from, DateOnly to) =>
        view.Date <= to && view.EndDate >= from;

    private static PlannerOccurrenceView Apply(PlannerItem item, DateOnly original, int span, PlannerOccurrence? change)
    {
        if (change is null)
        {
            return new PlannerOccurrenceView(
                item, original, original, original.AddDays(span), item.Title, item.Notes, item.ScheduledTime,
                item.EndTime, PlannerItemStatuses.Planned, IsChanged: false);
        }

        // Moved, it lasts as many days as the series' occurrences do unless its own end says otherwise.
        var date = change.Date ?? original;
        var endDate = change.EndDate is DateOnly own && own >= date ? own : date.AddDays(span);

        // The time of day is changed as a pair (or not at all): AllDay says which.
        var (start, end) = change.AllDay switch
        {
            true => ((TimeOnly?)null, (TimeOnly?)null),
            false => (change.ScheduledTime, change.EndTime),
            _ => (item.ScheduledTime, item.EndTime),
        };

        return new PlannerOccurrenceView(
            item, original, date, endDate, change.Title ?? item.Title, change.Notes ?? item.Notes, start, end,
            change.Status ?? PlannerItemStatuses.Planned, IsChanged: true);
    }
}
