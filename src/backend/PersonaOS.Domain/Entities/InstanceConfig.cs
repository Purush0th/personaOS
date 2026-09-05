namespace PersonaOS.Domain.Entities;

/// <summary>
/// Singleton configuration row for this PersonaOS install (always Id == 1).
/// Seeded/updated by the first-run Setup Wizard. Holds the only per-install
/// personalization: the assistant nickname, its persona/tone, the model and
/// (encrypted) Anthropic key, plus feature toggles. The PersonaOS product
/// brand and visual identity are fixed and never stored here.
/// </summary>
public class InstanceConfig
{
    /// <summary>Singleton key — there is exactly one row, always 1.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>False until the Setup Wizard has completed.</summary>
    public bool IsConfigured { get; set; }

    /// <summary>Free-form name the assistant refers to itself by (e.g. "Siri", "Jarvis").</summary>
    public string AssistantNickname { get; set; } = "Assistant";

    /// <summary>Editable system-prompt tone/personality template (formal / friendly / concise …).</summary>
    public string PersonaTemplate { get; set; } = string.Empty;

    /// <summary>IANA time zone id driving planner/reminder display and scheduling (e.g. "Asia/Kolkata").</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Which AI backend to call. One of <see cref="Providers"/>.</summary>
    public string AiProvider { get; set; } = Providers.Anthropic;

    /// <summary>Model id the user selected (e.g. "claude-opus-4-8", "gpt-4o", "llama3.1").</summary>
    public string AiModel { get; set; } = "claude-opus-4-8";

    /// <summary>
    /// Base URL for the OpenAI-compatible provider (e.g. "https://api.openai.com/v1",
    /// "http://localhost:11434/v1" for Ollama). Ignored for the Anthropic provider.
    /// </summary>
    public string? AiBaseUrl { get; set; }

    /// <summary>
    /// The configured provider's API key, encrypted at rest via Data Protection. Never the
    /// raw key. Column name kept for backward compatibility — existing ciphertext is bound
    /// to the protector purpose and must not be re-encrypted under a new one.
    /// </summary>
    public string? AnthropicApiKeyEncrypted { get; set; }

    /// <summary>Feature toggle map (module name → enabled). Gates UI + endpoints.</summary>
    public Dictionary<string, bool> Features { get; set; } = new();

    /// <summary>Local time the morning brief is sent; null disables just that job.</summary>
    public TimeOnly? MorningBriefTime { get; set; } = new(7, 30);

    /// <summary>Local time the evening rollup is sent; null disables just that job.</summary>
    public TimeOnly? EveningRollupTime { get; set; } = new(21, 0);

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Supported AI backends. The app talks to all of them through the neutral
    /// <c>IAiMessageStreamer</c> port; the value selects which adapter handles a request.</summary>
    public static class Providers
    {
        /// <summary>Anthropic Claude via the official SDK (default).</summary>
        public const string Anthropic = "anthropic";

        /// <summary>Any OpenAI Chat Completions-compatible endpoint: OpenAI, Ollama,
        /// Groq, OpenRouter, LM Studio, etc. Requires <see cref="AiBaseUrl"/>.</summary>
        public const string OpenAiCompatible = "openai_compatible";

        public static readonly string[] All = [Anthropic, OpenAiCompatible];
    }

    /// <summary>Canonical module names used as keys in <see cref="Features"/>.</summary>
    public static class Modules
    {
        public const string Goals = "goals";
        public const string Planner = "planner";
        public const string Reminders = "reminders";
        public const string Docs = "docs";
        public const string Voice = "voice";
        public const string Proactive = "proactive";
    }

    /// <summary>Sensible defaults applied when a fresh instance is created.</summary>
    public static Dictionary<string, bool> DefaultFeatures() => new()
    {
        [Modules.Goals] = true,
        [Modules.Planner] = true,
        [Modules.Reminders] = true,
        [Modules.Docs] = true,
        [Modules.Voice] = true,
        [Modules.Proactive] = false,
    };
}
