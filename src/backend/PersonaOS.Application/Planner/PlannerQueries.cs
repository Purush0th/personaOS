using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Planner;

/// <summary>
/// The one way to ask "what is on the planner between these days", shared by the planner service,
/// the assistant's prompt and the briefs, so none of them forgets that events repeat or last
/// several days.
/// </summary>
public static class PlannerQueries
{
    /// <summary>
    /// The events on the days from <paramref name="from"/> to <paramref name="to"/> (inclusive), repeats
    /// expanded and single-occurrence changes applied, in the order they happen. An event over
    /// several days is included once if any of its days is in the range.
    /// </summary>
    /// <param name="withLinks">Also load each event's goal and board task (for display); not needed for counting or notifying.</param>
    public static async Task<List<PlannerOccurrenceView>> EventsAsync(
        this IAppDbContext db, DateOnly from, DateOnly to, bool withLinks = false, CancellationToken ct = default)
    {
        IQueryable<Domain.Entities.PlannerItem> query = db.PlannerItems.AsNoTracking();
        if (withLinks)
            query = query.Include(i => i.Goal).Include(i => i.Task).ThenInclude(t => t!.Goal);

        // A repeating series can reach into the range from any earlier start, and so can a one-off
        // that lasts several days.
        var items = await query
            .Where(i => (i.Recurrence == null && i.Date <= to && (i.Date >= from || (i.EndDate != null && i.EndDate >= from)))
                        || (i.Recurrence != null && i.Date <= to))
            .ToListAsync(ct);

        var series = items.Where(i => i.Recurrence != null).ToList();
        if (series.Count == 0) return PlannerCalendar.Expand(items, [], from, to);

        var seriesIds = series.Select(i => i.Id).ToList();
        // The earliest day an occurrence that reaches into the range can start on.
        var earliest = from.AddDays(-series.Max(PlannerCalendar.SpanDays));
        var changes = await db.PlannerOccurrences.AsNoTracking()
            .Where(c => seriesIds.Contains(c.ItemId)
                        && ((c.OriginalDate >= earliest && c.OriginalDate <= to)
                            || (c.Date != null && c.Date >= earliest && c.Date <= to)
                            || (c.EndDate != null && c.EndDate >= from && (c.Date ?? c.OriginalDate) <= to)))
            .ToListAsync(ct);

        return PlannerCalendar.Expand(items, changes, from, to);
    }
}
