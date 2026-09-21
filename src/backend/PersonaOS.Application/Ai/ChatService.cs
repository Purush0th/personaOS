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

        // The reply as first written, kept aside if a corrective round replaces it.
        string? firstDraft = null;
        // True when the stored reply still claims a change that no tool made.
        var unverifiedClaim = false;

        // What each tool call already returned this turn, keyed by call and arguments. A small
        // model asked "what are my tasks for today?" called get_planner eight times with the same
        // date and never answered, so a repeat is served from here instead of running again.
        var answered = new Dictionary<string, string>(StringComparer.Ordinal);
        var repeats = 0;

        // At most two rounds: the reply, then — only if it claimed a change that nothing made —
        // one corrective round. See the claim check after the loop.
        for (var round = 0; round < 2; round++)
        {
            var correcting = round == 1;
            var roundReply = correcting ? new StringBuilder() : reply;

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
                            roundReply.Append(delta);
                            iterationText.Append(delta);
                            // A corrective round is buffered, not streamed: its text replaces the
                            // first draft wholesale on "done", rather than being appended after a
                            // claim the user has already read.
                            if (!correcting) yield return new ChatStreamEvent("delta", Text: delta);
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

                // The model asked for tools: run them and hand the results back. Arguments a model
                // wrapped in an envelope are unwrapped first, so everything downstream — the
                // registry, the card, its summary, the repeat check — sees the real fields.
                for (var i = 0; i < toolCalls.Count; i++)
                {
                    var normalized = ToolCallInput.Normalize(toolCalls[i].InputJson);
                    if (!ReferenceEquals(normalized, toolCalls[i].InputJson))
                    {
                        logger.LogInformation(
                            "Unwrapped nested arguments from {Model} for {Tool}", config.AiModel, toolCalls[i].Name);
                        toolCalls[i] = toolCalls[i] with { InputJson = normalized };
                    }
                }

                turns.Add(new AiChatTurn(ChatRoles.Assistant, iterationText.ToString(), ToolCalls: toolCalls));
                var results = new List<AiToolResult>(toolCalls.Count);
                foreach (var call in toolCalls)
                {
                    // The same call twice in one turn asks a question already answered. Serve the
                    // first result again and say so, rather than repeating the work and letting
                    // the model spin until the iteration limit with nothing written.
                    var signature = call.Name + '|' + call.InputJson.Trim();
                    if (answered.TryGetValue(signature, out var already))
                    {
                        repeats++;
                        logger.LogInformation("Model {Model} repeated {Tool}", config.AiModel, call.Name);
                        results.Add(new AiToolResult(
                            call.Id,
                            already + "\n\n[You already called this tool with these arguments in this "
                            + "turn. This is the same result. Answer the user now; do not call it again.]",
                            IsError: false));
                        continue;
                    }

                    // Anything that writes is proposed, not run. Models have created goals and
                    // reminders nobody asked for — including straight after "let's discuss before
                    // we add anything" — and prompt rules did not stop it. The user decides.
                    if (mutatingTools.Contains(call.Name))
                    {
                        // Check the call before the user sees a card for it. A proposal that names
                        // something that does not exist used to fail only after they tapped
                        // Confirm; handing the model the reason now lets it fix the call this turn.
                        if (await toolRegistry.ValidateAsync(call, ct) is { } problem)
                        {
                            logger.LogInformation(
                                "Refused to propose {Tool}: {Problem}", call.Name, problem);
                            results.Add(new AiToolResult(
                                call.Id,
                                JsonSerializer.Serialize(new { error = problem }),
                                IsError: true));
                            continue;
                        }

                        proposals.Add(new ProposedAction(call.Name, call.InputJson));
                        // The model is told plainly, so it stops claiming the thing is done.
                        // Phrased as a status, not as instructions: told "tell them what you are
                        // proposing", a small model repeated that sentence to the user word for
                        // word instead of following it.
                        var waiting =
                            $"{{\"status\":\"not_executed\",\"reason\":\"'{call.Name}' changes the user's "
                            + "data and is waiting for their confirmation on a card shown under your "
                            + "reply\",\"next\":\"describe the change in your own words; it is not done "
                            + "yet; do not call this tool again\"}}";
                        answered[signature] = waiting;
                        results.Add(new AiToolResult(call.Id, waiting, IsError: false));
                        continue;
                    }

                    yield return new ChatStreamEvent("tool", ToolName: call.Name, ConversationId: conversation.Id);
                    var result = await toolRegistry.ExecuteAsync(call, ct);
                    answered[signature] = result.Content;
                    results.Add(result);
                    // Record what actually happened, from the tool's own result — the only
                    // account of the turn that does not depend on the model telling the truth.
                    receipts.Add(ToolReceiptBuilder.Build(call.Name, result.Content, result.IsError));
                }
                turns.Add(new AiChatTurn(ChatRoles.User, string.Empty, ToolResults: results));

                // Telling it once is worth a try; a model still repeating itself after that is
                // stuck, and more rounds only cost the user time.
                if (repeats >= 2)
                {
                    logger.LogWarning(
                        "Model {Model} kept repeating tool calls; stopping the loop after {Repeats}",
                        config.AiModel, repeats);
                    break;
                }
            }

            if (!correcting)
            {
                if (streamError is not null) break;

                // The reply claims a change, yet nothing was proposed this turn. Models do this —
                // "I've set a reminder" with no tool call and an empty reminders table — and a
                // missing card under a confident sentence is too easy to miss. Give the model one
                // chance to either make the change properly or take the claim back. Skipped when
                // anything was proposed: "I've proposed a reminder" is then an honest description.
                var draft = LeakedToolCallScrubber.Scrub(reply.ToString(), tools.Select(t => t.Name).ToArray());
                var claim = proposals.Count == 0 ? ActionClaimDetector.FindClaim(draft) : null;
                if (claim is null) break;

                logger.LogWarning(
                    "Model {Model} claimed a change without calling a tool (\"{Claim}\"); asking it to correct.",
                    config.AiModel, claim);

                firstDraft = draft;
                turns.Add(new AiChatTurn(ChatRoles.Assistant, draft));
                turns.Add(new AiChatTurn(ChatRoles.User,
                    "[Automatic check, not from the user] Your last reply says a change was made — \""
                    + claim + "\" — but you did not call any tool, so nothing was saved or changed. "
                    + "If the user asked for that change, call the right tool now. If they did not, "
                    + "rewrite your reply so it does not say anything was done. "
                    // Without this, models answer the check conversationally and the user reads
                    // "Sure, here is the corrected message:" above their reply.
                    + "Write only what the user should read, as if for the first time. Do not "
                    + "mention this check, and do not introduce your reply."));
                continue;
            }

            // The corrective round finished (or failed). Decide what to keep.
            var corrected = CorrectionPreamble.Strip(
                LeakedToolCallScrubber.Scrub(roundReply.ToString(), tools.Select(t => t.Name).ToArray()));
            if (streamError is not null)
            {
                // The first reply was complete; only the correction failed. Keep the original, say
                // nothing about the error, and flag the claim instead of hiding it.
                streamError = null;
                unverifiedClaim = proposals.Count == 0;
            }
            else
            {
                if (corrected.Length == 0 && proposals.Count > 0)
                {
                    corrected = "Here is the change I would make — confirm it below if it looks right.";
                }

                if (corrected.Length > 0)
                {
                    reply.Clear().Append(corrected);
                    unverifiedClaim = proposals.Count == 0 && ActionClaimDetector.FindClaim(corrected) is not null;
                }
                else
                {
                    unverifiedClaim = true;
                }
            }
        }

        if (streamError is not null && reply.Length == 0)
        {
            yield return new ChatStreamEvent("error", Error: streamError, ConversationId: conversation.Id);
            yield break;
        }

        // A weak model may print a tool call as text rather than emitting it through the
        // provider's tool-call channel. Nothing ran, so strip it: otherwise the user is shown
        // internals, and the next turn reads it back from history and copies the mistake.
        // Reasoning a thinking model wrote as content goes first: it is the model's private
        // notes, not an answer, and storing it would feed it back as history next turn.
        var raw = ThinkingBlock.Strip(reply.ToString());
        var finalText = LeakedToolCallScrubber.Scrub(raw, tools.Select(t => t.Name).ToArray());
        var scrubbed = !ReferenceEquals(finalText, raw) || raw.Length != reply.Length;
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

        // Tools ran and the model never wrote a word — it spent the turn calling them. An empty
        // bubble tells the user nothing, so say what happened.
        var filledEmptyReply = false;
        if (finalText.Length == 0 && streamError is null)
        {
            logger.LogWarning(
                "Model {Model} wrote no reply after {Count} tool results", config.AiModel, receipts.Count);
            finalText = receipts.Count > 0
                ? "I looked that up but did not manage to write an answer. What the tools "
                  + "returned is listed below — ask me again and I will summarise it."
                : "I did not manage to write an answer to that. Could you ask me again?";
            filledEmptyReply = true;
        }

        // What the user watched stream in is no longer the reply when it was cleaned, when a
        // corrective round replaced it, or when there was nothing to watch at all — the client
        // must swap its bubble for the stored text.
        var replaceStreamedText =
            scrubbed || filledEmptyReply || (firstDraft is not null && finalText != firstDraft);

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
            UnverifiedClaim = unverifiedClaim,
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
            Text: replaceStreamedText ? finalText : null,
            ConversationId: conversation.Id,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            Actions: receipts.Count == 0 ? null : receipts,
            Pending: pending.Count == 0 ? null : pending,
            UnverifiedClaim: unverifiedClaim ? true : null);
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
