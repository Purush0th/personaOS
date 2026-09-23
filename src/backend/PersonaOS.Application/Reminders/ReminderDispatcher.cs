using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Reminders;

public interface IReminderDispatcher
{
    /// <summary>
    /// Delivers every pending reminder that is now due. Returns how many were
    /// delivered. Safe to call repeatedly; hosted by a timer in the API.
    /// </summary>
    Task<int> DispatchDueAsync(CancellationToken ct = default);
}

/// <summary>
/// Pushes due reminders to the user's registered devices. Invalid tokens are pruned;
/// transient failures are retried on later passes until <see cref="MaxAttempts"/>.
/// </summary>
public class ReminderDispatcher(
    IAppDbContext db,
    IPushSender pushSender,
    IInstanceConfigService configService,
    ILogger<ReminderDispatcher> logger) : IReminderDispatcher
{
    /// <summary>Give up (mark failed) after this many unsuccessful delivery passes.</summary>
    private const int MaxAttempts = 5;

    public async Task<int> DispatchDueAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (!config.IsConfigured) return 0;
        if (!config.IsEnabled(InstanceConfig.Modules.Reminders))
            return 0;

        var now = DateTime.UtcNow;
        var due = await db.Reminders
            .Where(r => r.Status == ReminderStatuses.Pending && r.DueAtUtc <= now)
            .OrderBy(r => r.DueAtUtc)
            .Take(50)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        if (!pushSender.IsConfigured)
        {
            // No provider yet: leave reminders pending so they fire once push is set up.
            logger.LogInformation(
                "{Count} reminder(s) are due but push notifications are not configured.", due.Count);
            return 0;
        }

        var tokens = await db.DeviceTokens.Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0)
        {
            logger.LogInformation(
                "{Count} reminder(s) are due but no devices are registered.", due.Count);
            return 0;
        }

        var delivered = 0;
        var invalidTokens = new HashSet<string>();

        foreach (var reminder in due)
        {
            // Data-only, so the phone's own code decides what to show. Its exact alarm has
            // normally fired already; this is the fallback for a phone that missed the schedule
            // push, and the phone ignores it for any reminder it has already raised — otherwise
            // every reminder would alert twice.
            var results = await pushSender.SendDataAsync(
                tokens,
                ReminderPushMessages.ForDue(reminder, config.AssistantNickname),
                ct);

            foreach (var failed in results.Where(r => r.TokenInvalid))
            {
                invalidTokens.Add(failed.Token);
            }

            if (results.Any(r => r.Success))
            {
                reminder.Status = ReminderStatuses.Delivered;
                reminder.DeliveredAtUtc = DateTime.UtcNow;
                reminder.LastError = null;
                delivered++;
            }
            else
            {
                reminder.DeliveryAttempts++;
                reminder.LastError = results.FirstOrDefault(r => r.Error is not null)?.Error
                    ?? "Push delivery failed.";
                if (reminder.DeliveryAttempts >= MaxAttempts)
                {
                    reminder.Status = ReminderStatuses.Failed;
                    logger.LogWarning(
                        "Reminder {Id} failed after {Attempts} attempts: {Error}",
                        reminder.Id, reminder.DeliveryAttempts, reminder.LastError);
                }
            }

            reminder.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Prune tokens the provider rejected as permanently invalid.
        if (invalidTokens.Count > 0)
        {
            var stale = await db.DeviceTokens
                .Where(d => invalidTokens.Contains(d.Token))
                .ToListAsync(ct);
            db.DeviceTokens.RemoveRange(stale);
            logger.LogInformation("Removed {Count} invalid device token(s).", stale.Count);
        }

        await db.SaveChangesAsync(ct);
        return delivered;
    }
}
