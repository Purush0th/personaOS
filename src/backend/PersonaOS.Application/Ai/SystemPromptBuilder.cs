using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;

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

        // Phase 2+: append an active-goals summary here.
        return sb.ToString();
    }
}
