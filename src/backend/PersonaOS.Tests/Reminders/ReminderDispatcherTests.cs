using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Reminders;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Reminders;

/// <summary>
/// Covers the delivery path that live testing cannot reach without a real push
/// provider: marking delivered, pruning invalid tokens, and retry-then-fail.
/// </summary>
public class ReminderDispatcherTests
{
    private static async Task<(TestDbContext Db, FakePushSender Push, ReminderDispatcher Dispatcher)> SetupAsync(
        params string[] deviceTokens)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.GetOrCreateAsync();

        foreach (var token in deviceTokens)
        {
            db.DeviceTokens.Add(new DeviceToken { Token = token, Platform = DevicePlatforms.Android });
        }
        await db.SaveChangesAsync();

        var push = new FakePushSender();
        var dispatcher = new ReminderDispatcher(db, push, config, NullLogger<ReminderDispatcher>.Instance);
        return (db, push, dispatcher);
    }

    private static Reminder DueReminder(string message = "Stand-up") => new()
    {
        Message = message,
        DueAtUtc = DateTime.UtcNow.AddMinutes(-1),
        Status = ReminderStatuses.Pending,
    };

    [Fact]
    public async Task Due_reminder_is_delivered_and_marked()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        db.Reminders.Add(DueReminder("Take a walk"));
        await db.SaveChangesAsync();

        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(1, delivered);
        Assert.Equal(1, push.SendCount);
        Assert.Contains("Take a walk", push.SentBodies);

        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatuses.Delivered, reminder.Status);
        Assert.NotNull(reminder.DeliveredAtUtc);
        Assert.Null(reminder.LastError);
    }

    [Fact]
    public async Task Delivered_reminder_is_not_sent_again()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        await dispatcher.DispatchDueAsync();
        var secondPass = await dispatcher.DispatchDueAsync();

        Assert.Equal(0, secondPass);
        Assert.Equal(1, push.SendCount);
    }

    [Fact]
    public async Task Future_cancelled_and_failed_reminders_are_skipped()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        db.Reminders.AddRange(
            new Reminder { Message = "Future", DueAtUtc = DateTime.UtcNow.AddHours(1), Status = ReminderStatuses.Pending },
            new Reminder { Message = "Cancelled", DueAtUtc = DateTime.UtcNow.AddMinutes(-5), Status = ReminderStatuses.Cancelled },
            new Reminder { Message = "Failed", DueAtUtc = DateTime.UtcNow.AddMinutes(-5), Status = ReminderStatuses.Failed });
        await db.SaveChangesAsync();

        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(0, delivered);
        Assert.Equal(0, push.SendCount);
    }

    [Fact]
    public async Task Invalid_tokens_are_pruned_and_valid_delivery_still_counts()
    {
        var (db, push, dispatcher) = await SetupAsync("good-token", "dead-token");
        push.InvalidTokens.Add("dead-token");
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(1, delivered);
        var remaining = await db.DeviceTokens.Select(d => d.Token).ToListAsync();
        Assert.Equal(["good-token"], remaining);
    }

    [Fact]
    public async Task Transient_failure_increments_attempts_and_keeps_pending()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        push.Succeeds = false;
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(0, delivered);
        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatuses.Pending, reminder.Status);
        Assert.Equal(1, reminder.DeliveryAttempts);
        Assert.Equal(push.FailureReason, reminder.LastError);
    }

    [Fact]
    public async Task Reminder_fails_after_five_attempts()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        push.Succeeds = false;
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        for (var i = 0; i < 5; i++)
        {
            await dispatcher.DispatchDueAsync();
        }

        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatuses.Failed, reminder.Status);
        Assert.Equal(5, reminder.DeliveryAttempts);

        // A failed reminder is no longer retried.
        var afterFailure = push.SendCount;
        await dispatcher.DispatchDueAsync();
        Assert.Equal(afterFailure, push.SendCount);
    }

    [Fact]
    public async Task Recovery_before_the_limit_still_delivers()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        push.Succeeds = false;
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        await dispatcher.DispatchDueAsync();
        await dispatcher.DispatchDueAsync();
        push.Succeeds = true;
        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(1, delivered);
        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatuses.Delivered, reminder.Status);
        Assert.Null(reminder.LastError);
    }

    [Fact]
    public async Task Unconfigured_push_leaves_reminders_pending_and_unattempted()
    {
        var (db, push, dispatcher) = await SetupAsync("token-a");
        push.IsConfigured = false;
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(0, delivered);
        Assert.Equal(0, push.SendCount);
        var reminder = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatuses.Pending, reminder.Status);
        Assert.Equal(0, reminder.DeliveryAttempts);
    }

    [Fact]
    public async Task No_registered_devices_leaves_reminders_pending()
    {
        var (db, push, dispatcher) = await SetupAsync();
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        var delivered = await dispatcher.DispatchDueAsync();

        Assert.Equal(0, delivered);
        Assert.Equal(0, push.SendCount);
        Assert.Equal(ReminderStatuses.Pending, (await db.Reminders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Disabled_reminders_feature_dispatches_nothing()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.UpdateAsync(c => c.Features[InstanceConfig.Modules.Reminders] = false);
        db.DeviceTokens.Add(new DeviceToken { Token = "token-a", Platform = DevicePlatforms.Android });
        db.Reminders.Add(DueReminder());
        await db.SaveChangesAsync();

        var push = new FakePushSender();
        var dispatcher = new ReminderDispatcher(db, push, config, NullLogger<ReminderDispatcher>.Instance);

        Assert.Equal(0, await dispatcher.DispatchDueAsync());
        Assert.Equal(0, push.SendCount);
    }
}
