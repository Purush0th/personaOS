using System.Text;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai.History;

/// <summary>
/// Folds messages leaving the history window into the conversation's running summary, so a long
/// conversation keeps what the user said early on without resending all of it. One extra model
/// call, made only when messages actually fall out of the window.
///
/// A summary is an enhancement, never a dependency: when the call fails or times out, the turn
/// goes ahead without it and the next turn tries again.
/// </summary>
public sealed class ConversationSummarizer(IPromptLibrary prompts, ILogger<ConversationSummarizer> logger)
{
    /// <summary>Each message is cut to this many characters in the transcript being summarised.</summary>
    private const int MaxMessageChars = 1_500;

    /// <summary>The summary's own ceiling, so it cannot grow into the room it is meant to save.</summary>
    public const int MaxSummaryChars = 1_200;

    /// <summary>The summary as it goes at the end of the system prompt.</summary>
    public string Section(string summary) =>
        prompts.Render("conversation-summary", new Dictionary<string, object?> { ["summary"] = summary });

    public async Task<string?> SummarizeAsync(
        IAiMessageStreamer streamer,
        AiRequest model,
        string? previousSummary,
        IReadOnlyList<ChatMessage> messages,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var transcript = new StringBuilder();
        foreach (var message in messages)
        {
            var content = message.Content.Length > MaxMessageChars ? message.Content[..MaxMessageChars] + "…" : message.Content;
            transcript.Append(message.Role == ChatRoles.User ? "User: " : "Assistant: ").Append(content).Append("\n\n");
        }

        var request = model with
        {
            SystemPrompt = prompts.Render("summarize", new Dictionary<string, object?>()),
            Turns = [new AiChatTurn(ChatRoles.User, prompts.Render("summarize-input", new Dictionary<string, object?>
            {
                ["previous"] = previousSummary,
                ["transcript"] = transcript.ToString().TrimEnd(),
            }))],
            Tools = [],
        };

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        var summary = new StringBuilder();
        try
        {
            await foreach (var chunk in streamer.StreamAsync(request, limit.Token))
            {
                summary.Append(chunk.TextDelta);
            }
        }
        catch (Exception e) when (e is AiStreamException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Could not summarise {Count} older messages; going on without: {Problem}", messages.Count, e.Message);
            return null;
        }

        var text = ThinkingBlock.Strip(summary.ToString()).Trim();
        if (text.Length == 0) return null;
        return text.Length > MaxSummaryChars ? text[..MaxSummaryChars] + "…" : text;
    }
}
