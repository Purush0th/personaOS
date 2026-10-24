using PersonaOS.Application.Configuration;
using PersonaOS.Application.Updates;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// With "Install updates automatically" on, asks for a new release soon after it is published:
/// pushing a release updates the server without anyone touching it (owner, 2026-10-24). Off, it
/// does nothing; the Settings page and the banner still offer Update now.
/// </summary>
public class UpdateCheckService(
    IServiceProvider services,
    ILogger<UpdateCheckService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var config = await scope.ServiceProvider.GetRequiredService<IInstanceConfigService>().GetOrCreateAsync(stoppingToken);
                if (config.IsConfigured && config.AutoInstallUpdates
                    && await scope.ServiceProvider.GetRequiredService<IUpdateService>().InstallIfDueAsync(stoppingToken) is { } version)
                {
                    logger.LogInformation("Installing release {Version} automatically.", version);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Update check failed; will retry.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
