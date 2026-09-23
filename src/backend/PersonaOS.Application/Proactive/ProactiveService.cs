using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Proactive;

public class ProactiveService(
    IAppDbContext db,
    IInstanceConfigService configService,
    IProactiveBriefComposer composer,
    IPushSender pushSender,
    TimeProvider timeProvider,
    ILogger<ProactiveService> logger) : IProactiveService
{
    /// <summary>
    /// A job whose time passed more than this long ago is skipped rather than
    /// fired late — nobody wants yesterday's morning brief when the server was
    /// off overnight and has just come back up.
    /// </summary>
    private static readonly TimeSpan LateThreshold = TimeSpan.FromHours(3);

    public async Task<IReadOnlyList<ProactiveRunResult>> RunDueJobsAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (!config.IsConfigured) return [];
        if (!config.IsEnabled(InstanceConfig.Modules.Proactive))
            return [];

        var localNow = UserClock.ToLocal(timeProvider.GetUtcNow().UtcDateTime, config.TimeZone);
        var localDate = DateOnly.FromDateTime(localNow);
        var results = new List<ProactiveRunResult>();

        foreach (var (jobName, scheduledTime) in ScheduledJobs(config))
        {
            if (scheduledTime is not TimeOnly time) continue;

            var due = localNow.TimeOfDay - time.ToTimeSpan();
            if (due < TimeSpan.Zero) continue;              // not yet
            if (due > LateThreshold) continue;              // too late to be useful

            var result = await RunIfNotAlreadyRunAsync(jobName, config, localDate, ct);
            if (result is not null) results.Add(result);
        }

        return results;
    }

    public async Task<ProactiveRunResult> RunJobAsync(
        string jobName, bool force = true, CancellationToken ct = default)
    {
        if (!ProactiveJobs.All.Contains(jobName))
            throw new ArgumentException($"Unknown proactive job '{jobName}'.", nameof(jobName));

        var config = await configService.GetOrCreateAsync(ct);
        var localDate = DateOnly.FromDateTime(
            UserClock.ToLocal(timeProvider.GetUtcNow().UtcDateTime, config.TimeZone));

        if (!force)
        {
            return await RunIfNotAlreadyRunAsync(jobName, config, localDate, ct)
                   ?? new ProactiveRunResult(jobName, Ran: false, Pushed: false, Summary: null,
                       Skipped: "already ran today");
        }

        return await ExecuteAsync(jobName, config, localDate, ct);
    }

    private async Task<ProactiveRunResult?> RunIfNotAlreadyRunAsync(
        string jobName, InstanceConfig config, DateOnly localDate, CancellationToken ct)
    {
        var alreadyRan = await db.ProactiveJobRuns
            .AnyAsync(r => r.JobName == jobName && r.LocalDate == localDate, ct);

        return alreadyRan ? null : await ExecuteAsync(jobName, config, localDate, ct);
    }

    private async Task<ProactiveRunResult> ExecuteAsync(
        string jobName, InstanceConfig config, DateOnly localDate, CancellationToken ct)
    {
        var summary = await composer.ComposeAsync(jobName, config, localDate, ct);

        // Record the run even when there is nothing to say, so an empty day
        // doesn't cause a retry on every tick.
        if (string.IsNullOrWhiteSpace(summary))
        {
            await RecordRunAsync(jobName, localDate, pushed: false, summary: string.Empty, ct);
            return new ProactiveRunResult(jobName, Ran: true, Pushed: false, Summary: null,
                Skipped: "nothing to report");
        }

        var pushed = await PushAsync(config, summary, ct);
        await SaveToConversationAsync(jobName, summary, ct);
        await RecordRunAsync(jobName, localDate, pushed, summary, ct);

        logger.LogInformation("Proactive job {Job} ran for {Date} (pushed: {Pushed}).",
            jobName, localDate, pushed);

        return new ProactiveRunResult(jobName, Ran: true, Pushed: pushed, Summary: summary);
    }

    private async Task<bool> PushAsync(InstanceConfig config, string summary, CancellationToken ct)
    {
        if (!pushSender.IsConfigured) return false;

        var tokens = await db.DeviceTokens.Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0) return false;

        var results = await pushSender.SendAsync(
            tokens,
            title: config.AssistantNickname,
            body: FirstLine(summary),
            data: new Dictionary<string, string> { ["type"] = "proactive" },
            ct);

        var invalid = results.Where(r => r.TokenInvalid).Select(r => r.Token).ToHashSet();
        if (invalid.Count > 0)
        {
            var stale = await db.DeviceTokens.Where(d => invalid.Contains(d.Token)).ToListAsync(ct);
            db.DeviceTokens.RemoveRange(stale);
        }

        return results.Any(r => r.Success);
    }

    /// <summary>
    /// Files the brief into a conversation so it shows up in chat history and the
    /// user can reply to it ("yes, move them to tomorrow") with full context.
    /// </summary>
    private async Task SaveToConversationAsync(string jobName, string summary, CancellationToken ct)
    {
        var title = jobName == ProactiveJobs.MorningBrief ? "Morning brief" : "Evening rollup";
        var conversation = new Conversation { Title = title };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRoles.Assistant,
            Content = summary,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task RecordRunAsync(
        string jobName, DateOnly localDate, bool pushed, string summary, CancellationToken ct)
    {
        db.ProactiveJobRuns.Add(new ProactiveJobRun
        {
            JobName = jobName,
            LocalDate = localDate,
            Pushed = pushed,
            Summary = summary,
        });
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<(string JobName, TimeOnly? Time)> ScheduledJobs(InstanceConfig config)
    {
        yield return (ProactiveJobs.MorningBrief, config.MorningBriefTime);
        yield return (ProactiveJobs.EveningRollup, config.EveningRollupTime);
    }

    /// <summary>Push notifications show one line; the full text lives in the chat entry.</summary>
    private static string FirstLine(string summary)
    {
        var line = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? summary;
        return line.Length > 180 ? line[..177] + "…" : line;
    }
}
