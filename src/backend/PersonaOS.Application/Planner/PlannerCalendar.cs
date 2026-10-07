using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Planner;

/// <summary>
/// One event as it actually happens on one day: a one-off item, or one occurrence of a repeating
/// item with its own changes applied.
/// </summary>
/// <param name="Item">The item, or for a repeating event the series.</param>
/// <param name="OriginalDate">The day the series' rule put it on (the item's own day for a one-off).</param>
/// <param name="Date">The day it is on now (differs from <paramref name="OriginalDate"/> when it was moved).</param>
/// <param name="Start">Start time; null for an all-day event.</param>
/// <param name="End">End time as stored; null when none was set (see <see cref="PlannerCalendar.EffectiveEnd"/>).</param>
/// <param name="IsChanged">This occurrence differs from the series.</param>
public sealed record PlannerOccurrenceView(
    PlannerItem Item,
    DateOnly OriginalDate,
    DateOnly Date,
    string Title,
    string? Notes,
    TimeOnly? Start,
    TimeOnly? End,
    string Status,
    bool IsChanged)
{
    public bool IsRecurring => Item.Recurrence is not null;
}

/// <summary>
/// Works out what a set of planner items looks like over a range of days: expands the repeating
/// ones, applies the changes made to single occurrences, and drops the deleted ones. Pure, so the
/// service that reads and the dispatcher that sends notifications agree on what happens when.
/// </summary>
public static class PlannerCalendar
{
    /// <summary>
    /// The occurrences with a day in <paramref name="from"/> to <paramref name="to"/> (inclusive), by
    /// day then time of day (all-day last), then the order set by the user.
    /// </summary>
    /// <param name="changes">Changes to occurrences of these items that touch the range, whichever day they started on.</param>
    public static List<PlannerOccurrenceView> Expand(
        IEnumerable<PlannerItem> items, IEnumerable<PlannerOccurrence> changes, DateOnly from, DateOnly to)
    {
        var changesByItem = changes.ToLookup(c => c.ItemId);
        var result = new List<PlannerOccurrenceView>();

        foreach (var item in items)
        {
            if (item.Recurrence is null)
            {
                if (item.Date >= from && item.Date <= to)
                {
                    result.Add(new PlannerOccurrenceView(
                        item, item.Date, item.Date, item.Title, item.Notes, item.ScheduledTime, item.EndTime,
                        item.Status, IsChanged: false));
                }

                continue;
            }

            if (!RecurrenceRule.TryParse(item.Recurrence, out var rule, out _))
            {
                // A rule that cannot be read (edited by hand?) must not hide the event.
                if (item.Date >= from && item.Date <= to)
                {
                    result.Add(new PlannerOccurrenceView(
                        item, item.Date, item.Date, item.Title, item.Notes, item.ScheduledTime, item.EndTime,
                        PlannerItemStatuses.Planned, IsChanged: false));
                }

                continue;
            }

            var changed = changesByItem[item.Id].ToDictionary(c => c.OriginalDate);
            var originals = rule.Dates(item.Date, from, to).ToHashSet();
            // An occurrence moved into the range from outside it is not one the rule gives in the range.
            foreach (var change in changed.Values.Where(c => !c.IsCancelled && c.Date is DateOnly moved && moved >= from && moved <= to))
                originals.Add(change.OriginalDate);

            foreach (var original in originals.Order())
            {
                changed.TryGetValue(original, out var change);
                if (change is { IsCancelled: true }) continue;

                var date = change?.Date ?? original;
                if (date < from || date > to) continue;

                result.Add(Apply(item, original, date, change));
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
    /// Null for an all-day event.
    /// </summary>
    public static TimeOnly? EffectiveEnd(TimeOnly? start, TimeOnly? end)
    {
        if (start is not TimeOnly s) return null;
        if (end is TimeOnly e && e > s) return e;
        return WithinDay(s, PlannerEvents.DefaultDurationMinutes);
    }

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

    private static PlannerOccurrenceView Apply(PlannerItem item, DateOnly original, DateOnly date, PlannerOccurrence? change)
    {
        if (change is null)
        {
            return new PlannerOccurrenceView(
                item, original, date, item.Title, item.Notes, item.ScheduledTime, item.EndTime,
                PlannerItemStatuses.Planned, IsChanged: false);
        }

        // The time of day is changed as a pair (or not at all): AllDay says which.
        var (start, end) = change.AllDay switch
        {
            true => ((TimeOnly?)null, (TimeOnly?)null),
            false => (change.ScheduledTime, change.EndTime),
            _ => (item.ScheduledTime, item.EndTime),
        };

        return new PlannerOccurrenceView(
            item, original, date, change.Title ?? item.Title, change.Notes ?? item.Notes, start, end,
            change.Status ?? PlannerItemStatuses.Planned, IsChanged: true);
    }
}
