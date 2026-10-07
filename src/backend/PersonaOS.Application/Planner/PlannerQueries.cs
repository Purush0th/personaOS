using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Planner;

/// <summary>
/// The one way to ask "what is on the planner between these days", shared by the planner service,
/// the assistant's prompt and the briefs, so none of them forgets that events repeat.
/// </summary>
public static class PlannerQueries
{
    /// <summary>
    /// The events on the days from <paramref name="from"/> to <paramref name="to"/> (inclusive), repeats
    /// expanded and single-occurrence changes applied, in the order they happen.
    /// </summary>
    /// <param name="withLinks">Also load each event's goal and board task (for display); not needed for counting or notifying.</param>
    public static async Task<List<PlannerOccurrenceView>> EventsAsync(
        this IAppDbContext db, DateOnly from, DateOnly to, bool withLinks = false, CancellationToken ct = default)
    {
        IQueryable<Domain.Entities.PlannerItem> query = db.PlannerItems.AsNoTracking();
        if (withLinks)
            query = query.Include(i => i.Goal).Include(i => i.Task).ThenInclude(t => t!.Goal);

        // A repeating series can reach into the range from any earlier start.
        var items = await query
            .Where(i => (i.Recurrence == null && i.Date >= from && i.Date <= to)
                        || (i.Recurrence != null && i.Date <= to))
            .ToListAsync(ct);

        var seriesIds = items.Where(i => i.Recurrence != null).Select(i => i.Id).ToList();
        var changes = seriesIds.Count == 0
            ? []
            : await db.PlannerOccurrences.AsNoTracking()
                .Where(c => seriesIds.Contains(c.ItemId)
                            && ((c.OriginalDate >= from && c.OriginalDate <= to)
                                || (c.Date != null && c.Date >= from && c.Date <= to)))
                .ToListAsync(ct);

        return PlannerCalendar.Expand(items, changes, from, to);
    }
}
