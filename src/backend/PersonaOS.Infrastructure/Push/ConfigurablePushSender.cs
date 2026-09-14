using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Push;

namespace PersonaOS.Infrastructure.Push;

/// <summary>
/// The push sender the rest of the app sees, whose provider can be swapped while the API runs.
///
/// Push is configured by uploading a credential on the admin page, so it cannot be fixed at
/// startup the way the other adapters are. This holds the current provider — FCM, or nothing —
/// and replaces it when the admin uploads or removes a key. With nothing configured it reports
/// <see cref="IsConfigured"/> false, so due reminders stay pending instead of failing.
/// </summary>
public sealed class ConfigurablePushSender(ILoggerFactory loggerFactory)
    : IPushSender, IPushProviderConfigurator, IDisposable
{
    private readonly Lock _gate = new();

    // Read without the lock on the send path: a reference swap is atomic, so a send in flight
    // finishes on whichever sender it picked up.
    private volatile FcmPushSender? _current;

    public bool IsConfigured => _current is not null;

    public void Configure(string? serviceAccountJson)
    {
        // Build the new sender before taking the old one out, so a key that fails to load
        // leaves push exactly as it was rather than switching it off.
        var replacement = serviceAccountJson is null
            ? null
            : new FcmPushSender(serviceAccountJson, loggerFactory.CreateLogger<FcmPushSender>());

        FcmPushSender? previous;
        lock (_gate)
        {
            previous = _current;
            _current = replacement;
        }

        previous?.Dispose();
    }

    public Task<IReadOnlyList<PushResult>> SendAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var sender = _current;
        return sender is not null
            ? sender.SendAsync(tokens, title, body, data, ct)
            : Task.FromResult(NotConfigured(tokens));
    }

    public Task<IReadOnlyList<PushResult>> SendDataAsync(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct = default)
    {
        var sender = _current;
        return sender is not null
            ? sender.SendDataAsync(tokens, data, ct)
            : Task.FromResult(NotConfigured(tokens));
    }

    private static IReadOnlyList<PushResult> NotConfigured(IReadOnlyList<string> tokens) =>
        tokens.Select(t => new PushResult(t, Success: false, Error: "Push notifications are not configured.")).ToList();

    public void Dispose() => _current?.Dispose();
}

/// <summary>
/// Loads the stored push credential when the API starts, so push survives a restart without
/// the admin uploading the key again.
/// </summary>
public sealed class PushConfigLoader(IServiceScopeFactory scopes, ILogger<PushConfigLoader> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPushConfigService>().ApplyStoredAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A stored key that no longer loads — revoked, or encrypted under a keyring that was
            // lost — must not stop the whole instance from starting. Push stays off, and the
            // admin can upload the key again.
            logger.LogError(ex, "Could not load the stored push credential; push notifications are off.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
