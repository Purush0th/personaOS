using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Models.Messages;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Infrastructure.Ai;

/// <summary>
/// Anthropic SDK implementation of the AI streaming port. The only place in the
/// codebase that talks to the Anthropic API. SDK exceptions are mapped to
/// <see cref="AiStreamException"/> with user-presentable messages.
/// </summary>
public class AnthropicMessageStreamer : IAiMessageStreamer
{
    private const long MaxOutputTokens = 16000;

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        string apiKey,
        string model,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = new AnthropicClient { ApiKey = apiKey };
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxOutputTokens,
            System = systemPrompt,
            Messages = turns
                .Select(t => new MessageParam
                {
                    Role = t.Role == ChatRoles.Assistant ? Role.Assistant : Role.User,
                    Content = t.Content,
                })
                .ToList(),
            // Phase 2 adds Tools here, dispatched via an Application-level tool registry.
        };

        // `yield` cannot live inside try/catch — advance inside try, yield outside.
        var stream = client.Messages.CreateStreaming(parameters).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                bool moved;
                RawMessageStreamEvent? current = null;
                try
                {
                    moved = await stream.MoveNextAsync();
                    if (moved) current = stream.Current;
                }
                catch (Exception ex)
                {
                    throw Map(ex);
                }

                if (!moved) break;

                AiStreamChunk? chunk = null;
                if (current!.TryPickStart(out var start))
                {
                    chunk = new AiStreamChunk(InputTokens: start.Message.Usage.InputTokens);
                }
                else if (current.TryPickContentBlockDelta(out var blockDelta)
                         && blockDelta.Delta.TryPickText(out var text))
                {
                    chunk = new AiStreamChunk(TextDelta: text.Text);
                }
                else if (current.TryPickDelta(out var messageDelta))
                {
                    chunk = new AiStreamChunk(OutputTokens: messageDelta.Usage.OutputTokens);
                }

                if (chunk is not null)
                {
                    yield return chunk;
                }
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    private static AiStreamException Map(Exception ex) => ex switch
    {
        AiStreamException already => already,
        Anthropic.Exceptions.AnthropicUnauthorizedException =>
            new AiStreamException("The Anthropic API rejected the configured API key. Check it in Settings.", ex),
        Anthropic.Exceptions.AnthropicRateLimitException =>
            new AiStreamException("The Anthropic API rate limit was hit. Try again shortly.", ex),
        Anthropic.Exceptions.AnthropicApiException apiEx =>
            new AiStreamException($"Anthropic API error: {apiEx.Message}", ex),
        _ => new AiStreamException("Could not reach the Anthropic API.", ex),
    };
}
