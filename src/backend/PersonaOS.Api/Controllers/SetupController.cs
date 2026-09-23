using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Domain.Entities;
using PersonaOS.Application.Ai.Models;
using PersonaOS.Application.Auth;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// First-run Setup Wizard endpoints. POST /api/setup works exactly once — while
/// the instance is unconfigured — and seeds the admin account + InstanceConfig.
/// Afterwards, edits go through PUT /api/setup behind admin auth.
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(
    IInstanceConfigService configService,
    IAuthService authService,
    IAiMessageStreamerFactory streamerFactory) : ControllerBase
{
    public record SetupStatusResponse(bool IsConfigured);

    public record SetupRequest(
        string AssistantNickname,
        string? PersonaTemplate,
        string AdminUsername,
        string AdminPassword,
        string AnthropicApiKey,
        string? AiProvider,
        string? AiModel,
        string? AiBaseUrl,
        string? TimeZone,
        Dictionary<string, bool>? Features,
        int? AiContextTokens = null);

    /// <summary>Current settings minus any secret. <c>HasAnthropicApiKey</c> stands in for the key itself.</summary>
    public record CurrentSettingsResponse(
        string AssistantNickname,
        string PersonaTemplate,
        string AiProvider,
        string AiModel,
        string? AiBaseUrl,
        string TimeZone,
        Dictionary<string, bool> Features,
        bool HasAnthropicApiKey,
        int? AiContextTokens,
        int DefaultContextTokens);

    public record UpdateSettingsRequest(
        string? AssistantNickname,
        string? PersonaTemplate,
        string? AnthropicApiKey,
        string? AiProvider,
        string? AiModel,
        string? AiBaseUrl,
        string? TimeZone,
        Dictionary<string, bool>? Features,
        int? AiContextTokens = null);

    /// <summary>
    /// Provider settings to test. Any omitted field falls back to what's stored, so the
    /// page can test the current config, or a not-yet-saved change (incl. a fresh key).
    /// </summary>
    public record TestConnectionRequest(
        string? AiProvider,
        string? AiModel,
        string? AiBaseUrl,
        string? AnthropicApiKey,
        int? AiContextTokens = null);

    public record TestConnectionResponse(bool Ok, string Message);

    /// <summary>Whether this instance has completed first-run setup.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusResponse>> Status(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return Ok(new SetupStatusResponse(config.IsConfigured));
    }

    /// <summary>One-shot first-run setup. Rejected once the instance is configured.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Configure([FromBody] SetupRequest request, CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (config.IsConfigured || await authService.AdminExistsAsync(ct))
            return Conflict(new { error = "This instance is already configured. Use PUT /api/setup (authenticated) to change settings." });

        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        if (!TryResolveTimeZone(request.TimeZone, out var timeZone))
            return BadRequest(new { error = $"Unknown time zone '{request.TimeZone}'." });

        var providerError = ValidateProvider(request.AiProvider, request.AiBaseUrl) ?? ValidateContext(request.AiContextTokens);
        if (providerError is not null)
            return BadRequest(new { error = providerError });

        await authService.CreateAdminAsync(request.AdminUsername, request.AdminPassword, ct);
        if (!string.IsNullOrWhiteSpace(request.AnthropicApiKey))
            await configService.SetAnthropicApiKeyAsync(request.AnthropicApiKey, ct);
        await configService.UpdateAsync(c =>
        {
            c.AssistantNickname = request.AssistantNickname.Trim();
            c.PersonaTemplate = request.PersonaTemplate?.Trim() ?? string.Empty;
            c.TimeZone = timeZone;
            ApplyAiSettings(c, request.AiProvider, request.AiModel, request.AiBaseUrl, request.AiContextTokens);
            if (request.Features is not null)
                ApplyFeatureToggles(c, request.Features);
            c.IsConfigured = true;
        }, ct);

        return Ok(new { message = $"Setup complete. Say hello to {request.AssistantNickname.Trim()}!" });
    }

    /// <summary>
    /// Current settings, for prefilling the Settings page. Admin JWT required.
    /// The Anthropic key is never returned — only whether one is set.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<CurrentSettingsResponse>> Current(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return Ok(new CurrentSettingsResponse(
            config.AssistantNickname,
            config.PersonaTemplate,
            config.AiProvider,
            config.AiModel,
            config.AiBaseUrl,
            config.TimeZone,
            config.Features,
            HasAnthropicApiKey: !string.IsNullOrEmpty(config.AnthropicApiKeyEncrypted),
            config.AiContextTokens,
            // What applies when the field is left empty, so the page can show it as the hint.
            DefaultContextTokens: ModelProfiles.For(config.AiProvider, config.AiModel).ContextTokens));
    }

    /// <summary>Edit settings after setup. Admin JWT required. Only provided fields change.</summary>
    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        if (request.AnthropicApiKey is not null)
        {
            if (string.IsNullOrWhiteSpace(request.AnthropicApiKey))
                return BadRequest(new { error = "AnthropicApiKey must not be blank when provided." });
            await configService.SetAnthropicApiKeyAsync(request.AnthropicApiKey, ct);
        }

        string? resolvedTimeZone = null;
        if (request.TimeZone is not null && !TryResolveTimeZone(request.TimeZone, out resolvedTimeZone!))
            return BadRequest(new { error = $"Unknown time zone '{request.TimeZone}'." });

        // Validate the effective provider+baseUrl (fall back to what's already stored).
        var current = await configService.GetOrCreateAsync(ct);
        var effectiveProvider = string.IsNullOrWhiteSpace(request.AiProvider) ? current.AiProvider : request.AiProvider;
        var effectiveBaseUrl = request.AiBaseUrl ?? current.AiBaseUrl;
        var providerError = ValidateProvider(effectiveProvider, effectiveBaseUrl) ?? ValidateContext(request.AiContextTokens);
        if (providerError is not null)
            return BadRequest(new { error = providerError });

        await configService.UpdateAsync(c =>
        {
            if (!string.IsNullOrWhiteSpace(request.AssistantNickname))
                c.AssistantNickname = request.AssistantNickname.Trim();
            if (request.PersonaTemplate is not null)
                c.PersonaTemplate = request.PersonaTemplate.Trim();
            ApplyAiSettings(c, request.AiProvider, request.AiModel, request.AiBaseUrl, request.AiContextTokens);
            if (resolvedTimeZone is not null)
                c.TimeZone = resolvedTimeZone;
            if (request.Features is not null)
                ApplyFeatureToggles(c, request.Features);
        }, ct);

        return Ok(new { message = "Settings updated." });
    }

    /// <summary>
    /// Verifies the provider is reachable and answering, without saving anything. Runs a
    /// tiny completion through the selected adapter and stops at the first token. A provider
    /// failure returns 200 with <c>Ok=false</c> so the page can show it inline; only a
    /// malformed request (bad provider, missing base URL) is a 400.
    /// </summary>
    [HttpPost("test")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<TestConnectionResponse>> TestConnection(
        [FromBody] TestConnectionRequest request, CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);

        var provider = (string.IsNullOrWhiteSpace(request.AiProvider) ? config.AiProvider : request.AiProvider)
            .Trim().ToLowerInvariant();
        var model = string.IsNullOrWhiteSpace(request.AiModel) ? config.AiModel : request.AiModel.Trim();
        var baseUrl = request.AiBaseUrl ?? config.AiBaseUrl;

        var providerError = ValidateProvider(provider, baseUrl) ?? ValidateContext(request.AiContextTokens);
        if (providerError is not null)
            return BadRequest(new { error = providerError });

        // Prefer a freshly-typed key, else the stored one; blank is fine for keyless providers.
        var apiKey = !string.IsNullOrWhiteSpace(request.AnthropicApiKey)
            ? request.AnthropicApiKey.Trim()
            : await configService.GetAnthropicApiKeyAsync(ct);
        if (provider == InstanceConfig.Providers.Anthropic && string.IsNullOrWhiteSpace(apiKey))
            return Ok(new TestConnectionResponse(false, "No Anthropic API key is set. Add one and test again."));

        var streamer = streamerFactory.ForProvider(provider);
        var turns = new[] { new AiChatTurn(ChatRoles.User, "Reply with the single word: OK") };

        // Bound the probe so a hung endpoint doesn't hang the request.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            // The model's own options apply: a thinking model asked to think first could spend the
            // whole 30 seconds reasoning about the word "OK".
            var options = ModelProfiles.For(provider, model, request.AiContextTokens ?? config.AiContextTokens).OptionsFor(provider);
            await foreach (var chunk in streamer.StreamAsync(
                new AiRequest(apiKey ?? string.Empty, model, baseUrl,
                    "You are a connection test. Reply with a single short word.", turns, [], options),
                cts.Token))
            {
                // First sign of life (text, usage, or a stop) means the round-trip works — stop early.
                if (chunk.TextDelta is { Length: > 0 } || chunk.OutputTokens is not null || chunk.StopReason is not null)
                    break;
            }
        }
        catch (AiStreamException ex)
        {
            return Ok(new TestConnectionResponse(false, ex.Message));
        }
        catch (OperationCanceledException)
        {
            return Ok(new TestConnectionResponse(false, "The provider did not respond within 30 seconds."));
        }

        var where = provider == InstanceConfig.Providers.Anthropic ? "Anthropic" : baseUrl;
        return Ok(new TestConnectionResponse(true, $"Connected — {model} responded via {where}."));
    }

    private static string? Validate(SetupRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.AssistantNickname)) return "AssistantNickname is required.";
        if (r.AssistantNickname.Trim().Length > 100) return "AssistantNickname must be 100 characters or fewer.";
        if (string.IsNullOrWhiteSpace(r.AdminUsername)) return "AdminUsername is required.";
        if (string.IsNullOrWhiteSpace(r.AdminPassword)) return "AdminPassword is required.";
        if (r.AdminPassword.Length < 8) return "AdminPassword must be at least 8 characters.";
        // Anthropic requires a key; OpenAI-compatible providers may be keyless (e.g. local Ollama).
        var provider = string.IsNullOrWhiteSpace(r.AiProvider)
            ? InstanceConfig.Providers.Anthropic
            : r.AiProvider.Trim().ToLowerInvariant();
        if (provider == InstanceConfig.Providers.Anthropic && string.IsNullOrWhiteSpace(r.AnthropicApiKey))
            return "An Anthropic API key is required.";
        return null;
    }

    /// <summary>Rejects an unknown provider, or an OpenAI-compatible one with no base URL.</summary>
    private static string? ValidateProvider(string? provider, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return null; // not being changed / left at default

        var normalized = provider.Trim().ToLowerInvariant();
        if (!InstanceConfig.Providers.All.Contains(normalized))
            return $"Unknown AI provider '{provider}'. Use one of: {string.Join(", ", InstanceConfig.Providers.All)}.";

        if (InstanceConfig.Providers.NeedsBaseUrl(normalized) && string.IsNullOrWhiteSpace(baseUrl))
        {
            return normalized == InstanceConfig.Providers.Ollama
                ? "AiBaseUrl is required for the ollama provider (e.g. http://localhost:11434)."
                : "AiBaseUrl is required for the openai_compatible provider (e.g. https://api.openai.com/v1 or http://localhost:11434/v1).";
        }

        return null;
    }

    /// <summary>Smallest and largest context size accepted; 0 clears the setting back to the model's default.</summary>
    private const int MinContextTokens = 2_048;
    private const int MaxContextTokens = 1_048_576;

    private static string? ValidateContext(int? contextTokens) =>
        contextTokens is null or 0 or (>= MinContextTokens and <= MaxContextTokens)
            ? null
            : $"AiContextTokens must be between {MinContextTokens} and {MaxContextTokens}, or 0 for the model's default.";

    /// <summary>
    /// Applies provided AI settings; null/blank fields are left unchanged. A context size of 0
    /// clears it, so the model profile's default applies again.
    /// </summary>
    private static void ApplyAiSettings(InstanceConfig config, string? provider, string? model, string? baseUrl, int? contextTokens)
    {
        if (contextTokens is int tokens)
            config.AiContextTokens = tokens == 0 ? null : tokens;
        if (!string.IsNullOrWhiteSpace(provider))
            config.AiProvider = provider.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(model))
            config.AiModel = model.Trim();
        // Base URL: a non-null value updates it (empty string clears it for the Anthropic provider).
        if (baseUrl is not null)
            config.AiBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim();
    }

    /// <summary>Only known module names can be toggled; unknown keys are ignored.</summary>
    private static void ApplyFeatureToggles(InstanceConfig config, Dictionary<string, bool> requested)
    {
        var known = InstanceConfig.DefaultFeatures().Keys;
        foreach (var key in known)
        {
            if (requested.TryGetValue(key, out var enabled))
                config.Features[key] = enabled;
        }
    }

    private static bool TryResolveTimeZone(string? id, out string resolved)
    {
        resolved = "UTC";
        if (string.IsNullOrWhiteSpace(id))
            return true; // default UTC

        try
        {
            resolved = TimeZoneInfo.FindSystemTimeZoneById(id.Trim()).Id;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
