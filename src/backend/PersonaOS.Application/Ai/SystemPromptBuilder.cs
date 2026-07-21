using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai;

public class SystemPromptBuilder(
    IInstanceConfigService configService,
    IAppDbContext db) : ISystemPromptBuilder
{
    public async Task<string> BuildAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);

        var sb = new StringBuilder();
        sb.Append("You are ").Append(config.AssistantNickname)
          .Append(", a private personal AI assistant for the user. ");
        sb.Append("You help manage their goals, daily planner, reminders, and answer questions. ");
        sb.Append("Be helpful, direct, and concise.");

        if (!string.IsNullOrWhiteSpace(config.PersonaTemplate))
        {
            sb.Append("\n\n").Append(config.PersonaTemplate.Trim());
        }

        var profile = await db.UserProfile.AsNoTracking().FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(profile?.AboutMe))
        {
            sb.Append("\n\nAbout the user:\n").Append(profile.AboutMe.Trim());
        }

        // "Today" must be the user's local date, not the server's UTC date.
        var today = TodayFor(config.TimeZone);
        sb.Append("\n\nThe user's time zone is ").Append(config.TimeZone)
          .Append(", where today is ").Append(today.ToString("yyyy-MM-dd")).Append('.');

        if (config.Features.TryGetValue(InstanceConfig.Modules.Goals, out var goalsOn) && goalsOn)
        {
            var activeRoots = await db.Goals.AsNoTracking()
                .Where(g => g.ParentGoalId == null && g.Status == GoalStatuses.Active)
                .OrderBy(g => g.PeriodStart).ThenBy(g => g.Id)
                .Select(g => new { g.Title, g.PeriodType, g.Progress })
                .ToListAsync(ct);
            if (activeRoots.Count > 0)
            {
                sb.Append("\n\nThe user's active top-level goals (use the goal tools for details or changes):");
                foreach (var g in activeRoots)
                {
                    sb.Append("\n- ").Append(g.Title)
                      .Append(" (").Append(g.PeriodType).Append(", ~").Append(g.Progress).Append("% by own tracking)");
                }
            }
        }

        if (config.Features.TryGetValue(InstanceConfig.Modules.Planner, out var plannerOn) && plannerOn)
        {
            var todaysItems = await db.PlannerItems.AsNoTracking()
                .Where(i => i.Date == today)
                .OrderBy(i => i.ScheduledTime == null)
                .ThenBy(i => i.ScheduledTime)
                .ThenBy(i => i.SortOrder)
                .Select(i => new { i.Title, i.Status, i.ScheduledTime })
                .ToListAsync(ct);
            if (todaysItems.Count > 0)
            {
                sb.Append("\n\nToday's planner (use the planner tools to change it):");
                foreach (var item in todaysItems)
                {
                    sb.Append("\n- ");
                    if (item.ScheduledTime is TimeOnly time) sb.Append(time.ToString("HH:mm")).Append(' ');
                    sb.Append(item.Title).Append(" [").Append(item.Status).Append(']');
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>The user's local date; falls back to UTC if the zone id is unknown.</summary>
    private static DateOnly TodayFor(string timeZoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }
}
