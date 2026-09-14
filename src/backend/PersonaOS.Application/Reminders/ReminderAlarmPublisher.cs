using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Reminders;

/// <summary>
/// The data-only messages that keep a phone's alarms in step with the server's reminders.
///
/// Reminders are alarm-grade on the phone: they fire from an exact alarm scheduled on the device,
/// not from a push sent at the due time. A push can never be punctual — the dispatcher polls, and
/// Android's Doze holds messages back — while an alarm-clock alarm fires to the second and even
/// offline. So the server's job is to tell the phone WHEN, as soon as it knows, and the phone
/// takes it from there. The due-time message is only a fallback for a phone that missed the
/// schedule.
///
/// Each message carries what the phone needs to act without calling back: a background wake-up
/// has no signed-in session to fetch the reminder with. That means the reminder text passes
/// through FCM, as it already did in the notification this replaces.
/// </summary>
public static class ReminderPushMessages
{
    /// <summary>Create or move the alarm for a reminder.</summary>
    public const string Scheduled = "reminder_scheduled";

    /// <summary>Remove the alarm: the reminder was cancelled or deleted.</summary>
    public const string Removed = "reminder_removed";

    /// <summary>The reminder is due now. Raise it unless the phone's own alarm already has.</summary>
    public const string Due = "reminder_due";

    public static IReadOnlyDictionary<string, string> ForScheduled(Reminder reminder, string title) =>
        Payload(Scheduled, reminder, title);

    public static IReadOnlyDictionary<string, string> ForDue(Reminder reminder, string title) =>
        Payload(Due, reminder, title);

    public static IReadOnlyDictionary<string, string> ForRemoved(int reminderId) =>
        new Dictionary<string, string>
        {
            ["type"] = Removed,
            ["reminderId"] = reminderId.ToString(CultureInfo.InvariantCulture),
        };

    private static Dictionary<string, string> Payload(string type, Reminder reminder, string title) => new()
    {
        ["type"] = type,
        ["reminderId"] = reminder.Id.ToString(CultureInfo.InvariantCulture),
        // Round-trip format with an explicit Z. The API's other timestamps are serialised
        // without a zone, which the phone once misread as local time; this one is unambiguous.
        ["dueAtUtc"] = DateTime.SpecifyKind(reminder.DueAtUtc, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["title"] = title,
        ["message"] = reminder.Message,
    };
}

public interface IReminderAlarmPublisher
{
    /// <summary>Tells every registered device to schedule (or reschedule) this reminder's alarm.</summary>
    Task PublishScheduledAsync(Reminder reminder, CancellationToken ct = default);

    /// <summary>Tells every registered device to drop this reminder's alarm.</summary>
    Task PublishRemovedAsync(int reminderId, CancellationToken ct = default);
}

/// <summary>
/// Best-effort: a failure here is logged and swallowed, never thrown. Creating or cancelling a
/// reminder must not fail because FCM is unreachable — the reminder is saved, the due-time push
/// is still the fallback, and the phone resyncs every alarm on its next sign-in.
/// </summary>
public class ReminderAlarmPublisher(
    IAppDbContext db,
    IPushSender pushSender,
    Configuration.IInstanceConfigService configService,
    ILogger<ReminderAlarmPublisher> logger) : IReminderAlarmPublisher
{
    public async Task PublishScheduledAsync(Reminder reminder, CancellationToken ct = default)
    {
        var title = (await configService.GetOrCreateAsync(ct)).AssistantNickname;
        await PublishAsync(ReminderPushMessages.ForScheduled(reminder, title), ct);
    }

    public Task PublishRemovedAsync(int reminderId, CancellationToken ct = default) =>
        PublishAsync(ReminderPushMessages.ForRemoved(reminderId), ct);

    private async Task PublishAsync(IReadOnlyDictionary<string, string> payload, CancellationToken ct)
    {
        if (!pushSender.IsConfigured) return;

        try
        {
            var tokens = await db.DeviceTokens.Select(d => d.Token).ToListAsync(ct);
            if (tokens.Count == 0) return;

            var results = await pushSender.SendDataAsync(tokens, payload, ct);
            var failed = results.Count(r => !r.Success);
            if (failed > 0)
            {
                logger.LogWarning(
                    "Reminder alarm update ({Type}) reached {Ok} of {Total} device(s).",
                    payload["type"], results.Count - failed, results.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send reminder alarm update ({Type}).", payload["type"]);
        }
    }
}
