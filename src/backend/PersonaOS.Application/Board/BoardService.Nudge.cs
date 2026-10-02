using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Board;

/// <summary>The board: the Sunday planning nudge, the one thing that happens on its own.</summary>
public partial class BoardService
{
    // ---------------------------------------------------------------- the Sunday nudge

    public async Task<IReadOnlyList<string>> RunRemindersAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var now = UtcNow();
        var local = UserClock.ToLocal(now, config.TimeZone);
        if (local.DayOfWeek != DayOfWeek.Sunday) return [];

        var nudgeAt = UserClock.ToUtc(local.Date.Add(SprintCalendar.NudgeTime.ToTimeSpan()), config.TimeZone);
        if (now < nudgeAt) return [];

        var localDate = DateOnly.FromDateTime(local);
        if (await db.ProactiveJobRuns.AnyAsync(r => r.JobName == PlanningNudgeJob && r.LocalDate == localDate, ct))
        {
            return [];
        }

        var active = await db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        var body = await NudgeTextAsync(active, now, ct);
        var pushed = body is not null && now - nudgeAt <= NudgeLateThreshold && await PushAsync(config, body, ct);

        db.ProactiveJobRuns.Add(new ProactiveJobRun
        {
            JobName = PlanningNudgeJob,
            LocalDate = localDate,
            Pushed = pushed,
            Summary = body ?? string.Empty,
        });
        await db.SaveChangesAsync(ct);

        return body is null ? [] : [pushed ? "sent the planning nudge" : "planning nudge not pushed"];
    }

    private async Task<string?> NudgeTextAsync(Sprint? active, DateTime now, CancellationToken ct)
    {
        if (active is null)
        {
            var planned = await db.Sprints.AsNoTracking()
                .Where(s => s.Status == SprintStatuses.Planned).OrderBy(s => s.Number).FirstOrDefaultAsync(ct);
            return planned is null
                ? "No sprint is running. Plan one for the week ahead?"
                : $"{ItemKeys.Sprint(planned.Number)} is planned and waiting. Start it when you are ready.";
        }

        var tasks = await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == active.Id).ToListAsync(ct);
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).Sum(t => t.Points ?? 0);
        var total = tasks.Sum(t => t.Points ?? 0);
        var name = active.Name is null
            ? ItemKeys.Sprint(active.Number)
            : $"{ItemKeys.Sprint(active.Number)} “{active.Name}”";

        return now >= active.EndsAtUtc
            ? $"{name} has reached its end date: {done} of {total} points done. Review it, complete it, and plan the next one."
            : $"{name}: {done} of {total} points done. Time for a look at the week ahead.";
    }

    private async Task<bool> PushAsync(InstanceConfig config, string body, CancellationToken ct)
    {
        if (!pushSender.IsConfigured) return false;
        var tokens = await db.DeviceTokens.Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0) return false;

        try
        {
            var results = await pushSender.SendAsync(
                tokens, config.AssistantNickname, body,
                new Dictionary<string, string> { ["type"] = "board" }, ct);
            return results.Any(r => r.Success);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Planning nudge push failed.");
            return false;
        }
    }
}
