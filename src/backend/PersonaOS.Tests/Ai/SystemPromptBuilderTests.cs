using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The system prompt is the one place PersonaOS states its behavioural contract, and every
/// provider adapter receives it unchanged — so these assertions are what "model-agnostic"
/// actually means in practice. Grounding was added after qwen2.5 told a user to install the
/// app from the App Store, which does not list it.
/// </summary>
public class SystemPromptBuilderTests
{
    private static async Task<string> BuildAsync(
        Action<InstanceConfig>? configure = null,
        Action<TestDbContext>? seed = null,
        IPromptOverrideSource? overrides = null)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var instance = await config.GetOrCreateAsync();
        configure?.Invoke(instance);
        seed?.Invoke(db);
        await db.SaveChangesAsync();

        return await new SystemPromptBuilder(config, db, TestPrompts.Library(overrides)).BuildAsync();
    }

    /// <summary>A goal, a running sprint with open and done work, today's planner and a reminder.</summary>
    internal static void SeedEverything(TestDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Goals.Add(new Goal { Number = 1, Title = "Ship PersonaOS", PeriodType = GoalPeriods.Year, PeriodStart = today, PeriodEnd = today });
        var sprint = new Sprint
        {
            Number = 2, Name = "Paperwork", Status = SprintStatuses.Active,
            StartsAtUtc = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc),
        };
        db.Sprints.Add(sprint);
        db.BoardTasks.AddRange(
            new BoardTask { Number = 7, Title = "File taxes", Points = 5, Sprint = sprint, SortOrder = 1 },
            new BoardTask { Number = 8, Title = "Unsized", Sprint = sprint, SortOrder = 2, Status = BoardTaskStatuses.InProgress },
            new BoardTask { Number = 9, Title = "Finished", Points = 3, Sprint = sprint, SortOrder = 3, Status = BoardTaskStatuses.Done });
        db.PlannerItems.AddRange(
            new PlannerItem { Title = "Gym", Date = today, ScheduledTime = new TimeOnly(7, 30) },
            new PlannerItem { Title = "Read", Date = today });
        db.Reminders.Add(new Reminder { Message = "Call mum", DueAtUtc = DateTime.UtcNow.AddDays(1) });
    }

    [Fact]
    public async Task States_that_the_app_is_not_in_any_app_store()
    {
        var prompt = await BuildAsync();

        Assert.Contains("App Store", prompt);
        Assert.Contains("Google Play", prompt);
        Assert.Contains("Never tell the user to search an app store", prompt);
    }

    [Fact]
    public async Task Says_the_instance_is_self_hosted_and_private()
    {
        var prompt = await BuildAsync();

        Assert.Contains("self-hosts", prompt);
        Assert.Contains("data stays on their server", prompt);
    }

    [Fact]
    public async Task Supplies_the_real_project_url_rather_than_only_forbidding_invention()
    {
        // Told merely "don't invent URLs", qwen2.5 still produced a confident link to a
        // repository that does not exist. Stating the fact is what actually works.
        var prompt = await BuildAsync();

        Assert.Contains("https://github.com/Purush0th/personaOS", prompt);
        Assert.Contains("https://github.com/Purush0th/personaOS/releases", prompt);
        Assert.Contains("Never give any other URL for PersonaOS", prompt);
    }

    [Fact]
    public async Task Tells_the_model_to_admit_ignorance_rather_than_invent()
    {
        var prompt = await BuildAsync();

        Assert.Contains("I don't know", prompt);
        Assert.Contains("Never invent installation steps", prompt);
    }

    [Fact]
    public async Task Forbids_writing_tool_calls_as_text_and_claiming_unconfirmed_actions()
    {
        var prompt = await BuildAsync();

        Assert.Contains("Never write a tool call as text", prompt);
        Assert.Contains("unless a tool result in this conversation confirms it", prompt);
    }

    [Theory]
    [InlineData("qwen3:4b")]
    [InlineData("qwen3-4b-16k")]
    [InlineData("Qwen3:30b")]
    public async Task Turns_off_thinking_for_qwen3(string model)
    {
        // Measured on the owner's box: 626 generated tokens for a one-sentence answer, paid again
        // at every step of a tool turn — about 15 seconds per reply.
        var prompt = await BuildAsync(c => c.AiModel = model);

        Assert.StartsWith("/no_think", prompt);
    }

    [Theory]
    [InlineData("qwen2.5:3b-instruct")]
    [InlineData("claude-opus-4")]
    [InlineData("gemma3:270m")]
    public async Task Leaves_models_without_a_thinking_mode_alone(string model)
    {
        var prompt = await BuildAsync(c => c.AiModel = model);

        Assert.DoesNotContain("/no_think", prompt);
    }

    [Fact]
    public async Task Explains_how_the_confirmation_card_works()
    {
        // The model told the user to say "confirm", which does nothing — the card has buttons.
        var prompt = await BuildAsync();

        Assert.Contains("Confirm and Discard buttons", prompt);
        Assert.Contains("never ask them to type or say \"confirm\"", prompt);
    }

    [Fact]
    public async Task Carries_the_configured_nickname_and_the_fixed_product_name()
    {
        var prompt = await BuildAsync(c => c.AssistantNickname = "Juno");

        Assert.Contains("You are Juno", prompt);
        Assert.Contains("\"PersonaOS\" is the fixed product name", prompt);
        Assert.Contains("\"Juno\" is the name this user chose for you", prompt);
    }

    [Fact]
    public async Task Lists_enabled_modules_so_the_model_knows_what_it_can_offer()
    {
        var prompt = await BuildAsync();

        Assert.Contains("Enabled on this instance:", prompt);
        Assert.Contains("a daily planner", prompt);
    }

    [Fact]
    public async Task Names_a_disabled_module_as_switched_off()
    {
        // Docs off: the tool is gated anyway, but the model should not offer the feature.
        var prompt = await BuildAsync(c => c.Features = new Dictionary<string, bool>(c.Features)
        {
            [InstanceConfig.Modules.Docs] = false,
        });

        Assert.Contains("Switched off, so do not offer it:", prompt);
        var switchedOff = prompt[prompt.IndexOf("Switched off", StringComparison.Ordinal)..];
        Assert.Contains("stored documents", switchedOff);
    }

    [Fact]
    public async Task An_all_modules_off_instance_still_describes_itself_honestly()
    {
        var prompt = await BuildAsync(c => c.Features = InstanceConfig.DefaultFeatures()
            .ToDictionary(kv => kv.Key, _ => false));

        Assert.Contains("nothing beyond chat", prompt);
        // Grounding must survive regardless of which modules are on.
        Assert.Contains("Google Play", prompt);
    }

    [Fact]
    public async Task The_same_prompt_is_produced_regardless_of_configured_provider()
    {
        // The contract is model-agnostic by construction: nothing in the prompt varies with
        // provider or model. If this ever fails, behaviour has started to diverge per provider.
        var anthropic = await BuildAsync(c =>
        {
            c.AiProvider = InstanceConfig.Providers.Anthropic;
            c.AiModel = "claude-opus-4-8";
        });
        var local = await BuildAsync(c =>
        {
            c.AiProvider = InstanceConfig.Providers.OpenAiCompatible;
            c.AiModel = "qwen2.5:latest";
            c.AiBaseUrl = "http://localhost:11434/v1";
        });

        Assert.Equal(anthropic, local);
    }

    [Fact]
    public async Task Lists_active_goals_by_key()
    {
        var prompt = await BuildAsync(seed: SeedEverything);

        Assert.Contains("Always refer to a goal by its key", prompt);
        Assert.Contains("\n- GOAL-1 Ship PersonaOS (year)", prompt);
    }

    [Fact]
    public async Task Summarises_the_running_sprint_and_lists_only_its_open_tasks()
    {
        var prompt = await BuildAsync(c => c.TimeZone = "UTC", SeedEverything);

        Assert.Contains("SPRINT-2 “Paperwork” is running until Sun 27 Sep 18:00: 3 of 8 points done.", prompt);
        Assert.Contains("\n- TASK-7 File taxes [todo, 5 pts]", prompt);
        Assert.Contains("\n- TASK-8 Unsized [in_progress, unestimated]", prompt);
        Assert.DoesNotContain("TASK-9", prompt);
    }

    [Fact]
    public async Task Lists_todays_planner_with_times_first()
    {
        var prompt = await BuildAsync(c => c.TimeZone = "UTC", SeedEverything);

        Assert.Contains("Today's planner (use the planner tools to change it):\n- 07:30 Gym [planned]\n- Read [planned]", prompt);
    }

    [Fact]
    public async Task Lists_upcoming_reminders_in_the_users_zone()
    {
        var prompt = await BuildAsync(seed: SeedEverything);

        Assert.Contains("Upcoming reminders (times in the user's zone):", prompt);
        Assert.Contains(" — Call mum", prompt);
    }

    [Fact]
    public async Task Leaves_out_sections_with_nothing_in_them()
    {
        var prompt = await BuildAsync();

        Assert.DoesNotContain("The user's active goals", prompt);
        Assert.DoesNotContain("Today's planner", prompt);
        Assert.DoesNotContain("Upcoming reminders", prompt);
        Assert.DoesNotContain("\n\n\n", prompt);
        // The board's rules stand on their own, with no sprint to describe.
        Assert.Contains("The sprint board works like Scrum", prompt);
    }

    [Fact]
    public async Task Text_the_user_wrote_is_never_read_as_a_template()
    {
        var prompt = await BuildAsync(c => c.PersonaTemplate = "Call me {{nickname}} and never {{#x}}.");

        Assert.Contains("Call me {{nickname}} and never {{#x}}.", prompt);
    }

    [Fact]
    public async Task An_installation_can_reword_a_fragment_without_a_rebuild()
    {
        var overrides = new FakePromptOverrides();
        overrides.Files["identity.prompty"] = "You are {{nickname}}, the house assistant.";

        var prompt = await BuildAsync(c => c.AssistantNickname = "Juno", overrides: overrides);

        Assert.StartsWith("You are Juno, the house assistant.", prompt);
    }
}
