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
    /// <summary>
    /// Canonical project home. Fixed like the brand itself (see the PRD's brand model), and it
    /// must stay in step with the repo the update banner polls (web `updates.service.ts`).
    /// </summary>
    private const string ProjectUrl = "https://github.com/Purush0th/personaOS";

    private const string ReleasesUrl = ProjectUrl + "/releases";

    /// <summary>
    /// Facts about the product the assistant runs inside. Models otherwise invent them —
    /// qwen2.5 told a user to install the app from the App Store, which does not list it.
    /// This is deliberately provider-neutral: it lives here, in the Application layer, so
    /// every adapter sends the same grounding and no provider needs special-casing.
    /// </summary>
    private static void AppendProductGrounding(StringBuilder sb, InstanceConfig config)
    {
        sb.Append("\n\nAbout PersonaOS, the app you run inside — these are facts, use them ")
          .Append("instead of guessing:");
        sb.Append("\n- PersonaOS is open-source software the user self-hosts on their own machine. ")
          .Append("It is not a cloud service, has no sign-up, and there is no company account.");
        sb.Append("\n- It is NOT published in the Apple App Store or Google Play. Never tell the ")
          .Append("user to search an app store for it. The web dashboard runs in a browser, and ")
          .Append("the Android app is distributed as an APK from the project's GitHub releases.");
        // Give the real URL rather than only forbidding invention: told merely not to guess,
        // qwen2.5 still produced a confident link to a repository that does not exist.
        sb.Append("\n- The project is at ").Append(ProjectUrl).Append(" and its releases are at ")
          .Append(ReleasesUrl).Append(". Never give any other URL for PersonaOS; if you are not ")
          .Append("certain a link is one of these, do not write a link at all.");
        sb.Append("\n- Devices reach the instance over the user's own private network, such as ")
          .Append("Tailscale, not over the public internet.");
        sb.Append("\n- The user's data stays on their server. The only thing that leaves it is the ")
          .Append("text of this conversation, sent to the AI provider they configured.");
        sb.Append("\n- \"PersonaOS\" is the fixed product name; \"").Append(config.AssistantNickname)
          .Append("\" is the name this user chose for you. Do not claim to be any other product.");
        sb.Append("\n- If you are unsure about how PersonaOS works, say so and point the user at ")
          .Append("the project's README. Never invent installation steps, URLs, file names, ")
          .Append("settings or features. A short \"I don't know\" is better than a plausible guess.");
    }

    /// <summary>
    /// Lists the modules actually enabled on this instance, so the assistant does not offer
    /// features the admin has switched off (their endpoints and tools are gated anyway).
    /// </summary>
    private static void AppendCapabilities(StringBuilder sb, InstanceConfig config)
    {
        var names = new (string Key, string Label)[]
        {
            (InstanceConfig.Modules.Goals, "goals (yearly/quarterly/monthly)"),
            (InstanceConfig.Modules.Planner, "a daily planner"),
            (InstanceConfig.Modules.Reminders, "reminders"),
            (InstanceConfig.Modules.Docs, "stored documents you can read"),
            (InstanceConfig.Modules.Voice, "voice input and read-back"),
            (InstanceConfig.Modules.Proactive, "proactive morning and evening briefs"),
        };

        var enabled = names
            .Where(n => config.Features.TryGetValue(n.Key, out var on) && on)
            .Select(n => n.Label)
            .ToList();
        var disabled = names
            .Where(n => !config.Features.TryGetValue(n.Key, out var on) || !on)
            .Select(n => n.Label)
            .ToList();

        sb.Append("\n\nEnabled on this instance: ")
          .Append(enabled.Count == 0 ? "nothing beyond chat" : string.Join(", ", enabled))
          .Append('.');

        if (disabled.Count > 0)
        {
            sb.Append(" Switched off, so do not offer it: ").Append(string.Join(", ", disabled))
              .Append('.');
        }
    }

    public async Task<string> BuildAsync(CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);

        var sb = new StringBuilder();
        sb.Append("You are ").Append(config.AssistantNickname)
          .Append(", a private personal AI assistant for the user. ");
        sb.Append("You help manage their goals, daily planner, reminders, and answer questions. ");
        sb.Append("Be helpful, direct, and concise.");

        // Small local models tend to narrate actions they never took, and to print tool calls
        // as text instead of emitting them. Both mislead the user, so state the rules plainly.
        sb.Append("\n\nRules about your tools:");
        sb.Append("\n- Call a tool using the tool-calling mechanism. Never write a tool call as ")
          .Append("text and never print JSON describing one — the user sees your reply exactly ")
          .Append("as you write it, and text is not executed.");
        sb.Append("\n- Never say you have added, updated, deleted or scheduled something unless a ")
          .Append("tool result in this conversation confirms it. Without that confirmation, nothing ")
          .Append("was saved. Say what you are going to do, or actually call the tool.");
        sb.Append("\n- If no tool fits, answer from what you know and say plainly that you cannot do ")
          .Append("it. Do not invent a tool, a parameter, or a document name.");

        AppendProductGrounding(sb, config);
        AppendCapabilities(sb, config);

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
        var today = Common.UserClock.Today(config.TimeZone);
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

        if (config.Features.TryGetValue(InstanceConfig.Modules.Reminders, out var remindersOn) && remindersOn)
        {
            var upcoming = await db.Reminders.AsNoTracking()
                .Where(r => r.Status == ReminderStatuses.Pending && r.DueAtUtc >= DateTime.UtcNow)
                .OrderBy(r => r.DueAtUtc)
                .Take(5)
                .Select(r => new { r.Message, r.DueAtUtc })
                .ToListAsync(ct);
            if (upcoming.Count > 0)
            {
                sb.Append("\n\nUpcoming reminders (times in the user's zone):");
                foreach (var r in upcoming)
                {
                    sb.Append("\n- ")
                      .Append(Common.UserClock.ToLocal(r.DueAtUtc, config.TimeZone).ToString("yyyy-MM-dd HH:mm"))
                      .Append(" — ").Append(r.Message);
                }
            }
        }

        return sb.ToString();
    }
}
