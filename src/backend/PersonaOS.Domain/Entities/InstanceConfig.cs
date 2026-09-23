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
    /// The model's context window in tokens, when the user knows better than the model profile.
    /// Sent to Ollama with every request, and used everywhere to decide how much history fits.
    /// Null means the profile's default.
    /// </summary>
    public int? AiContextTokens { get; set; }

    /// <summary>
    /// The configured provider's API key, encrypted at rest via Data Protection. Never the
    /// raw key. Column name kept for backward compatibility — existing ciphertext is bound
    /// to the protector purpose and must not be re-encrypted under a new one.
    /// </summary>
    public string? AnthropicApiKeyEncrypted { get; set; }

    /// <summary>
    /// The owner's Firebase service-account key (the whole JSON), encrypted at rest under its
    /// own Data Protection purpose — never the AI key's, so neither ciphertext can be passed
    /// off as the other. Null means push is not configured. Bring-your-own: each install uses
    /// its own Firebase project, so no key is ever shared between installs.
    /// </summary>
    public string? FcmServiceAccountEncrypted { get; set; }

    /// <summary>
    /// The Firebase <em>client</em> options the mobile app starts Firebase with (API key, app id,
    /// sender id, project id), extracted from the uploaded google-services.json. Stored in plain
    /// text on purpose: these values are identifiers, not secrets, and ship inside every
    /// ordinary Firebase app. Keeping them server-side is what lets the release APK carry no
    /// Firebase config at all, and lets a reinstalled app recover push on sign-in.
    /// </summary>
    public string? FcmClientConfigJson { get; set; }

    /// <summary>The Firebase project both uploaded files were verified to belong to.</summary>
    public string? FcmProjectId { get; set; }

    /// <summary>Feature toggle map (module name → enabled). Gates UI + endpoints.</summary>
    public Dictionary<string, bool> Features { get; set; } = new();

    /// <summary>Whether a module is switched on. A module missing from the map is off.</summary>
    public bool IsEnabled(string module) => Features.TryGetValue(module, out var on) && on;

    /// <summary>Local time the morning brief is sent; null disables just that job.</summary>
    public TimeOnly? MorningBriefTime { get; set; } = new(7, 30);

    /// <summary>Local time the evening rollup is sent; null disables just that job.</summary>
    public TimeOnly? EveningRollupTime { get; set; } = new(21, 0);

    /// <summary>
    /// Let the assistant reword the briefs in its own voice. Off by default: the composed text is
    /// complete on its own, and a reworded one is used only when every item in it survives.
    /// </summary>
    public bool PhraseBriefs { get; set; }

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

        /// <summary>Ollama through its own API rather than its OpenAI-compatible one, which cannot
        /// be told the context size or to skip a model's thinking. Requires <see cref="AiBaseUrl"/>.</summary>
        public const string Ollama = "ollama";

        public static readonly string[] All = [Anthropic, OpenAiCompatible, Ollama];

        /// <summary>Providers reached at an address the user gives, rather than a fixed service.</summary>
        public static bool NeedsBaseUrl(string provider) => provider is OpenAiCompatible or Ollama;
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
        public const string Board = "board";
    }

    /// <summary>Sensible defaults applied when a fresh instance is created.</summary>
    public static Dictionary<string, bool> DefaultFeatures() => new()
    {
        [Modules.Goals] = true,
        [Modules.Planner] = true,
        [Modules.Reminders] = true,
        [Modules.Docs] = true,
        [Modules.Voice] = true,
        [Modules.Board] = true,
        [Modules.Proactive] = false,
    };
}
