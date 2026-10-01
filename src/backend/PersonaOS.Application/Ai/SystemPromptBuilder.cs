using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Ai.Models;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Application.Memories;
using PersonaOS.Domain;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Assembles the system prompt from fragments. The wording lives in <c>.prompty</c> files
/// (<c>Ai/Prompts/Fragments</c>, overridable per installation, see <see cref="PromptLibrary"/>);
/// this class only decides which fragments apply and fills in their values from the instance's
/// configuration and the user's data.
///
/// Text the user wrote (the persona, "about me") is appended as it is and never rendered as a
/// template, so braces in it cannot be read as tags.
/// </summary>
public class SystemPromptBuilder(
    IInstanceConfigService configService,
    IAppDbContext db,
    IPromptLibrary prompts) : ISystemPromptBuilder
{
    /// <summary>How many open sprint tasks and upcoming reminders the prompt lists at most.</summary>
    private const int MaxSprintTasks = 12;
    private const int MaxReminders = 5;

    /// <summary>How many memories come with a message at most: enough to help, few enough to stay cheap.</summary>
    private const int MaxMemories = 8;

    private static readonly (string Key, string Label)[] ModuleLabels =
    [
        (InstanceConfig.Modules.Goals, "goals (yearly/quarterly/monthly)"),
        (InstanceConfig.Modules.Board, "a sprint board (tasks with value points, in weekly sprints)"),
        (InstanceConfig.Modules.Planner, "a daily planner"),
        (InstanceConfig.Modules.Reminders, "reminders"),
        (InstanceConfig.Modules.Memory, "a memory of what the user told you in earlier conversations"),
        (InstanceConfig.Modules.Voice, "voice input and read-back"),
        (InstanceConfig.Modules.Proactive, "proactive morning and evening briefs"),
    ];

    public async Task<string> BuildAsync(PromptRequest? request = null, CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var today = UserClock.Today(config.TimeZone);
        var sections = new List<string>();
        var model = ModelProfiles.For(config);
        // A small model gets the compact wording of the fragments that have one.
        var fragments = new Fragments(prompts, model.Prompt == PromptVariant.Compact ? "compact" : null);

        // Qwen3 skips its reasoning pass when it sees this; the native Ollama adapter also sends
        // think: false, which is the switch that measurably works.
        if (model.Thinks) sections.Add(ModelProfiles.NoThink);

        sections.Add(fragments.Render("identity", ("nickname", config.AssistantNickname)));
        sections.Add(fragments.Render("tool-rules"));
        sections.Add(fragments.Render("product",
            ("nickname", config.AssistantNickname),
            ("projectUrl", PersonaOsProject.Url),
            ("releasesUrl", PersonaOsProject.ReleasesUrl)));
        sections.Add(fragments.Render("modules",
            ("enabled", string.Join(", ", ModuleLabels.Where(m => config.IsEnabled(m.Key)).Select(m => m.Label))),
            ("disabled", string.Join(", ", ModuleLabels.Where(m => !config.IsEnabled(m.Key)).Select(m => m.Label)))));

        if (!string.IsNullOrWhiteSpace(config.PersonaTemplate)) sections.Add(config.PersonaTemplate.Trim());

        var profile = await db.UserProfile.AsNoTracking().FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(profile?.AboutMe)) sections.Add("About the user:\n" + profile.AboutMe.Trim());

        if (config.IsEnabled(InstanceConfig.Modules.Memory)) sections.Add(await MemoryAsync(fragments, config, request?.UserMessage, ct));

        // The mode's instructions come last, after the data, so they are what the model reads just
        // before the conversation: a small model follows the nearest instruction best.
        var mode = ChatModes.Parse(request?.Mode) ?? ChatModes.Chat;

        var now = UserClock.ToLocal(DateTime.UtcNow, config.TimeZone);
        sections.Add(fragments.Render("clock",
            ("timeZone", config.TimeZone),
            ("today", today.ToString("yyyy-MM-dd")),
            ("now", now.ToString("HH:mm")),
            ("tomorrow", today.AddDays(1).ToString("yyyy-MM-dd")),
            ("week", Enumerable.Range(0, 7)
                .Select(d => today.AddDays(d))
                .Select(d => $"{d.ToString("dddd yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}{(d == today ? " (today)" : d == today.AddDays(1) ? " (tomorrow)" : string.Empty)}")
                .ToList())));

        if (config.IsEnabled(InstanceConfig.Modules.Goals)) sections.Add(await GoalsAsync(fragments, ct));
        if (config.IsEnabled(InstanceConfig.Modules.Board)) sections.Add(await BoardAsync(fragments, config, ct));
        if (config.IsEnabled(InstanceConfig.Modules.Planner)) sections.Add(await PlannerAsync(fragments, today, ct));
        if (config.IsEnabled(InstanceConfig.Modules.Reminders)) sections.Add(await RemindersAsync(fragments, config, ct));
        if (mode != ChatModes.Chat)
        {
            sections.Add(fragments.Render("mode", ChatModes.All
                .Where(m => m != ChatModes.Chat)
                .Select(m => (m, (object?)(m == mode ? "yes" : null)))
                .ToArray()));
        }

        return string.Join("\n\n", sections.Where(s => s.Length > 0));
    }

    private async Task<string> GoalsAsync(Fragments fragments, CancellationToken ct)
    {
        var goals = await db.Goals.AsNoTracking()
            .Where(g => g.Status == GoalStatuses.Active)
            .OrderBy(g => g.PeriodStart).ThenBy(g => g.Number)
            .Select(g => new { g.Id, g.Number, g.Title, g.PeriodType, g.PeriodEnd, g.ParentGoalId })
            .ToListAsync(ct);
        if (goals.Count == 0) return string.Empty;

        // Parents first, each followed by its children, so the nesting reads off the list:
        // "GOAL-1 Get fit (year 2026)", "GOAL-2 Build a base (quarter Q4 2026, under GOAL-1)".
        var keys = goals.ToDictionary(g => g.Id, g => ItemKeys.Goal(g.Number));
        var children = goals.Where(g => g.ParentGoalId is int p && keys.ContainsKey(p)).ToLookup(g => g.ParentGoalId!.Value);
        var lines = new List<string>();
        void Add(int id)
        {
            var g = goals.First(x => x.Id == id);
            var under = g.ParentGoalId is int parent && keys.TryGetValue(parent, out var parentKey) ? $", under {parentKey}" : string.Empty;
            lines.Add($"{keys[g.Id]} {g.Title} ({g.PeriodType} {GoalCalendar.Label(g.PeriodType, g.PeriodEnd)}{under})");
            foreach (var child in children[g.Id]) Add(child.Id);
        }
        foreach (var root in goals.Where(g => g.ParentGoalId is not int p || !keys.ContainsKey(p))) Add(root.Id);

        return fragments.Render("goals", ("goals", lines));
    }

    /// <summary>
    /// The board rules, plus the running (or next planned) sprint in brief. Read straight from the
    /// database so building a prompt never advances the sprint cycle as a side effect.
    /// </summary>
    private async Task<string> BoardAsync(Fragments fragments, InstanceConfig config, CancellationToken ct)
    {
        var sprint = await db.Sprints.AsNoTracking()
            .Where(s => s.Status != SprintStatuses.Closed)
            .OrderBy(s => s.Status == SprintStatuses.Active ? 0 : 1).ThenBy(s => s.Number)
            .FirstOrDefaultAsync(ct);
        if (sprint is null) return fragments.Render("board", ("sprint", null), ("tasks", null));

        var tasks = await db.BoardTasks.AsNoTracking()
            .Where(t => t.SprintId == sprint.Id)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.Number, t.Title, t.Status, t.Points })
            .ToListAsync(ct);

        var when = sprint.Status == SprintStatuses.Active
            ? $"is running until {UserClock.ToLocal(sprint.EndsAtUtc, config.TimeZone):ddd d MMM HH:mm}"
            : $"is planned, not started, from {UserClock.ToLocal(sprint.StartsAtUtc, config.TimeZone):ddd d MMM HH:mm}";
        var name = sprint.Name is null ? string.Empty : $" “{sprint.Name}”";
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).Sum(t => t.Points ?? 0);
        var summary = $"{ItemKeys.Sprint(sprint.Number)}{name} {when}: {done} of {tasks.Sum(t => t.Points ?? 0)} points done.";

        var open = tasks
            .Where(t => t.Status != BoardTaskStatuses.Done)
            .Take(MaxSprintTasks)
            .Select(t => $"{ItemKeys.Task(t.Number)} {t.Title} [{t.Status}, {(t.Points is int p ? $"{p} pts" : "unestimated")}]")
            .ToList();

        return fragments.Render("board", ("sprint", summary), ("tasks", open));
    }

    private async Task<string> PlannerAsync(Fragments fragments, DateOnly today, CancellationToken ct)
    {
        var items = await db.PlannerItems.AsNoTracking()
            .Where(i => i.Date == today)
            .OrderBy(i => i.ScheduledTime == null)
            .ThenBy(i => i.ScheduledTime)
            .ThenBy(i => i.SortOrder)
            .Select(i => new { i.Title, i.Status, i.ScheduledTime })
            .ToListAsync(ct);

        return items.Count == 0
            ? string.Empty
            : fragments.Render("planner", ("items", items
                .Select(i => $"{(i.ScheduledTime is TimeOnly time ? time.ToString("HH:mm") + " " : string.Empty)}{i.Title} [{i.Status}]")
                .ToList()));
    }

    private async Task<string> RemindersAsync(Fragments fragments, InstanceConfig config, CancellationToken ct)
    {
        var upcoming = await db.Reminders.AsNoTracking()
            .Where(r => r.Status == ReminderStatuses.Pending && r.DueAtUtc >= DateTime.UtcNow)
            .OrderBy(r => r.DueAtUtc)
            .Take(MaxReminders)
            .Select(r => new { r.Message, r.DueAtUtc })
            .ToListAsync(ct);

        return upcoming.Count == 0
            ? string.Empty
            : fragments.Render("reminders", ("reminders", upcoming
                .Select(r => $"{UserClock.ToLocal(r.DueAtUtc, config.TimeZone):yyyy-MM-dd HH:mm} — {r.Message}")
                .ToList()));
    }

    /// <summary>
    /// How memories work, and those relevant to the message. The memory text is the user's own
    /// words, so it is passed as values and never rendered as a template.
    /// </summary>
    private async Task<string> MemoryAsync(Fragments fragments, InstanceConfig config, string? userMessage, CancellationToken ct)
    {
        var relevant = await new MemoryService(db).RelevantAsync(userMessage, MaxMemories, ct);
        return fragments.Render("memory",
            ("autoSave", config.MemoryAutoSave ? "on" : null),
            ("hasMemories", relevant.Count > 0 ? "yes" : null),
            ("memories", relevant.Select(m => $"[{m.Category}] {m.Content}").ToList()));
    }

    /// <summary>The library, fixed to the prompt variant this model gets.</summary>
    private readonly record struct Fragments(IPromptLibrary Library, string? Variant)
    {
        public string Render(string fragment, params (string Name, object? Value)[] values) =>
            Library.Render(fragment, values.ToDictionary(v => v.Name, v => v.Value), Variant);
    }
}
