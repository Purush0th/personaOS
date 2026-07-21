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

        sb.Append("\n\nThe user's time zone is ").Append(config.TimeZone).Append('.');

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

        return sb.ToString();
    }
}
