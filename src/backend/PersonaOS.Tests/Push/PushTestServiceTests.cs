using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Push;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Push;

/// <summary>
/// Settings → Push notifications → Send test notification (owner, 2026-10-24): every registered
/// phone gets one, and the page shows how each took it.
/// </summary>
public class PushTestServiceTests
{
    private static async Task<(PushTestService Service, TestDbContext Db, FakePushSender Push)> Setup(params string[] tokens)
    {
        var db = TestDbContext.Create();
        foreach (var token in tokens) db.DeviceTokens.Add(new DeviceToken { Token = token, DeviceName = $"Phone {token}" });
        await db.SaveChangesAsync();
        var push = new FakePushSender();
        return (new PushTestService(db, push, NullLogger<PushTestService>.Instance), db, push);
    }

    [Fact]
    public async Task Every_registered_phone_gets_the_test()
    {
        var (service, _, push) = await Setup("a", "b");

        var result = await service.SendAsync();

        Assert.True(result.Configured);
        Assert.Equal(2, result.Delivered);
        Assert.Equal(["Phone a", "Phone b"], result.Devices.Select(d => d.Name));
        Assert.Equal("PersonaOS test", Assert.Single(push.SentTitles));
        Assert.Equal(PushChannels.Reminders, Assert.Single(push.SentChannels));
    }

    [Fact]
    public async Task A_phone_firebase_no_longer_knows_is_reported_and_forgotten()
    {
        var (service, db, push) = await Setup("a", "gone");
        push.InvalidTokens.Add("gone");

        var result = await service.SendAsync();

        var gone = Assert.Single(result.Devices, d => d.Name == "Phone gone");
        Assert.False(gone.Delivered);
        Assert.True(gone.Removed);
        Assert.Equal(["a"], await db.DeviceTokens.Select(d => d.Token).ToListAsync());
    }

    [Fact]
    public async Task Without_push_set_up_nothing_is_sent()
    {
        var (service, _, push) = await Setup("a");
        push.IsConfigured = false;

        var result = await service.SendAsync();

        Assert.False(result.Configured);
        Assert.Empty(result.Devices);
        Assert.Equal(0, push.SendCount);
    }

    [Fact]
    public async Task With_no_phone_registered_it_says_so()
    {
        var (service, _, push) = await Setup();

        var result = await service.SendAsync();

        Assert.True(result.Configured);
        Assert.Empty(result.Devices);
        Assert.Equal(0, push.SendCount);
    }
}
