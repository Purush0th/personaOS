using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Proactive;

public interface IProactiveBriefComposer
{
    /// <summary>Builds the message body for a job, or null when there is nothing worth saying.</summary>
    Task<string?> ComposeAsync(string jobName, InstanceConfig config, DateOnly localDate, CancellationToken ct = default);
}

/// <summary>
/// Builds proactive messages directly from the user's data. Composition is
/// deterministic and free — the assistant never needs the API to tell you what
/// is on today. (Optional model-written phrasing is a later enhancement; it must
/// not become a dependency, or a quota problem would silence the briefs.)
/// </summary>
public class ProactiveBriefComposer(IAppDbContext db) : IProactiveBriefComposer
{
    public async Task<string?> ComposeAsync(
        string jobName, InstanceConfig config, DateOnly localDate, CancellationToken ct = default) =>
        jobName switch
        {
            ProactiveJobs.MorningBrief => await ComposeMorningBriefAsync(config, localDate, ct),
            ProactiveJobs.EveningRollup => await ComposeEveningRollupAsync(config, localDate, ct),
            _ => null,
        };

    private async Task<string?> ComposeMorningBriefAsync(
        InstanceConfig config, DateOnly localDate, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("Good morning. Here's ").Append(localDate.ToString("dddd, d MMM")).Append('.');
        var hasContent = false;

        if (config.IsEnabled(InstanceConfig.Modules.Planner))
        {
            var items = await db.PlannerItems.AsNoTracking()
                .Where(i => i.Date == localDate && i.Status == PlannerItemStatuses.Planned)
                .OrderBy(i => i.ScheduledTime == null)
                .ThenBy(i => i.ScheduledTime)
                .ThenBy(i => i.SortOrder)
                .Select(i => new { i.Title, i.ScheduledTime })
                .ToListAsync(ct);

            if (items.Count > 0)
            {
                hasContent = true;
                sb.Append("\n\nOn your plan (").Append(items.Count).Append("):");
                foreach (var item in items)
                {
                    sb.Append("\n• ");
                    if (item.ScheduledTime is TimeOnly t) sb.Append(t.ToString("HH:mm")).Append(" — ");
                    sb.Append(item.Title);
                }
            }
        }

        if (config.IsEnabled(InstanceConfig.Modules.Reminders))
        {
            var dayStartUtc = Common.UserClock.ToUtc(localDate.ToDateTime(TimeOnly.MinValue), config.TimeZone);
            var dayEndUtc = Common.UserClock.ToUtc(localDate.ToDateTime(new TimeOnly(23, 59, 59)), config.TimeZone);

            var reminders = await db.Reminders.AsNoTracking()
                .Where(r => r.Status == ReminderStatuses.Pending
                            && r.DueAtUtc >= dayStartUtc && r.DueAtUtc <= dayEndUtc)
                .OrderBy(r => r.DueAtUtc)
                .Select(r => new { r.Message, r.DueAtUtc })
                .ToListAsync(ct);

            if (reminders.Count > 0)
            {
                hasContent = true;
                sb.Append("\n\nReminders today:");
                foreach (var reminder in reminders)
                {
                    sb.Append("\n• ")
                      .Append(Common.UserClock.ToLocal(reminder.DueAtUtc, config.TimeZone).ToString("HH:mm"))
                      .Append(" — ").Append(reminder.Message);
                }
            }
        }

        if (config.IsEnabled(InstanceConfig.Modules.Board))
        {
            // The morning nudge to plan the day: what the week's sprint still holds, so the day's
            // plan can be picked from it.
            var sprintTasks = await db.BoardTasks.AsNoTracking()
                .Where(t => t.Sprint != null && t.Sprint.Status == SprintStatuses.Active
                            && t.Status != BoardTaskStatuses.Done)
                .OrderBy(t => t.Status == BoardTaskStatuses.InProgress ? 0 : 1).ThenBy(t => t.SortOrder)
                .Select(t => new { t.Number, t.Title, t.Status })
                .ToListAsync(ct);

            if (sprintTasks.Count > 0)
            {
                hasContent = true;
                sb.Append("\n\nTime to plan your day. From the sprint:");
                foreach (var task in sprintTasks.Take(5))
                {
                    sb.Append("\n• ").Append(ItemKeys.Task(task.Number)).Append(' ').Append(task.Title);
                    if (task.Status == BoardTaskStatuses.InProgress) sb.Append(" (in progress)");
                }
                if (sprintTasks.Count > 5) sb.Append("\n• …and ").Append(sprintTasks.Count - 5).Append(" more");
            }
        }

        if (config.IsEnabled(InstanceConfig.Modules.Goals))
        {
            var active = await db.Goals.AsNoTracking()
                .Include(g => g.Tasks)
                .Where(g => g.Status == GoalStatuses.Active)
                .ToListAsync(ct);
            var stalled = active
                .Select(g => new { g.Title, Progress = GoalProgressCalculator.Compute(g.Progress, g.Tasks.ToList()) })
                .Where(g => g.Progress < 100)
                .OrderBy(g => g.Progress)
                .Take(2)
                .ToList();

            if (stalled.Count > 0)
            {
                hasContent = true;
                sb.Append("\n\nGoals worth a nudge:");
                foreach (var goal in stalled)
                {
                    sb.Append("\n• ").Append(goal.Title).Append(" (").Append(goal.Progress).Append("%)");
                }
            }
        }

        // Nothing scheduled and nothing outstanding: stay quiet rather than
        // sending a content-free notification every morning.
        return hasContent ? sb.ToString() : null;
    }

    private async Task<string?> ComposeEveningRollupAsync(
        InstanceConfig config, DateOnly localDate, CancellationToken ct)
    {
        if (!config.IsEnabled(InstanceConfig.Modules.Planner)) return null;

        var items = await db.PlannerItems.AsNoTracking()
            .Where(i => i.Date == localDate)
            .Select(i => new { i.Title, i.Status })
            .ToListAsync(ct);

        if (items.Count == 0) return null;

        var done = items.Count(i => i.Status == PlannerItemStatuses.Done);
        var open = items.Where(i => i.Status == PlannerItemStatuses.Planned).ToList();

        var sb = new StringBuilder();
        sb.Append("Evening rollup for ").Append(localDate.ToString("ddd, d MMM")).Append(": ")
          .Append(done).Append(" of ").Append(items.Count).Append(" done.");

        if (open.Count > 0)
        {
            sb.Append("\n\nStill open:");
            foreach (var item in open)
            {
                sb.Append("\n• ").Append(item.Title);
            }
            sb.Append("\n\nWant me to move these to tomorrow?");
        }
        else
        {
            sb.Append(" Everything on today's plan is closed out — nice work.");
        }

        return sb.ToString();
    }
}
