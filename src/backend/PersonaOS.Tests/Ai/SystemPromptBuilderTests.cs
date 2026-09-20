using PersonaOS.Application.Ai;
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
    private static async Task<string> BuildAsync(Action<InstanceConfig>? configure = null)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var instance = await config.GetOrCreateAsync();
        configure?.Invoke(instance);
        await db.SaveChangesAsync();

        return await new SystemPromptBuilder(config, db).BuildAsync();
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
}
