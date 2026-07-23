namespace PersonaOS.Application.Proactive;

/// <summary>Outcome of one proactive job execution.</summary>
public record ProactiveRunResult(string JobName, bool Ran, bool Pushed, string? Summary, string? Skipped = null);

public interface IProactiveService
{
    /// <summary>
    /// Runs any proactive job whose scheduled local time has passed today and
    /// that has not already run for that local date. Idempotent.
    /// </summary>
    Task<IReadOnlyList<ProactiveRunResult>> RunDueJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs one job immediately regardless of schedule, for testing and manual
    /// triggering. Still records the run so the scheduled pass won't repeat it.
    /// </summary>
    Task<ProactiveRunResult> RunJobAsync(string jobName, bool force = true, CancellationToken ct = default);
}
