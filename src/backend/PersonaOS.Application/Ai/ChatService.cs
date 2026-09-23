using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai;

/// <summary>
/// Runs a chat turn: the model streams a reply, tools run between its rounds, and the exchange is
/// stored. What may run, and what the reply may say, is decided by the guards
/// (<see cref="ToolCallPipeline"/> and <see cref="ReplyPipeline"/>); this class only orchestrates.
/// </summary>
public class ChatService(
    IAppDbContext db,
    IInstanceConfigService configService,
    ISystemPromptBuilder promptBuilder,
    IAiMessageStreamerFactory streamerFactory,
    IPersonaToolRegistry toolRegistry,
    ToolCallPipeline toolCallGuards,
    ReplyPipeline replyGuards,
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

        var tools = await toolRegistry.GetEnabledToolDefinitionsAsync(ct);
        var request = new ModelRequest(
            streamerFactory.ForProvider(config.AiProvider),
            apiKey ?? string.Empty,
            config,
            await promptBuilder.BuildAsync(ct),
            await HistoryAsync(conversation.Id, userMessage, ct),
            tools,
            // Which tools need confirmation is fixed for the turn, so it is resolved once.
            new ChatTurnState(config.AiModel, await toolRegistry.GetMutatingToolNamesAsync(ct), tools.Select(t => t.Name).ToList()),
            conversation.Id);

        // The reply, streamed as it is written.
        var first = new ModelRound();
        await foreach (var evt in RunToolLoopAsync(request, first, streamText: true, ct)) yield return evt;

        if (first.Error is not null && first.Text.Length == 0)
        {
            yield return new ChatStreamEvent("error", Error: first.Error, ConversationId: conversation.Id);
            yield break;
        }

        var reply = replyGuards.Review(new ReplyDraft(first.Text.ToString(), request.Turn, interrupted: first.Error is not null));
        var final = reply;
        var unverifiedClaim = false;

        // The reply claimed a change nothing made: one chance to make it properly or take it back.
        // Buffered, not streamed: the correction replaces the first draft wholesale rather than
        // being appended after a claim the user has already read.
        if (first.Error is null && reply.UnbackedClaim is { } claim)
        {
            logger.LogWarning("Model {Model} claimed a change without calling a tool; asking it to correct.", config.AiModel);
            request.Turns.Add(new AiChatTurn(ChatRoles.Assistant, reply.Text));
            request.Turns.Add(new AiChatTurn(ChatRoles.User, ClaimCheckInstruction(claim)));

            var correction = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(request, correction, streamText: false, ct)) yield return evt;

            var corrected = correction.Error is null
                ? replyGuards.Review(new ReplyDraft(correction.Text.ToString(), request.Turn, isCorrection: true))
                : null;
            if (corrected is { Text.Length: > 0 })
            {
                final = corrected;
                unverifiedClaim = corrected.UnbackedClaim is not null;
            }
            else
            {
                // The correction failed or said nothing: keep the first reply and flag the claim
                // rather than hide it, unless the correction turned it into a real proposal.
                unverifiedClaim = request.Turn.Proposals.Count == 0;
            }
        }

        // What the user watched stream in is no longer the reply when a guard rewrote it or a
        // correction replaced it; the client must swap its bubble for the stored text.
        var replaceStreamedText = reply.Rewritten || final.Text != reply.Text;
        var pending = await PersistAsync(conversation, userMessage, final.Text, request, unverifiedClaim);

        if (first.Error is not null)
        {
            yield return new ChatStreamEvent("error", Error: first.Error, ConversationId: conversation.Id);
            yield break;
        }

        var receipts = request.Turn.Receipts;
        yield return new ChatStreamEvent(
            "done",
            Text: replaceStreamedText ? final.Text : null,
            ConversationId: conversation.Id,
            InputTokens: request.Usage.Input,
            OutputTokens: request.Usage.Output,
            Actions: receipts.Count == 0 ? null : receipts,
            Pending: pending.Count == 0 ? null : pending,
            UnverifiedClaim: unverifiedClaim ? true : null);
    }

    /// <summary>
    /// Sent when the reply claimed a change nothing made. The last sentence matters: without it,
    /// models answer the check conversationally and the user reads "Sure, here is the corrected
    /// message:" above their reply.
    /// </summary>
    private static string ClaimCheckInstruction(string claim) =>
        "[Automatic check, not from the user] Your last reply says a change was made — \""
        + claim + "\" — but you did not call any tool, so nothing was saved or changed. "
        + "If the user asked for that change, call the right tool now. If they did not, "
        + "rewrite your reply so it does not say anything was done. "
        + "Write only what the user should read, as if for the first time. Do not "
        + "mention this check, and do not introduce your reply.";

    /// <summary>The windowed history plus the new message, oldest first.</summary>
    private async Task<List<AiChatTurn>> HistoryAsync(int conversationId, string userMessage, CancellationToken ct)
    {
        var history = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Take(HistoryWindow)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .ToListAsync(ct);

        var turns = new List<AiChatTurn>(history.Count + 1);
        turns.AddRange(history.Select(m => new AiChatTurn(m.Role, m.Content)));
        turns.Add(new AiChatTurn(ChatRoles.User, userMessage));
        return turns;
    }

    /// <summary>
    /// Streams the model, running the tools it asks for between rounds, until it answers without
    /// asking for more. Text is appended to <paramref name="round"/> and, when
    /// <paramref name="streamText"/>, sent to the client as it arrives.
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> RunToolLoopAsync(
        ModelRequest request, ModelRound round, bool streamText, [EnumeratorCancellation] CancellationToken ct)
    {
        var turn = request.Turn;
        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var iterationText = new StringBuilder();
            var toolCalls = new List<AiToolCall>();
            string? stopReason = null;

            // `yield` cannot sit inside a try with a catch, so the stream is advanced inside one
            // and its events are yielded outside it.
            var stream = request.Streamer
                .StreamAsync(request.ApiKey, request.Config.AiModel, request.Config.AiBaseUrl, request.SystemPrompt,
                    request.Turns, request.Tools, ct)
                .GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    AiStreamChunk chunk;
                    try
                    {
                        if (!await stream.MoveNextAsync()) break;
                        chunk = stream.Current;
                    }
                    catch (AiStreamException ex)
                    {
                        logger.LogWarning(ex, "AI stream failed");
                        round.Error = ex.Message;
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "AI stream failed unexpectedly");
                        round.Error = "Could not reach the AI service.";
                        break;
                    }

                    request.Usage.Add(chunk);
                    if (chunk.ToolCall is not null) toolCalls.Add(chunk.ToolCall);
                    if (chunk.StopReason is not null) stopReason = chunk.StopReason;
                    if (chunk.TextDelta is { Length: > 0 } delta)
                    {
                        round.Text.Append(delta);
                        iterationText.Append(delta);
                        if (streamText) yield return new ChatStreamEvent("delta", Text: delta);
                    }
                }
            }
            finally
            {
                await stream.DisposeAsync();
            }

            if (round.Error is not null || stopReason != AiStopReasons.ToolUse || toolCalls.Count == 0) yield break;

            // The model asked for tools: each passes the guards, then runs or is answered for it.
            var calls = new List<AiToolCall>(toolCalls.Count);
            var results = new List<AiToolResult>(toolCalls.Count);
            foreach (var requested in toolCalls)
            {
                var decision = await toolCallGuards.DecideAsync(requested, turn, ct);
                calls.Add(decision.Call);
                if (decision is ToolCallDecision.Answered answered)
                {
                    results.Add(answered.Result);
                    continue;
                }

                yield return new ChatStreamEvent("tool", ToolName: decision.Call.Name, ConversationId: request.ConversationId);
                var result = await toolRegistry.ExecuteAsync(decision.Call, ct);
                turn.RecordExecution(decision.Call, result);
                results.Add(result);
            }

            request.Turns.Add(new AiChatTurn(ChatRoles.Assistant, iterationText.ToString(), ToolCalls: calls));
            request.Turns.Add(new AiChatTurn(ChatRoles.User, string.Empty, ToolResults: results));

            // Told once that it is repeating itself; a model still doing it is stuck.
            if (turn.IsStuck)
            {
                logger.LogWarning("Model {Model} kept repeating tool calls; stopping after {Repeats}",
                    request.Config.AiModel, turn.Repeats);
                yield break;
            }
        }
    }

    /// <summary>
    /// Stores the exchange (also when the stream broke part way, keeping what arrived) and the
    /// proposals, against the message that made them.
    /// </summary>
    private async Task<List<PendingActionDto>> PersistAsync(
        Conversation conversation, string userMessage, string replyText, ModelRequest request, bool unverifiedClaim)
    {
        var turn = request.Turn;
        var now = DateTime.UtcNow;
        db.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRoles.User,
            Content = userMessage,
            CreatedAtUtc = now,
        });
        var assistantMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRoles.Assistant,
            Content = replyText,
            InputTokens = request.Usage.Input is long input ? (int)input : null,
            OutputTokens = request.Usage.Output is long output ? (int)output : null,
            ToolActionsJson = turn.Receipts.Count == 0 ? null : JsonSerializer.Serialize(turn.Receipts),
            UnverifiedClaim = unverifiedClaim,
            CreatedAtUtc = now.AddMilliseconds(1),
        };
        db.ChatMessages.Add(assistantMessage);
        conversation.UpdatedAtUtc = now;
        await db.SaveChangesAsync(CancellationToken.None);

        var pending = new List<PendingActionDto>(turn.Proposals.Count);
        if (turn.Proposals.Count == 0) return pending;

        foreach (var proposal in turn.Proposals)
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
                action.PublicId, action.ToolName, action.Summary, PendingActionStatuses.Pending, null, null));
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return pending;
    }

    /// <summary>Everything one turn's model rounds share.</summary>
    private sealed record ModelRequest(
        IAiMessageStreamer Streamer,
        string ApiKey,
        InstanceConfig Config,
        string SystemPrompt,
        List<AiChatTurn> Turns,
        IReadOnlyList<AiToolDefinition> Tools,
        ChatTurnState Turn,
        int ConversationId)
    {
        public TokenUsage Usage { get; } = new();
    }

    /// <summary>One pass of the tool loop: the text it produced, and the error that ended it, if any.</summary>
    private sealed class ModelRound
    {
        public StringBuilder Text { get; } = new();

        public string? Error { get; set; }
    }

    /// <summary>Tokens reported across every model call in the turn; null until a provider reports any.</summary>
    private sealed class TokenUsage
    {
        public long? Input { get; private set; }

        public long? Output { get; private set; }

        public void Add(AiStreamChunk chunk)
        {
            if (chunk.InputTokens is not null) Input = (Input ?? 0) + chunk.InputTokens;
            if (chunk.OutputTokens is not null) Output = (Output ?? 0) + chunk.OutputTokens;
        }
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
                        m.ToolActionsJson, m.UnverifiedClaim,
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
                    : null,
                m.UnverifiedClaim))
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
