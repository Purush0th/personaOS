using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai;

public class ChatService(
    IAppDbContext db,
    IInstanceConfigService configService,
    ISystemPromptBuilder promptBuilder,
    IAiMessageStreamerFactory streamerFactory,
    IPersonaToolRegistry toolRegistry,
    ILogger<ChatService> logger) : IChatService
{
    /// <summary>History window: how many prior messages are sent to the model per request.
    /// Older turns are dropped (rolling summarization is a later enhancement).</summary>
    private const int HistoryWindow = 20;

    /// <summary>Upper bound on model↔tool round-trips within one user message.</summary>
    private const int MaxToolIterations = 8;

    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        int? conversationId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (!config.IsConfigured)
        {
            yield return new ChatStreamEvent("error", Error: "This instance is not set up yet. Run the Setup Wizard first.");
            yield break;
        }

        var apiKey = await configService.GetAnthropicApiKeyAsync(ct);
        // Anthropic always needs a key; OpenAI-compatible providers may be keyless (e.g. local Ollama).
        if (config.AiProvider == InstanceConfig.Providers.Anthropic && string.IsNullOrWhiteSpace(apiKey))
        {
            yield return new ChatStreamEvent("error", Error: "No Anthropic API key is set. Add one in Settings.");
            yield break;
        }

        // Load or create the conversation.
        Conversation? conversation = conversationId is int id
            ? await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct)
            : null;
        if (conversationId is not null && conversation is null)
        {
            yield return new ChatStreamEvent("error", Error: $"Conversation {conversationId} not found.");
            yield break;
        }
        conversation ??= CreateConversation(userMessage);
        if (conversation.Id == 0)
        {
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);
        }

        yield return new ChatStreamEvent("start", ConversationId: conversation.Id);

        // Build the request: system prompt + windowed history + the new user message.
        var systemPrompt = await promptBuilder.BuildAsync(ct);
        var history = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Take(HistoryWindow)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .ToListAsync(ct);

        var turns = new List<AiChatTurn>(history.Count + 1);
        turns.AddRange(history.Select(m => new AiChatTurn(m.Role, m.Content)));
        turns.Add(new AiChatTurn(ChatRoles.User, userMessage));

        // Stream the reply, executing Claude tool calls between model rounds.
        // `yield` cannot live inside try/catch, so the enumerator is advanced
        // inside try and events are yielded outside it.
        var tools = await toolRegistry.GetEnabledToolDefinitionsAsync(ct);
        var streamer = streamerFactory.ForProvider(config.AiProvider);
        var reply = new StringBuilder();
        long? inputTokens = null;
        long? outputTokens = null;
        string? streamError = null;

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var iterationText = new StringBuilder();
            var toolCalls = new List<AiToolCall>();
            string? stopReason = null;

            var stream = streamer
                .StreamAsync(apiKey ?? string.Empty, config.AiModel, config.AiBaseUrl, systemPrompt, turns, tools, ct)
                .GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    bool moved;
                    AiStreamChunk? chunk = null;
                    try
                    {
                        moved = await stream.MoveNextAsync();
                        if (moved) chunk = stream.Current;
                    }
                    catch (AiStreamException ex)
                    {
                        logger.LogWarning(ex, "AI stream failed");
                        streamError = ex.Message;
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "AI stream failed unexpectedly");
                        streamError = "Could not reach the AI service.";
                        break;
                    }

                    if (!moved) break;

                    if (chunk!.InputTokens is not null) inputTokens = (inputTokens ?? 0) + chunk.InputTokens;
                    if (chunk.OutputTokens is not null) outputTokens = (outputTokens ?? 0) + chunk.OutputTokens;
                    if (chunk.ToolCall is not null) toolCalls.Add(chunk.ToolCall);
                    if (chunk.StopReason is not null) stopReason = chunk.StopReason;
                    if (chunk.TextDelta is { Length: > 0 } delta)
                    {
                        reply.Append(delta);
                        iterationText.Append(delta);
                        yield return new ChatStreamEvent("delta", Text: delta);
                    }
                }
            }
            finally
            {
                await stream.DisposeAsync();
            }

            if (streamError is not null || stopReason != AiStopReasons.ToolUse || toolCalls.Count == 0)
            {
                break;
            }

            // The model asked for tools: run them and hand the results back.
            turns.Add(new AiChatTurn(ChatRoles.Assistant, iterationText.ToString(), ToolCalls: toolCalls));
            var results = new List<AiToolResult>(toolCalls.Count);
            foreach (var call in toolCalls)
            {
                yield return new ChatStreamEvent("tool", ToolName: call.Name, ConversationId: conversation.Id);
                results.Add(await toolRegistry.ExecuteAsync(call, ct));
            }
            turns.Add(new AiChatTurn(ChatRoles.User, string.Empty, ToolResults: results));
        }

        if (streamError is not null && reply.Length == 0)
        {
            yield return new ChatStreamEvent("error", Error: streamError, ConversationId: conversation.Id);
            yield break;
        }

        // Persist the exchange (also when the stream broke mid-reply — keep the partial).
        var now = DateTime.UtcNow;
        db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRoles.User,
            Content = userMessage,
            CreatedAtUtc = now,
        });
        db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRoles.Assistant,
            Content = reply.ToString(),
            InputTokens = inputTokens is null ? null : (int)inputTokens,
            OutputTokens = outputTokens is null ? null : (int)outputTokens,
            CreatedAtUtc = now.AddMilliseconds(1),
        });
        conversation.UpdatedAtUtc = now;
        await db.SaveChangesAsync(CancellationToken.None);

        if (streamError is not null)
        {
            yield return new ChatStreamEvent("error", Error: streamError, ConversationId: conversation.Id);
            yield break;
        }

        yield return new ChatStreamEvent(
            "done",
            ConversationId: conversation.Id,
            InputTokens: inputTokens,
            OutputTokens: outputTokens);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(CancellationToken ct = default) =>
        await db.Conversations.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(ct);

    public async Task<ConversationDetail?> GetConversationAsync(int id, CancellationToken ct = default)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.CreatedAtUtc,
                Messages = c.Messages
                    .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
                    .Select(m => new ChatMessageDto(
                        m.Id, m.Role, m.Content, m.InputTokens, m.OutputTokens, m.CreatedAtUtc))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return conversation is null
            ? null
            : new ConversationDetail(conversation.Id, conversation.Title, conversation.CreatedAtUtc, conversation.Messages);
    }

    private static Conversation CreateConversation(string firstMessage)
    {
        var title = firstMessage.Trim();
        if (title.Length > 60) title = title[..57] + "…";
        return new Conversation { Title = title.Length == 0 ? "New conversation" : title };
    }
}
