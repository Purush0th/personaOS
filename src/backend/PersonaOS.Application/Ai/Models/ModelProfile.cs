using System.Globalization;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai.Models;

/// <summary>Which wording of the system prompt a model gets.</summary>
public enum PromptVariant
{
    /// <summary>Every rule, with its reasons. For models that can hold a long prompt.</summary>
    Full,

    /// <summary>Short, blunt rules. A small model loses the thread of a long prompt.</summary>
    Compact,
}

/// <summary>
/// How PersonaOS treats a model, instead of treating a 270M model and Claude alike.
/// </summary>
/// <param name="Name">Which rule matched, for logs.</param>
/// <param name="ContextTokens">The context window to request (Ollama) or to assume when budgeting history.</param>
/// <param name="Thinks">Reasons before every reply unless told not to.</param>
/// <param name="MaxToolIterations">Model and tool round trips allowed for one message.</param>
/// <param name="Prompt">Which wording of the system prompt it gets.</param>
/// <param name="IdleTimeout">How long the model may stay silent before the turn gives up on it.</param>
public sealed record ModelProfile(
    string Name,
    int ContextTokens,
    bool Thinks,
    int MaxToolIterations,
    PromptVariant Prompt,
    TimeSpan IdleTimeout)
{
    /// <summary>How long a local server keeps the model loaded between messages.</summary>
    public const string LocalKeepAlive = "30m";

    /// <summary>What to send with a request. Only the native Ollama adapter can act on these.</summary>
    public AiModelOptions OptionsFor(string provider) => provider == InstanceConfig.Providers.Ollama
        ? new AiModelOptions(ContextTokens, Think: Thinks ? false : null, LocalKeepAlive)
        : AiModelOptions.Default;
}

/// <summary>
/// Picks a profile from the provider and the model name, which is all an OpenAI-compatible
/// endpoint tells us. The rules come from scoring models with <c>scripts/model-check.ps1</c> on the
/// owner's box (eight scenarios, each checked against what the tools did):
/// <list type="bullet">
/// <item><c>qwen2.5:0.5b</c> 3/8: answered "tasks today" from the wrong tool and proposed nothing
/// where a card was wanted. Small models get the compact prompt and a short tool budget.</item>
/// <item><c>qwen2.5:3b-instruct</c> 8/8 at 0.6-7.7 s a turn: the working floor, full prompt.</item>
/// <item><c>qwen3:4b</c> 7/8 but 8-76 s a turn: it reasons before every reply, so thinking is
/// switched off and it is given longer to go quiet.</item>
/// </list>
/// A context size set in Settings wins over the profile's.
/// </summary>
public static partial class ModelProfiles
{
    /// <summary>Anthropic's models: a large context, reliable tool use, fast first token.</summary>
    public static readonly ModelProfile Hosted =
        new("hosted", 200_000, Thinks: false, MaxToolIterations: 8, PromptVariant.Full, TimeSpan.FromSeconds(60));

    /// <summary>
    /// An OpenAI-compatible endpoint: could be OpenAI, could be Ollama loading a 4,096-token context
    /// by default. History is budgeted for the small case unless Settings says otherwise.
    /// </summary>
    public static readonly ModelProfile Compatible =
        new("compatible", 8_192, Thinks: false, MaxToolIterations: 8, PromptVariant.Full, TimeSpan.FromSeconds(180));

    /// <summary>Ollama through its own API, which is told the context size on every request.</summary>
    public static readonly ModelProfile Local =
        new("local", 16_384, Thinks: false, MaxToolIterations: 8, PromptVariant.Full, TimeSpan.FromSeconds(180));

    /// <summary>
    /// The switch Qwen3 reads to skip its reasoning pass. Put in the prompt for a thinking model;
    /// through Ollama's OpenAI-compatible endpoint it measured as having no effect, which is why
    /// the native adapter sends <c>think: false</c> as well.
    /// </summary>
    public const string NoThink = "/no_think";

    /// <summary>At or below this many billion parameters, a model gets the compact treatment.</summary>
    private const double SmallModelBillions = 1.5;

    public static ModelProfile For(InstanceConfig config) =>
        For(config.AiProvider, config.AiModel, config.AiContextTokens);

    public static ModelProfile For(string provider, string model, int? contextTokens = null)
    {
        var profile = provider switch
        {
            InstanceConfig.Providers.Anthropic => Hosted,
            InstanceConfig.Providers.Ollama => Local,
            _ => Compatible,
        };

        if (provider != InstanceConfig.Providers.Anthropic)
        {
            if (IsSmall(model))
            {
                profile = profile with
                {
                    Name = "small",
                    ContextTokens = Math.Min(profile.ContextTokens, 8_192),
                    MaxToolIterations = 4,
                    Prompt = PromptVariant.Compact,
                };
            }

            // Qwen3 reasons before every reply: 626 generated tokens for a one-sentence answer,
            // paid again at each step of a tool turn. Local builds ("qwen3-4b-16k") count.
            if (model.Contains("qwen3", StringComparison.OrdinalIgnoreCase))
            {
                profile = profile with { Name = profile.Name + "+thinking", Thinks = true, IdleTimeout = TimeSpan.FromSeconds(300) };
            }
        }

        return contextTokens is int configured and > 0 ? profile with { ContextTokens = configured } : profile;
    }

    /// <summary>
    /// Reads a parameter count from the name: "qwen2.5:0.5b", "gemma3:270m", "llama3.2-1b". A name
    /// with no size in it is assumed not to be small.
    /// </summary>
    public static bool IsSmall(string model)
    {
        foreach (Match match in ParameterCount().Matches(model))
        {
            var size = double.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
            var billions = match.Groups["unit"].Value.Equals("m", StringComparison.OrdinalIgnoreCase) ? size / 1000 : size;
            if (billions <= SmallModelBillions) return true;
        }

        return false;
    }

    [GeneratedRegex(@"(?<![a-z0-9.])(?<size>\d+(?:\.\d+)?)(?<unit>[bm])(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex ParameterCount();
}
