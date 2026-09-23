using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Models;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Proactive;

public interface IBriefPhraser
{
    /// <summary>The brief in the assistant's own words, or null to send it as composed.</summary>
    Task<string?> PhraseAsync(InstanceConfig config, string brief, CancellationToken ct = default);
}

/// <summary>
/// Rewords a composed brief in the assistant's voice, when the user has asked for it
/// (<see cref="InstanceConfig.PhraseBriefs"/>). Strictly an enhancement: the composed text is
/// already complete and correct, and it is what goes out whenever the model is off, slow, or
/// changes the facts. The rewording is accepted only if every item and every time or number in the
/// original is still there, so a model cannot drop a reminder or invent one.
/// </summary>
public sealed partial class BriefPhraser(
    IInstanceConfigService configService,
    IAiMessageStreamerFactory streamerFactory,
    IPromptLibrary prompts,
    ILogger<BriefPhraser> logger) : IBriefPhraser
{
    /// <summary>The whole rewording must finish within this, or the composed brief goes out.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<string?> PhraseAsync(InstanceConfig config, string brief, CancellationToken ct = default)
    {
        if (!config.PhraseBriefs) return null;

        var apiKey = await configService.GetAnthropicApiKeyAsync(ct);
        if (config.AiProvider == InstanceConfig.Providers.Anthropic && string.IsNullOrWhiteSpace(apiKey)) return null;

        var request = new AiRequest(
            apiKey ?? string.Empty,
            config.AiModel,
            config.AiBaseUrl,
            prompts.Render("brief-phrasing", new Dictionary<string, object?>
            {
                ["nickname"] = config.AssistantNickname,
                ["persona"] = config.PersonaTemplate.Trim(),
            }),
            [new AiChatTurn(ChatRoles.User, brief)],
            [],
            ModelProfiles.For(config).OptionsFor(config.AiProvider));

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        var reply = new StringBuilder();
        try
        {
            await foreach (var chunk in streamerFactory.ForProvider(config.AiProvider).StreamAsync(request, limit.Token))
            {
                reply.Append(chunk.TextDelta);
            }
        }
        catch (Exception e) when (e is AiStreamException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Could not reword the brief; sending it as composed: {Problem}", e.Message);
            return null;
        }

        var phrased = ThinkingBlock.Strip(reply.ToString()).Trim();
        if (KeepsEveryFact(brief, phrased)) return phrased;

        logger.LogWarning("The reworded brief dropped or changed something; sending it as composed.");
        return null;
    }

    /// <summary>
    /// True when every item of the composed brief survives: each bullet's title, and every time and
    /// number anywhere in it. Also refuses an empty reply and one that rambles on at three times the
    /// length, which is not a brief.
    /// </summary>
    public static bool KeepsEveryFact(string brief, string phrased)
    {
        if (phrased.Length == 0 || phrased.Length > brief.Length * 3) return false;

        var titles = brief.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('•'))
            .Select(line => BulletTitle(line[1..]))
            .Where(title => title.Length > 0 && !title.StartsWith('…'));
        var numbers = Number().Matches(brief).Select(m => m.Value);

        return titles.Concat(numbers).All(fact => phrased.Contains(fact, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"07:30 — Gym" is "Gym"; "Ship v1 (40%)" is "Ship v1"; "TASK-7 File taxes (in progress)" is "TASK-7 File taxes".</summary>
    private static string BulletTitle(string bullet)
    {
        var text = bullet.Trim();
        var dash = text.IndexOf(" — ", StringComparison.Ordinal);
        if (dash >= 0) text = text[(dash + 3)..];
        var bracket = text.LastIndexOf(" (", StringComparison.Ordinal);
        return (bracket > 0 ? text[..bracket] : text).Trim();
    }

    /// <summary>Times like 07:30 and figures like 40% or 3, which a rewording must keep exactly.</summary>
    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b|\b\d+%?")]
    private static partial Regex Number();
}
