using PersonaOS.Application.Reminders;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Ticks the reminder dispatcher on an interval so due reminders reach the user's
/// devices without the app being open. Failures are logged and retried on the next
/// tick — a bad pass must never take the host down.
/// </summary>
public class ReminderDispatchService(
    IServiceProvider services,
    ILogger<ReminderDispatchService> logger) : BackgroundService
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
                var dispatcher = scope.ServiceProvider.GetRequiredService<IReminderDispatcher>();
                var delivered = await dispatcher.DispatchDueAsync(stoppingToken);
                if (delivered > 0)
                {
                    logger.LogInformation("Delivered {Count} reminder(s).", delivered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder dispatch pass failed; will retry.");
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
