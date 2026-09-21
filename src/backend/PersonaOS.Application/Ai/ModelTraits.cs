namespace PersonaOS.Application.Ai;

/// <summary>
/// What a model needs that the provider does not tell us. All we get from an OpenAI-compatible
/// endpoint is the name the user typed, so these are name matches — crude, but they keep the
/// workaround out of the user's persona field and in one place.
/// </summary>
public static class ModelTraits
{
    /// <summary>The switch Qwen3 reads to skip its reasoning pass.</summary>
    public const string NoThink = "/no_think";

    /// <summary>
    /// A Qwen3, which reasons before every reply unless told not to: measured at 626 generated
    /// tokens for a one-sentence answer, paid again at each step of a tool turn. Local builds of
    /// it ("qwen3-4b-16k") count. Qwen2.5 and Gemma have no thinking mode and must not get it.
    /// </summary>
    public static bool ThinksUnlessTold(string? model) =>
        model is not null && model.Contains("qwen3", StringComparison.OrdinalIgnoreCase);
}
