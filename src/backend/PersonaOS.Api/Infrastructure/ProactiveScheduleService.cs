using PersonaOS.Application.Proactive;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Ticks the proactive scheduler so briefs go out without the app being open.
/// The service itself is idempotent per local day, so the tick interval only
/// affects punctuality, never duplication.
/// </summary>
public class ProactiveScheduleService(
    IServiceProvider services,
    ILogger<ProactiveScheduleService> logger) : BackgroundService
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
                var proactive = scope.ServiceProvider.GetRequiredService<IProactiveService>();
                var results = await proactive.RunDueJobsAsync(stoppingToken);

                foreach (var result in results.Where(r => r.Ran && r.Summary is not null))
                {
                    logger.LogInformation("Proactive job {Job} sent (pushed: {Pushed}).",
                        result.JobName, result.Pushed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Proactive scheduler pass failed; will retry.");
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
