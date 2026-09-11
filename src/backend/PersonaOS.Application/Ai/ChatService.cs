using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
        // Which tools need confirmation is fixed for the turn, so resolve it once rather than
        // re-reading the instance config for every tool the model asks for.
        var mutatingTools = await toolRegistry.GetMutatingToolNamesAsync(ct);
        var streamer = streamerFactory.ForProvider(config.AiProvider);
        var reply = new StringBuilder();
        var receipts = new List<ToolReceipt>();
        var proposals = new List<ProposedAction>();
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
                // Anything that writes is proposed, not run. Models have created goals and
                // reminders nobody asked for — including straight after "let's discuss before
                // we add anything" — and prompt rules did not stop it. The user decides.
                if (mutatingTools.Contains(call.Name))
                {
                    proposals.Add(new ProposedAction(call.Name, call.InputJson));
                    // The model is told plainly, so it stops claiming the thing is done.
                    results.Add(new AiToolResult(
                        call.Id,
                        $"NOT EXECUTED. '{call.Name}' changes the user's data, so it is waiting for "
                        + "their confirmation. Tell them what you are proposing and that they need "
                        + "to confirm it. Do not say it is done, and do not call the tool again.",
                        IsError: false));
                    continue;
                }

                yield return new ChatStreamEvent("tool", ToolName: call.Name, ConversationId: conversation.Id);
                var result = await toolRegistry.ExecuteAsync(call, ct);
                results.Add(result);
                // Record what actually happened, from the tool's own result — the only
                // account of the turn that does not depend on the model telling the truth.
                receipts.Add(ToolReceiptBuilder.Build(call.Name, result.Content, result.IsError));
            }
            turns.Add(new AiChatTurn(ChatRoles.User, string.Empty, ToolResults: results));
        }

        if (streamError is not null && reply.Length == 0)
        {
            yield return new ChatStreamEvent("error", Error: streamError, ConversationId: conversation.Id);
            yield break;
        }

        // A weak model may print a tool call as text rather than emitting it through the
        // provider's tool-call channel. Nothing ran, so strip it: otherwise the user is shown
        // internals, and the next turn reads it back from history and copies the mistake.
        var raw = reply.ToString();
        var finalText = LeakedToolCallScrubber.Scrub(raw, tools.Select(t => t.Name).ToArray());
        var scrubbed = !ReferenceEquals(finalText, raw);
        if (scrubbed)
        {
            logger.LogWarning(
                "Model {Model} emitted tool-call syntax or a stray code fence in its reply text; cleaned before storing.",
                config.AiModel);

            if (finalText.Length == 0)
            {
                finalText = "I tried to use one of my tools but formed the request incorrectly, "
                          + "so nothing was changed. Could you rephrase that?";
            }
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
            Content = finalText,
            InputTokens = inputTokens is null ? null : (int)inputTokens,
            OutputTokens = outputTokens is null ? null : (int)outputTokens,
            ToolActionsJson = receipts.Count == 0 ? null : JsonSerializer.Serialize(receipts),
            CreatedAtUtc = now.AddMilliseconds(1),
        });
        conversation.UpdatedAtUtc = now;
        await db.SaveChangesAsync(CancellationToken.None);

        // Persist proposals against the message that made them, now that it has an id.
        var pending = new List<PendingActionDto>();
        if (proposals.Count > 0)
        {
            var assistantMessage = db.ChatMessages.Local.Last(m => m.Role == ChatRoles.Assistant);
            foreach (var proposal in proposals)
            {
                var action = new PendingAction
                {
                    ConversationId = conversation.Id,
                    ChatMessageId = assistantMessage.Id,
                    ToolName = proposal.ToolName,
                    InputJson = string.IsNullOrWhiteSpace(proposal.InputJson) ? "{}" : proposal.InputJson,
                    Summary = ProposedActionSummary.Describe(proposal.ToolName, proposal.InputJson),
                };
                db.PendingActions.Add(action);
                pending.Add(new PendingActionDto(
                    action.PublicId, action.ToolName, action.Summary,
                    PendingActionStatuses.Pending, null, null));
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }

        if (streamError is not null)
        {
            yield return new ChatStreamEvent("error", Error: streamError, ConversationId: conversation.Id);
            yield break;
        }

        // Text on "done" means the streamed deltas no longer match what was stored —
        // the client should replace the bubble it built up. Absent when nothing changed.
        yield return new ChatStreamEvent(
            "done",
            Text: scrubbed ? finalText : null,
            ConversationId: conversation.Id,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            Actions: receipts.Count == 0 ? null : receipts,
            Pending: pending.Count == 0 ? null : pending);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(CancellationToken ct = default) =>
        await db.Conversations.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => new ConversationSummary(c.Id, c.PublicId, c.Title, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(ct);

    public async Task<ConversationDetail?> GetConversationAsync(
        string idOrPublicId, CancellationToken ct = default)
    {
        // Public id is the normal case; a numeric id keeps links made before public ids working.
        var numericId = int.TryParse(idOrPublicId, out var parsed) ? parsed : (int?)null;

        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.PublicId == idOrPublicId || (numericId != null && c.Id == numericId))
            .Select(c => new
            {
                c.Id,
                c.PublicId,
                c.Title,
                c.CreatedAtUtc,
                Messages = c.Messages
                    .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
                    .Select(m => new
                    {
                        m.Id, m.Role, m.Content, m.InputTokens, m.OutputTokens, m.CreatedAtUtc,
                        m.ToolActionsJson,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (conversation is null) return null;

        var actions = await db.PendingActions.AsNoTracking()
            .Where(a => a.ConversationId == conversation.Id)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        // Receipts are deserialized here rather than in the query: EF cannot translate it,
        // and a malformed row must not take the whole conversation down.
        var messages = conversation.Messages
            .Select(m => new ChatMessageDto(
                m.Id, m.Role, m.Content, m.InputTokens, m.OutputTokens, m.CreatedAtUtc,
                ReadReceipts(m.ToolActionsJson),
                actions.Where(a => a.ChatMessageId == m.Id).Select(ToDto).ToList() is { Count: > 0 } p
                    ? p
                    : null))
            .ToList();

        return new ConversationDetail(
            conversation.Id, conversation.PublicId, conversation.Title, conversation.CreatedAtUtc, messages);
    }

    private IReadOnlyList<ToolReceipt>? ReadReceipts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<List<ToolReceipt>>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read stored tool receipts; showing the message without them.");
            return null;
        }
    }

    public async Task<bool> DeleteConversationAsync(string idOrPublicId, CancellationToken ct = default)
    {
        var numericId = int.TryParse(idOrPublicId, out var parsed) ? parsed : (int?)null;

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.PublicId == idOrPublicId || (numericId != null && c.Id == numericId), ct);
        if (conversation is null) return false;

        // Messages and pending actions are cascade-deleted by their foreign keys, so the
        // thread leaves nothing orphaned behind it.
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Deleted conversation {PublicId}", conversation.PublicId);
        return true;
    }

    public async Task<PendingActionDto?> ConfirmActionAsync(string actionId, CancellationToken ct = default)
    {
        var action = await db.PendingActions.FirstOrDefaultAsync(a => a.PublicId == actionId, ct);
        if (action is null) return null;

        // Confirming twice must not run the tool twice.
        if (action.Status != PendingActionStatuses.Pending) return ToDto(action);

        var result = await toolRegistry.ExecuteAsync(
            new AiToolCall(action.PublicId, action.ToolName, action.InputJson), ct);

        var receipt = ToolReceiptBuilder.Build(action.ToolName, result.Content, result.IsError);
        action.Status = PendingActionStatuses.Confirmed;
        action.ResultOk = !result.IsError;
        action.ResultSummary = receipt.Summary;
        action.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Confirmed action {Tool} ({Action}); ok={Ok}", action.ToolName, action.PublicId, action.ResultOk);

        return ToDto(action);
    }

    public async Task<PendingActionDto?> DiscardActionAsync(string actionId, CancellationToken ct = default)
    {
        var action = await db.PendingActions.FirstOrDefaultAsync(a => a.PublicId == actionId, ct);
        if (action is null) return null;
        if (action.Status != PendingActionStatuses.Pending) return ToDto(action);

        action.Status = PendingActionStatuses.Discarded;
        action.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        return ToDto(action);
    }

    private static PendingActionDto ToDto(PendingAction a) =>
        new(a.PublicId, a.ToolName, a.Summary, a.Status, a.ResultSummary, a.ResultOk);

    private static Conversation CreateConversation(string firstMessage)
    {
        var title = firstMessage.Trim();
        if (title.Length > 60) title = title[..57] + "…";
        return new Conversation { Title = title.Length == 0 ? "New conversation" : title };
    }
}
