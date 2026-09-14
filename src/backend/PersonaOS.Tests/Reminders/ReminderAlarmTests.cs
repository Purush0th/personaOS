using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Reminders;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Reminders;

/// <summary>
/// Reminders fire from exact alarms on the phone, so the server's job is to keep those alarms in
/// step. These pin the messages that do it — a wrong key or a missing zone here fails silently on
/// the device, not in any log the server keeps.
/// </summary>
public class ReminderAlarmTests
{
    private static async Task<(TestDbContext Db, FakePushSender Push, ReminderService Service)> SetupAsync(
        bool registerDevice = true)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.GetOrCreateAsync();

        if (registerDevice)
        {
            db.DeviceTokens.Add(new DeviceToken { Token = "phone", Platform = DevicePlatforms.Android });
            await db.SaveChangesAsync();
        }

        var push = new FakePushSender();
        var publisher = new ReminderAlarmPublisher(db, push, config, NullLogger<ReminderAlarmPublisher>.Instance);
        return (db, push, new ReminderService(db, config, publisher));
    }

    [Fact]
    public async Task Creating_a_reminder_sends_its_schedule_to_the_phone()
    {
        var (_, push, service) = await SetupAsync();
        var due = DateTime.UtcNow.AddHours(2);

        var created = await service.CreateAsync(new CreateReminderRequest("Call mum", DueAtUtc: due));

        var sent = Assert.Single(push.SentData);
        Assert.Equal(ReminderPushMessages.Scheduled, sent["type"]);
        Assert.Equal(created.Id.ToString(), sent["reminderId"]);
        Assert.Equal("Call mum", sent["message"]);
        Assert.Equal("Friday", sent["title"]);
    }

    [Fact]
    public async Task The_due_time_is_sent_as_explicit_UTC()
    {
        // The phone reads this to set an exact alarm. The API's other timestamps omit the zone,
        // and the phone once parsed one of those as local time — five and a half hours out.
        var (_, push, service) = await SetupAsync();
        var due = new DateTime(2030, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        await service.CreateAsync(new CreateReminderRequest("Dentist", DueAtUtc: due));

        Assert.Equal("2030-03-04T05:06:07Z", push.SentData.Single()["dueAtUtc"]);
    }

    [Fact]
    public async Task Moving_a_reminder_reschedules_it()
    {
        var (_, push, service) = await SetupAsync();
        var created = await service.CreateAsync(new CreateReminderRequest("Stretch", DueAtUtc: DateTime.UtcNow.AddHours(1)));
        var moved = new DateTime(2031, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        await service.UpdateAsync(created.Id, new UpdateReminderRequest(null, DueAtUtc: moved, DueAtLocal: null));

        var last = push.SentData[^1];
        Assert.Equal(ReminderPushMessages.Scheduled, last["type"]);
        Assert.Equal("2031-01-01T09:00:00Z", last["dueAtUtc"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelling_or_deleting_removes_the_alarm(bool cancel)
    {
        // Without this the phone keeps the alarm and rings for a reminder the user already
        // called off — worse than a late reminder.
        var (_, push, service) = await SetupAsync();
        var created = await service.CreateAsync(new CreateReminderRequest("Gym", DueAtUtc: DateTime.UtcNow.AddHours(1)));

        if (cancel) await service.CancelAsync(created.Id);
        else await service.DeleteAsync(created.Id);

        var last = push.SentData[^1];
        Assert.Equal(ReminderPushMessages.Removed, last["type"]);
        Assert.Equal(created.Id.ToString(), last["reminderId"]);
    }

    [Fact]
    public async Task Saving_a_reminder_does_not_depend_on_push_being_set_up()
    {
        var (db, push, service) = await SetupAsync();
        push.IsConfigured = false;

        await service.CreateAsync(new CreateReminderRequest("Water plants", DueAtUtc: DateTime.UtcNow.AddHours(1)));

        Assert.Empty(push.SentData);
        Assert.Single(db.Reminders);
    }

    [Fact]
    public async Task A_push_outage_never_fails_the_reminder_itself()
    {
        // Best-effort by design: the reminder is saved, the due-time push is still the fallback,
        // and the phone resyncs every alarm on its next sign-in.
        var (db, push, service) = await SetupAsync();
        push.Succeeds = false;

        await service.CreateAsync(new CreateReminderRequest("Pay rent", DueAtUtc: DateTime.UtcNow.AddHours(1)));

        Assert.Single(db.Reminders);
    }

    [Fact]
    public async Task The_due_fallback_is_data_only_so_the_phone_can_skip_an_alarm_it_already_raised()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.GetOrCreateAsync();
        db.DeviceTokens.Add(new DeviceToken { Token = "phone", Platform = DevicePlatforms.Android });
        db.Reminders.Add(new Reminder
        {
            Message = "Stand-up",
            DueAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = ReminderStatuses.Pending,
        });
        await db.SaveChangesAsync();
        var push = new FakePushSender();

        await new ReminderDispatcher(db, push, config, NullLogger<ReminderDispatcher>.Instance).DispatchDueAsync();

        var sent = Assert.Single(push.SentData);
        Assert.Equal(ReminderPushMessages.Due, sent["type"]);
        Assert.Equal("Stand-up", sent["message"]);
        Assert.True(sent.ContainsKey("reminderId"));
    }
}
