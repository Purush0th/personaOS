using PersonaOS.Application.Board;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Sends the Sunday planning nudge without the app being open. Sprints themselves start and stop
/// only when the user says so; this just reminds them on Sunday evening. The nudge is idempotent
/// per day, so the interval only affects punctuality.
/// </summary>
public class SprintScheduleService(
    IServiceProvider services,
    ILogger<SprintScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var config = await scope.ServiceProvider.GetRequiredService<IInstanceConfigService>()
                    .GetOrCreateAsync(stoppingToken);

                if (config.IsConfigured
                    && config.IsEnabled(InstanceConfig.Modules.Board))
                {
                    var events = await scope.ServiceProvider.GetRequiredService<IBoardService>()
                        .RunRemindersAsync(stoppingToken);
                    foreach (var evt in events) logger.LogInformation("Sprint reminder: {Event}.", evt);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sprint cycle pass failed; will retry.");
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
