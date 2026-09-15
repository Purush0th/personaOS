using PersonaOS.Application.Board;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Keeps the weekly sprint cycle moving without the app being open: closes the sprint on Sunday
/// at 18:00, sends the 19:00 planning nudge, and starts the next sprint at 20:00. The cycle is
/// idempotent, so the interval only affects punctuality.
/// </summary>
public class SprintScheduleService(
    IServiceProvider services,
    ILogger<SprintScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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
                    && config.Features.TryGetValue(InstanceConfig.Modules.Board, out var enabled) && enabled)
                {
                    var result = await scope.ServiceProvider.GetRequiredService<IBoardService>()
                        .RunCycleAsync(stoppingToken);
                    foreach (var evt in result.Events) logger.LogInformation("Sprint cycle: {Event}.", evt);
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
