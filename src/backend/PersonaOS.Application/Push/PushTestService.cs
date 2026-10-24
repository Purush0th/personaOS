using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Push;

/// <summary>How one registered phone took the test notification.</summary>
/// <param name="Name">The name the phone registered with, or its platform when it gave none.</param>
/// <param name="Removed">Firebase said the phone's token is no longer valid, so it was forgotten.</param>
public sealed record PushTestDevice(string Name, bool Delivered, string? Error, bool Removed);

/// <summary>The outcome of a test notification: whether push is on, and each phone's result.</summary>
public sealed record PushTestResult(bool Configured, IReadOnlyList<PushTestDevice> Devices)
{
    public int Delivered => Devices.Count(d => d.Delivered);
}

public interface IPushTestService
{
    /// <summary>Sends a test notification to every registered phone.</summary>
    Task<PushTestResult> SendAsync(CancellationToken ct = default);
}

/// <summary>
/// Settings → Push notifications → Send test notification (owner, 2026-10-24: "we need to add test
/// functionality for the firebase"). Until now the only way to know push worked was to set a
/// reminder and wait. Firebase's answer per phone is shown as it is, and a token Firebase rejects
/// for good is removed, as the reminder sender does.
/// </summary>
public sealed class PushTestService(IAppDbContext db, IPushSender push, ILogger<PushTestService> logger) : IPushTestService
{
    public async Task<PushTestResult> SendAsync(CancellationToken ct = default)
    {
        if (!push.IsConfigured) return new PushTestResult(false, []);

        var devices = await db.DeviceTokens.ToListAsync(ct);
        if (devices.Count == 0) return new PushTestResult(true, []);

        var results = await push.SendAsync(
            [.. devices.Select(d => d.Token)],
            "PersonaOS test",
            "Push notifications work on this phone.",
            new Dictionary<string, string> { ["type"] = "push_test" },
            PushChannels.Reminders,
            tag: "push-test",
            ct: ct);

        var byToken = results.ToDictionary(r => r.Token);
        var report = new List<PushTestDevice>();
        foreach (var device in devices)
        {
            var result = byToken.GetValueOrDefault(device.Token);
            if (result?.TokenInvalid == true) db.DeviceTokens.Remove(device);
            report.Add(new PushTestDevice(
                device.DeviceName is { Length: > 0 } name ? name : device.Platform,
                result?.Success == true,
                result is null ? "No answer from Firebase." : result.Error,
                result?.TokenInvalid == true));
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Test notification delivered to {Delivered} of {Count} device(s).", report.Count(d => d.Delivered), report.Count);
        return new PushTestResult(true, report);
    }
}
