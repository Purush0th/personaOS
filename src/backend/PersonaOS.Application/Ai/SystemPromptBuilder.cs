using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Ai;

public class SystemPromptBuilder(
    IInstanceConfigService configService,
    IAppDbContext db) : ISystemPromptBuilder
{
    private const string ProjectUrl = PersonaOsProject.Url;

    private const string ReleasesUrl = PersonaOsProject.ReleasesUrl;

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
            (InstanceConfig.Modules.Board, "a weekly sprint board (tasks with story points, planned on Sunday evenings)"),
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

    /// <summary>
    /// The sprint board in brief, plus how to run planning. Read straight from the database so
    /// building a prompt never advances the sprint cycle as a side effect.
    /// </summary>
    private async Task AppendBoardAsync(StringBuilder sb, InstanceConfig config, CancellationToken ct)
    {
        sb.Append("\n\nThe sprint board works like Scrum for one person. Goals are the epics; tasks ")
          .Append("(keys like TASK-7) sit under a goal or stand alone, and are sized in Fibonacci value ")
          .Append("points: 1, 2, 3, 5, 8, 13, 21. Work waits in the backlog, is pulled into a sprint ")
          .Append("(keys like SPRINT-2, with a name and start and end dates), and moves through This week ")
          .Append("(todo), In progress and Done. Sprints are created, started and completed by the user — ")
          .Append("nothing starts or closes on a timer — and only one runs at a time. Completing a sprint ")
          .Append("moves unfinished work to the next one. Adding to or removing from a sprint that has ")
          .Append("started is a scope change: allowed, but say so. Always refer to tasks, goals and ")
          .Append("sprints by key, never by list position.");
        sb.Append("\nWhen the user wants to plan or review a sprint:")
          .Append("\n1. Review: call get_sprint_report and get_plan. Say what was committed and completed, ")
          .Append("and what carried over, briefly and without judgement.")
          .Append("\n2. Estimate: for unestimated tasks, ask about complexity, effort and uncertainty, and ")
          .Append("suggest points by comparing with tasks already sized. Suggest splitting anything at 13 or more.")
          .Append("\n3. Capacity: ask whether the coming week is normal (travel, illness, busy work) and ")
          .Append("suggest committing about the recent velocity, adjusted for that. Say so if the plan is well above it.")
          .Append("\n4. Order: carried-over work and tasks under active goals first, then by value.")
          .Append("\nMake the changes with update_task and move_task; each becomes a card the user confirms.");

        var sprint = await db.Sprints.AsNoTracking()
            .Where(s => s.Status != SprintStatuses.Closed)
            .OrderBy(s => s.Status == SprintStatuses.Active ? 0 : 1).ThenBy(s => s.Number)
            .FirstOrDefaultAsync(ct);
        if (sprint is null) return;

        var tasks = await db.BoardTasks.AsNoTracking()
            .Where(t => t.SprintId == sprint.Id)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.Number, t.Title, t.Status, t.Points })
            .ToListAsync(ct);

        var ends = Common.UserClock.ToLocal(sprint.EndsAtUtc, config.TimeZone);
        var starts = Common.UserClock.ToLocal(sprint.StartsAtUtc, config.TimeZone);
        sb.Append("\n\n").Append(ItemKeys.Sprint(sprint.Number))
          .Append(sprint.Name is null ? string.Empty : $" “{sprint.Name}”").Append(' ')
          .Append(sprint.Status == SprintStatuses.Active
              ? $"is running until {ends:ddd d MMM HH:mm}"
              : $"is planned, not started, from {starts:ddd d MMM HH:mm}")
          .Append(": ").Append(tasks.Where(t => t.Status == BoardTaskStatuses.Done).Sum(t => t.Points ?? 0))
          .Append(" of ").Append(tasks.Sum(t => t.Points ?? 0)).Append(" points done.");

        var open = tasks.Where(t => t.Status != BoardTaskStatuses.Done).Take(12).ToList();
        foreach (var t in open)
        {
            sb.Append("\n- ").Append(ItemKeys.Task(t.Number)).Append(' ').Append(t.Title)
              .Append(" [").Append(t.Status).Append(", ")
              .Append(t.Points is int p ? $"{p} pts" : "unestimated").Append(']');
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
        // The model told the user to say "confirm", which does nothing: the card has buttons.
        sb.Append("\n- Anything that changes the user's data is not run when you call it. It becomes ")
          .Append("a card under your reply with Confirm and Discard buttons, and it runs only when ")
          .Append("they tap Confirm. Say what the card will do; never ask them to type or say ")
          .Append("\"confirm\", and never call the same tool twice waiting for an answer.");

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
            var activeGoals = await db.Goals.AsNoTracking()
                .Where(g => g.Status == GoalStatuses.Active)
                .OrderBy(g => g.PeriodStart).ThenBy(g => g.Number)
                .Select(g => new { g.Number, g.Title, g.PeriodType })
                .ToListAsync(ct);
            if (activeGoals.Count > 0)
            {
                // Keys, not positions: a model that saw a numbered list passed "3" as a goal id.
                sb.Append("\n\nThe user's active goals. Always refer to a goal by its key, never by its ")
                  .Append("position in a list:");
                foreach (var g in activeGoals)
                {
                    sb.Append("\n- ").Append(ItemKeys.Goal(g.Number)).Append(' ').Append(g.Title)
                      .Append(" (").Append(g.PeriodType).Append(')');
                }
            }
        }

        if (config.Features.TryGetValue(InstanceConfig.Modules.Board, out var boardOn) && boardOn)
        {
            await AppendBoardAsync(sb, config, ct);
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
