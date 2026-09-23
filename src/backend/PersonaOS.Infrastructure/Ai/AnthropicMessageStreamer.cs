using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
        AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // The base URL and model options are for local servers; Anthropic uses its own endpoint
        // and manages context itself.
        var client = new AnthropicClient { ApiKey = request.ApiKey };
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = MaxOutputTokens,
            System = request.SystemPrompt,
            Messages = request.Turns.Select(ToMessageParam).ToList(),
            Tools = request.Tools.Count > 0 ? request.Tools.Select(ToSdkTool).ToList() : null,
        };

        // Tool-use inputs arrive as partial-JSON deltas per content block; a call is
        // surfaced once its block stops.
        string? pendingToolId = null;
        string? pendingToolName = null;
        var pendingToolJson = new StringBuilder();

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
                else if (current.TryPickContentBlockStart(out var blockStart)
                         && blockStart.ContentBlock.TryPickToolUse(out var toolUse))
                {
                    pendingToolId = toolUse.ID;
                    pendingToolName = toolUse.Name;
                    pendingToolJson.Clear();
                }
                else if (current.TryPickContentBlockDelta(out var blockDelta))
                {
                    if (blockDelta.Delta.TryPickText(out var text))
                    {
                        chunk = new AiStreamChunk(TextDelta: text.Text);
                    }
                    else if (blockDelta.Delta.TryPickInputJson(out var inputJson))
                    {
                        pendingToolJson.Append(inputJson.PartialJson);
                    }
                }
                else if (current.TryPickContentBlockStop(out _) && pendingToolId is not null)
                {
                    chunk = new AiStreamChunk(ToolCall: new AiToolCall(
                        pendingToolId,
                        pendingToolName!,
                        pendingToolJson.Length == 0 ? "{}" : pendingToolJson.ToString()));
                    pendingToolId = null;
                    pendingToolName = null;
                    pendingToolJson.Clear();
                }
                else if (current.TryPickDelta(out var messageDelta))
                {
                    var stopReason = messageDelta.Delta.StopReason == StopReason.ToolUse
                        ? AiStopReasons.ToolUse
                        : null;
                    chunk = new AiStreamChunk(
                        OutputTokens: messageDelta.Usage.OutputTokens,
                        StopReason: stopReason);
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

    private static ToolUnion ToSdkTool(AiToolDefinition definition)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(definition.InputSchemaJson)
            ?? throw new InvalidOperationException($"Tool '{definition.Name}' has an invalid input schema.");
        return new ToolUnion(new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = new InputSchema(schema),
        }, null);
    }

    private static MessageParam ToMessageParam(AiChatTurn turn)
    {
        if (turn.ToolCalls is { Count: > 0 })
        {
            var blocks = new List<ContentBlockParam>();
            if (!string.IsNullOrEmpty(turn.Content))
            {
                blocks.Add(new ContentBlockParam(new TextBlockParam { Text = turn.Content }, null));
            }
            blocks.AddRange(turn.ToolCalls.Select(call => new ContentBlockParam(new ToolUseBlockParam
            {
                ID = call.Id,
                Name = call.Name,
                Input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.InputJson) ?? [],
            }, null)));
            return new MessageParam { Role = Role.Assistant, Content = blocks };
        }

        if (turn.ToolResults is { Count: > 0 })
        {
            var blocks = turn.ToolResults
                .Select(result => new ContentBlockParam(new ToolResultBlockParam
                {
                    ToolUseID = result.ToolUseId,
                    Content = result.Content,
                    IsError = result.IsError ? true : null,
                }, null))
                .ToList();
            return new MessageParam { Role = Role.User, Content = blocks };
        }

        return new MessageParam
        {
            Role = turn.Role == ChatRoles.Assistant ? Role.Assistant : Role.User,
            Content = turn.Content,
        };
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
