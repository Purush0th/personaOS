using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Planner;

public interface IPlannerAlertDispatcher
{
    /// <summary>
    /// Sends the notification of every event whose alert time has come and which has not started.
    /// Returns how many were sent. Safe to call repeatedly; hosted by a timer in the API.
    /// </summary>
    Task<int> DispatchDueAsync(CancellationToken ct = default);
}

/// <summary>
/// Notifies the phone shortly before a planner event starts, as an ordinary push notification (the
/// system draws it; it is not an alarm, which is what reminders are for).
///
/// An event is notified once per start time: <see cref="PlannerAlertLog"/> remembers it, and moving
/// the event gives it a new start and so a new notification. Events that are done or skipped, or
/// that have already begun, are not notified; a server that was down over the alert time still
/// sends it if the event is yet to start.
/// </summary>
public class PlannerAlertDispatcher(
    IAppDbContext db,
    IPushSender pushSender,
    IInstanceConfigService configService,
    TimeProvider time,
    ILogger<PlannerAlertDispatcher> logger) : IPlannerAlertDispatcher
{
    /// <summary>An alert with no lead time is sent at the start and is still worth sending this long after.</summary>
    private static readonly TimeSpan AtStartGrace = TimeSpan.FromMinutes(5);

    /// <summary>Records of sent notifications are kept this long, then dropped.</summary>
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(7);

    public async Task<int> DispatchDueAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (!config.IsConfigured || !config.IsEnabled(InstanceConfig.Modules.Planner)) return 0;

        var lead = await db.PlannerItems.AsNoTracking()
            .Where(i => i.NotifyBeforeMinutes != null)
            .Select(i => i.NotifyBeforeMinutes)
            .MaxAsync(ct);
        if (lead is null) return 0;

        var zone = UserClock.Zone(config.TimeZone);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);

        // Days that can hold an event whose alert is due: started up to a day ago (a late tick), or
        // starting within the longest lead from now. A day either side covers all-day events and zones.
        var from = DateOnly.FromDateTime(localNow).AddDays(-1);
        var to = DateOnly.FromDateTime(localNow.AddMinutes(lead.Value)).AddDays(1);
        var events = await db.EventsAsync(from, to, ct: ct);

        var due = new List<(PlannerOccurrenceView Event, DateTime StartUtc)>();
        foreach (var e in events)
        {
            if (e.Item.NotifyBeforeMinutes is not int minutes || e.Status != PlannerItemStatuses.Planned) continue;

            var startUtc = PlannerCalendar.StartUtc(e.Date, e.Start, zone);
            var alertAt = startUtc.AddMinutes(-minutes);
            var lastWorthwhile = minutes == 0 ? startUtc + AtStartGrace : startUtc;
            if (alertAt <= nowUtc && nowUtc < lastWorthwhile) due.Add((e, startUtc));
        }

        await PruneLogsAsync(nowUtc, ct);
        if (due.Count == 0) return 0;

        var itemIds = due.Select(d => d.Event.Item.Id).Distinct().ToList();
        var sent = (await db.PlannerAlertLogs.AsNoTracking()
                .Where(l => itemIds.Contains(l.ItemId))
                .Select(l => new { l.ItemId, l.OccurrenceDate, l.StartUtc })
                .ToListAsync(ct))
            .Select(l => (l.ItemId, l.OccurrenceDate, l.StartUtc.Ticks))
            .ToHashSet();
        due = due.Where(d => !sent.Contains((d.Event.Item.Id, d.Event.OriginalDate, d.StartUtc.Ticks))).ToList();
        if (due.Count == 0) return 0;

        if (!pushSender.IsConfigured)
        {
            logger.LogInformation("{Count} planner alert(s) are due but push notifications are not configured.", due.Count);
            return 0;
        }

        var tokens = await db.DeviceTokens.Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0)
        {
            logger.LogInformation("{Count} planner alert(s) are due but no devices are registered.", due.Count);
            return 0;
        }

        var delivered = 0;
        var invalidTokens = new HashSet<string>();
        var today = DateOnly.FromDateTime(localNow);
        foreach (var (e, startUtc) in due)
        {
            var results = await pushSender.SendAsync(
                tokens,
                e.Title,
                Body(e, e.Item.NotifyBeforeMinutes!.Value, today),
                new Dictionary<string, string>
                {
                    ["type"] = "planner_alert",
                    ["itemId"] = e.Item.Id.ToString(CultureInfo.InvariantCulture),
                    ["occurrenceDate"] = e.OriginalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["date"] = e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
                PushChannels.PlannerEvents,
                ct);

            foreach (var failed in results.Where(r => r.TokenInvalid)) invalidTokens.Add(failed.Token);

            if (results.Any(r => r.Success))
            {
                db.PlannerAlertLogs.Add(new PlannerAlertLog
                {
                    ItemId = e.Item.Id,
                    OccurrenceDate = e.OriginalDate,
                    StartUtc = startUtc,
                    SentAtUtc = nowUtc,
                });
                delivered++;
            }
            else
            {
                // Not marked sent: the next pass tries again until the event starts.
                logger.LogWarning(
                    "Planner alert for event {Id} on {Date} was not delivered: {Error}",
                    e.Item.Id, e.Date, results.FirstOrDefault(r => r.Error is not null)?.Error ?? "unknown error");
            }
        }

        if (invalidTokens.Count > 0)
        {
            var stale = await db.DeviceTokens.Where(d => invalidTokens.Contains(d.Token)).ToListAsync(ct);
            db.DeviceTokens.RemoveRange(stale);
            logger.LogInformation("Removed {Count} invalid device token(s).", stale.Count);
        }

        await db.SaveChangesAsync(ct);
        return delivered;
    }

    private async Task PruneLogsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc - LogRetention;
        var old = await db.PlannerAlertLogs.Where(l => l.SentAtUtc < cutoff).ToListAsync(ct);
        if (old.Count == 0) return;
        db.PlannerAlertLogs.RemoveRange(old);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "Starts in 30 minutes · 9:30 am", "Starts now · 9:30 am", "All day tomorrow", and for an event
    /// over several days "From today to Wed 14 Oct" or "Starts in 1 hour · 9:30 am · until Wed 14 Oct".
    /// </summary>
    public static string Body(PlannerOccurrenceView e, int minutesBefore, DateOnly today)
    {
        var until = e.IsMultiDay ? e.EndDate.ToString("ddd d MMM", CultureInfo.InvariantCulture) : null;
        if (e.Start is not TimeOnly start)
        {
            var when = e.Date == today ? "today"
                : e.Date == today.AddDays(1) ? "tomorrow"
                : "on " + e.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture);
            if (until is null) return $"All day {when}";
            return $"From {(when.StartsWith("on ", StringComparison.Ordinal) ? when[3..] : when)} to {until}";
        }

        var time = start.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
        var body = minutesBefore == 0 ? $"Starts now · {time}" : $"Starts in {Lead(minutesBefore)} · {time}";
        return until is null ? body : $"{body} · until {until}";
    }

    /// <summary>"30 minutes", "2 hours", "1 day", "2 weeks"; the largest unit that divides it evenly.</summary>
    public static string Lead(int minutes)
    {
        static string Unit(int count, string name) => count == 1 ? $"1 {name}" : $"{count} {name}s";

        if (minutes % (7 * 24 * 60) == 0) return Unit(minutes / (7 * 24 * 60), "week");
        if (minutes % (24 * 60) == 0) return Unit(minutes / (24 * 60), "day");
        if (minutes % 60 == 0) return Unit(minutes / 60, "hour");
        return Unit(minutes, "minute");
    }
}
