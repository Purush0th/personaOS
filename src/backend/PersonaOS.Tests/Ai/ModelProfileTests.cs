using PersonaOS.Application.Ai.Models;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.Ai;

public class ModelProfileTests
{
    private const string Anthropic = InstanceConfig.Providers.Anthropic;
    private const string Compatible = InstanceConfig.Providers.OpenAiCompatible;
    private const string Ollama = InstanceConfig.Providers.Ollama;

    [Theory]
    [InlineData("qwen2.5:0.5b", true)]
    [InlineData("gemma3:270m", true)]
    [InlineData("llama3.2-1b", true)]
    [InlineData("qwen2:1.5b", true)]
    [InlineData("qwen2.5:3b-instruct", false)]
    [InlineData("qwen2.5-3b-16k", false)]
    [InlineData("qwen3:4b", false)]
    [InlineData("gpt-4o", false)]
    [InlineData("llama3.1", false)]
    public void Reads_the_parameter_count_from_the_name(string model, bool small)
    {
        Assert.Equal(small, ModelProfiles.IsSmall(model));
    }

    [Fact]
    public void A_small_model_gets_the_compact_prompt_and_a_short_tool_budget()
    {
        // Scored 3/8: answered from the wrong tool and proposed nothing where a card was wanted.
        var profile = ModelProfiles.For(Ollama, "qwen2.5:0.5b");

        Assert.Equal(PromptVariant.Compact, profile.Prompt);
        Assert.Equal(4, profile.MaxToolIterations);
        Assert.Equal(8_192, profile.ContextTokens);
    }

    [Fact]
    public void The_working_floor_gets_the_full_prompt()
    {
        var profile = ModelProfiles.For(Ollama, "qwen2.5:3b-instruct");

        Assert.Equal(PromptVariant.Full, profile.Prompt);
        Assert.Equal(16_384, profile.ContextTokens);
        Assert.False(profile.Thinks);
    }

    [Theory]
    [InlineData("qwen3:4b")]
    [InlineData("qwen3-4b-16k")]
    [InlineData("Qwen3:30b")]
    public void Qwen3_is_a_thinking_model_and_is_given_longer_to_go_quiet(string model)
    {
        var profile = ModelProfiles.For(Ollama, model);

        Assert.True(profile.Thinks);
        Assert.True(profile.IdleTimeout > ModelProfiles.Local.IdleTimeout);
    }

    [Fact]
    public void Ollama_is_told_to_skip_thinking_and_how_much_context_to_load()
    {
        var options = ModelProfiles.For(Ollama, "qwen3:4b").OptionsFor(Ollama);

        Assert.False(options.Think);
        Assert.Equal(16_384, options.ContextTokens);
        Assert.Equal(ModelProfile.LocalKeepAlive, options.KeepAlive);
    }

    [Fact]
    public void A_model_without_thinking_is_not_sent_a_think_switch()
    {
        Assert.Null(ModelProfiles.For(Ollama, "qwen2.5:3b-instruct").OptionsFor(Ollama).Think);
    }

    [Theory]
    [InlineData(Anthropic, "claude-sonnet-5")]
    [InlineData(Compatible, "qwen3:4b")]
    public void Providers_that_cannot_take_options_are_sent_none(string provider, string model)
    {
        Assert.Equal(AiModelOptionsDefault(), ModelProfiles.For(provider, model).OptionsFor(provider));
    }

    [Fact]
    public void Hosted_models_are_never_treated_as_small_or_thinking_by_name()
    {
        // A name match is for local models; an Anthropic model id is not a parameter count.
        Assert.Same(ModelProfiles.Hosted, ModelProfiles.For(Anthropic, "claude-3-5-haiku"));
    }

    [Fact]
    public void An_openai_compatible_endpoint_is_budgeted_for_the_small_context_ollama_loads_by_default()
    {
        Assert.Equal(8_192, ModelProfiles.For(Compatible, "llama3.1").ContextTokens);
    }

    [Fact]
    public void A_context_size_set_in_settings_wins()
    {
        var profile = ModelProfiles.For(Ollama, "qwen2.5:0.5b", contextTokens: 32_768);

        Assert.Equal(32_768, profile.ContextTokens);
        Assert.Equal(32_768, profile.OptionsFor(Ollama).ContextTokens);
    }

    private static PersonaOS.Application.Common.Interfaces.AiModelOptions AiModelOptionsDefault() =>
        PersonaOS.Application.Common.Interfaces.AiModelOptions.Default;
}
