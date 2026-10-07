using PersonaOS.Application.Planner;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Ticks the planner alert dispatcher on an interval so a phone is told an event is about to start
/// without the app being open. A failed pass is logged and retried on the next tick: it must never
/// take the host down.
/// </summary>
public class PlannerAlertService(
    IServiceProvider services,
    ILogger<PlannerAlertService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IPlannerAlertDispatcher>();
                var sent = await dispatcher.DispatchDueAsync(stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Sent {Count} planner alert(s).", sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Planner alert pass failed; will retry.");
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
