using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.History;
using PersonaOS.Application.Ai.Models;
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
public partial class ChatService(
    IAppDbContext db,
    IInstanceConfigService configService,
    ISystemPromptBuilder promptBuilder,
    IAiMessageStreamerFactory streamerFactory,
    IPersonaToolRegistry toolRegistry,
    ToolCallPipeline toolCallGuards,
    ReplyPipeline replyGuards,
    ConversationSummarizer summarizer,
    TimeProvider time,
    IChatContext chatContext,
    ILogger<ChatService> logger) : IChatService
{
    /// <summary>Stored messages considered for the history window; far more than any context holds.</summary>
    private const int MaxHistoryMessages = 200;

    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        int? conversationId,
        string userMessage,
        string? mode = null,
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
        // The mode sent with the message wins and is kept for the conversation; an unknown or
        // missing one (an older client) keeps what the conversation had.
        if (ChatModes.Parse(mode) is { } chosen) conversation.Mode = chosen;
        if (conversation.Id == 0) db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        var activeMode = ChatModes.Parse(conversation.Mode) ?? ChatModes.Chat;

        chatContext.ConversationId = conversation.Id;
        yield return new ChatStreamEvent("start", ConversationId: conversation.Id);

        var profile = ModelProfiles.For(config);
        // Brainstorm and Reflect change nothing, so they are not offered a single write tool.
        var tools = await toolRegistry.GetEnabledToolDefinitionsAsync(ChatModes.Writes(activeMode), ct);
        var streamer = streamerFactory.ForProvider(config.AiProvider);
        var model = new AiRequest(apiKey ?? string.Empty, config.AiModel, config.AiBaseUrl, string.Empty, [], tools,
            profile.OptionsFor(config.AiProvider));
        var (systemPrompt, turns) = await PrepareContextAsync(
            conversation, userMessage, await promptBuilder.BuildAsync(new PromptRequest(userMessage, activeMode), ct), streamer, model, profile, ct);
        var request = new ModelRequest(
            streamer,
            model with { SystemPrompt = systemPrompt },
            profile,
            turns,
            // Which tools need confirmation is fixed for the turn, so it is resolved once.
            new ChatTurnState(
                config.AiModel,
                await toolRegistry.GetMutatingToolNamesAsync(ct),
                tools.Select(t => t.Name).ToList(),
                activeMode,
                userMessage,
                Common.UserClock.Today(config.TimeZone, time)),
            conversation.Id);

        // The reply, streamed as it is written.
        var first = new ModelRound();
        await foreach (var evt in RunToolLoopAsync(request, first, streamText: true, ct)) yield return evt;

        if (first.Error is not null && first.Text.Length == 0)
        {
            yield return new ChatStreamEvent("error", Error: first.Error, ConversationId: conversation.Id);
            yield break;
        }

        var reply = await replyGuards.ReviewAsync(
            new ReplyDraft(first.Text.ToString(), request.Turn, interrupted: first.Error is not null), ct);

        var retried = false;

        // The model wrote nothing and did nothing: qwen2.5:3b sometimes answers with only
        // "</tool_call>" tags, which the scrubber removes (accuracy suite, 2026-10-01). The same
        // request is asked once more before the user is told there is no answer.
        if (first.Error is null && reply.Text is EmptyReplyGuard.NoAnswer or LeakedToolCallGuard.NothingLeft
            && request.Turn.Receipts.Count == 0 && request.Turn.Proposals.Count == 0)
        {
            logger.LogWarning("Model {Model} wrote an empty reply; asking once more.", config.AiModel);
            var retry = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(request, retry, streamText: false, ct)) yield return evt;

            var again = retry.Error is null
                ? await replyGuards.ReviewAsync(new ReplyDraft(retry.Text.ToString(), request.Turn), ct)
                : null;
            if (again is { Text.Length: > 0 } && again.Text is not (EmptyReplyGuard.NoAnswer or LeakedToolCallGuard.NothingLeft))
            {
                reply = again;
                retried = true;
            }
        }

        var final = reply;
        var unverifiedClaim = false;

        // The reply claimed a change that did not happen: one chance to put it right. Buffered, not
        // streamed: the correction replaces the first draft wholesale rather than being appended
        // after a claim the user has already read.
        if (first.Error is null && reply.UnbackedClaim is { } claim)
        {
            logger.LogWarning("Model {Model} claimed a change that did not happen; asking it to correct.", config.AiModel);
            request.Turns.Add(new AiChatTurn(ChatRoles.Assistant, reply.Text));
            request.Turns.Add(new AiChatTurn(ChatRoles.User, reply.ClaimAwaitsConfirmation
                ? AwaitingCardInstruction(claim)
                : ActionClaimDetector.FindMemoryClaim(claim) is not null
                    ? MemoryClaimInstruction(claim)
                    : ClaimCheckInstruction(claim)));

            var correction = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(request, correction, streamText: false, ct)) yield return evt;

            var corrected = correction.Error is null
                ? await replyGuards.ReviewAsync(new ReplyDraft(correction.Text.ToString(), request.Turn, isCorrection: true), ct)
                : null;
            if (corrected is { Text.Length: > 0 }) final = corrected;

            // Above a card, a claim that survived the correction is taken out: qwen2.5:3b repeated
            // "I've created a new goal" word for word when asked to describe the card instead.
            // The card itself says what will happen, so nothing is lost by removing the sentence.
            if (final.ClaimAwaitsConfirmation || (final == reply && reply.ClaimAwaitsConfirmation))
            {
                final.Rewrite(WithoutClaim(final.Text, final.UnbackedClaim ?? claim));
            }

            // The "nothing was saved" note is for a claim nothing backs. Above a card it would be
            // wrong (the card is right there, waiting), so a claim that survives is left to it.
            unverifiedClaim = request.Turn.Proposals.Count == 0 && (final == reply || final.UnbackedClaim is not null);
        }

        // The reply promised to look something up and called nothing: one chance to make the call.
        // The second round replaces the first when it looked something up, or at least answered
        // without another promise: qwen2.5:3b once swapped a right list of tasks that began "let's
        // look at the tasks" for "I do not have the detailed information. Please call the tools".
        // The fact check below then reads whichever reply stands.
        if (first.Error is null && final.UnbackedClaim is null && final.PromisedLookup is { } promise)
        {
            logger.LogWarning("Model {Model} promised a lookup it never made; asking it to make it.", config.AiModel);
            request.Turns.Add(new AiChatTurn(ChatRoles.Assistant, final.Text));
            request.Turns.Add(new AiChatTurn(ChatRoles.User,
                LookupInstruction(promise, LookupToolFor($"{userMessage} {final.Text}", request.Model.Tools.Select(t => t.Name)))));

            var lookup = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(request, lookup, streamText: false, ct)) yield return evt;

            var answered = lookup.Error is null
                ? await replyGuards.ReviewAsync(new ReplyDraft(lookup.Text.ToString(), request.Turn, isCorrection: true), ct)
                : null;
            // Without a lookup, an answer that asks again or names items that do not exist (it
            // made up TASK-7) is no better than the promise.
            if (answered is { Text.Length: > 0 }
                && (lookup.ToolRuns > 0 || (PromisedLookupGuard.Find(answered.Text) is null && answered.UnknownItems.Count == 0)))
                final = answered;
        }

        // The reply got a task's points or status wrong (ItemFactGuard): one chance to put it right
        // with the facts in hand. If the model still gets them wrong, the facts are said anyway:
        // a wrong number must not be the last word, and qwen2.5:3b defended one for three turns.
        if (first.Error is null && final.WrongFacts.Count > 0)
        {
            var facts = final.WrongFacts;
            logger.LogWarning("Model {Model} got task facts wrong; asking it to correct.", config.AiModel);
            request.Turns.Add(new AiChatTurn(ChatRoles.Assistant, final.Text));
            request.Turns.Add(new AiChatTurn(ChatRoles.User, FactCheckInstruction(facts)));

            var correction = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(request, correction, streamText: false, ct)) yield return evt;

            var corrected = correction.Error is null
                ? await replyGuards.ReviewAsync(new ReplyDraft(correction.Text.ToString(), request.Turn, isCorrection: true), ct)
                : null;
            if (corrected is { Text.Length: > 0 }) final = corrected;

            // Still wrong, or no usable correction: take the wrong sentences out and say the facts,
            // those the model was given and any its correction got wrong in turn. Left in, the
            // wrong sentence sat right above its own correction ("unestimated with 3 value points").
            if (final.WrongFacts.Count > 0)
            {
                var wrong = final.WrongSentences.ToHashSet();
                var rest = ReplySentences.Without(final.Text, s => wrong.Contains(s.Trim())) ?? final.Text;
                var all = string.Join(" ", facts.Concat(final.WrongFacts).Distinct());
                final.Rewrite(rest.Trim().Length == 0 ? all : $"{rest.TrimEnd()}\n\nTo be exact: {all}");
            }
        }

        // The reply still says a change is on its way, but the change was refused and nothing is
        // waiting: say why, from the check itself. In chat 9wuxkb2b the user asked five times and
        // only ever saw "nothing was saved", never the reason.
        if (unverifiedClaim && request.Turn.Proposals.Count == 0 && request.Turn.Refusals.Count > 0)
        {
            final.Rewrite($"{final.Text.TrimEnd()}\n\nIt could not be done: {request.Turn.Refusals[^1]}");
        }

        // What the user watched stream in is no longer the reply when a guard rewrote it or a
        // correction replaced it; the client must swap its bubble for the stored text.
        var replaceStreamedText = retried || reply.Rewritten || final.Text != reply.Text;
        var pending = await PersistAsync(conversation, userMessage, final, request, unverifiedClaim);

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
            UnverifiedClaim: unverifiedClaim ? true : null,
            UnknownItems: final.UnknownItems.Count == 0 ? null : final.UnknownItems);
    }

    /// <summary>
    /// The system prompt and the turns to send: as much recent history as the model's context
    /// holds (see <see cref="HistoryPlanner"/>), with older messages folded into the conversation's
    /// running summary as they leave the window.
    /// </summary>
    private async Task<(string SystemPrompt, List<AiChatTurn> Turns)> PrepareContextAsync(
        Conversation conversation, string userMessage, string basePrompt, IAiMessageStreamer streamer,
        AiRequest model, ModelProfile profile, CancellationToken ct)
    {
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Take(MaxHistoryMessages)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .ToListAsync(ct);

        // Room for a summary is always kept, so writing one never pushes the window over.
        var fixedTokens = TokenEstimate.Of(basePrompt) + TokenEstimate.Of(model.Tools) + TokenEstimate.Of(userMessage)
            + ConversationSummarizer.MaxSummaryChars / 4;
        var plan = HistoryPlanner.Plan(messages, conversation.SummarizedThroughMessageId, fixedTokens, profile.ContextTokens);

        if (plan.PromptTooLarge)
        {
            logger.LogWarning(
                "The prompt and tools (about {Tokens} tokens) do not fit {Model}'s {Context}-token context; the model "
                + "will see them cut short. Raise the context size in Settings.",
                fixedTokens, model.Model, profile.ContextTokens);
        }

        if (plan.ToSummarize.Count > 0)
        {
            var summary = await summarizer.SummarizeAsync(
                streamer, model, conversation.Summary, plan.ToSummarize, profile.IdleTimeout, ct);
            if (summary is not null)
            {
                conversation.Summary = summary;
                conversation.SummarizedThroughMessageId = plan.ToSummarize[^1].Id;
                await db.SaveChangesAsync(ct);
            }
        }

        var turns = plan.Kept.Select(m => new AiChatTurn(m.Role, m.Content)).ToList();
        turns.Add(new AiChatTurn(ChatRoles.User, userMessage));
        var systemPrompt = conversation.Summary is { } saved ? basePrompt + "\n\n" + summarizer.Section(saved) : basePrompt;
        return (systemPrompt, turns);
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
        var idleTimeout = request.Profile.IdleTimeout;
        for (var iteration = 0; iteration < request.Profile.MaxToolIterations; iteration++)
        {
            var iterationText = new StringBuilder();
            var toolCalls = new List<AiToolCall>();
            string? stopReason = null;

            // A model that goes quiet (after a tool result, or while it thinks) must not leave the
            // user watching a spinner forever: the wait restarts with every chunk that arrives.
            using var idle = new CancellationTokenSource(Timeout.InfiniteTimeSpan, time);
            using var stop = ct.Register(static state => ((CancellationTokenSource)state!).Cancel(), idle);
            idle.CancelAfter(idleTimeout);

            // `yield` cannot sit inside a try with a catch, so the stream is advanced inside one
            // and its events are yielded outside it.
            var stream = request.Streamer
                .StreamAsync(request.Model with { Turns = request.Turns }, idle.Token)
                .GetAsyncEnumerator(idle.Token);
            try
            {
                while (true)
                {
                    AiStreamChunk chunk;
                    try
                    {
                        if (!await stream.MoveNextAsync()) break;
                        chunk = stream.Current;
                        idle.CancelAfter(idleTimeout);
                    }
                    catch (OperationCanceledException) when (idle.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        logger.LogWarning("Model {Model} sent nothing for {Seconds} s; giving up on it",
                            request.Model.Model, idleTimeout.TotalSeconds);
                        round.Error = $"The model stopped responding (nothing for {idleTimeout.TotalSeconds:0} seconds). "
                            + "It may still be loading, or its context may be too small for the conversation. Try again.";
                        break;
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

                yield return new ChatStreamEvent(
                    "tool", ToolName: decision.Call.Name, ToolLabel: Ai.ToolLabel.Running(decision.Call.Name),
                    ConversationId: request.ConversationId);
                var result = await toolRegistry.ExecuteAsync(decision.Call, ct);
                round.ToolRuns++;
                turn.RecordExecution(decision.Call, result);
                results.Add(result);
            }

            request.Turns.Add(new AiChatTurn(ChatRoles.Assistant, iterationText.ToString(), ToolCalls: calls));
            request.Turns.Add(new AiChatTurn(ChatRoles.User, string.Empty, ToolResults: results));

            // Told once that it is repeating itself; a model still doing it is stuck.
            if (turn.IsStuck)
            {
                logger.LogWarning("Model {Model} kept repeating tool calls; stopping after {Repeats}",
                    request.Model.Model, turn.Repeats);
                yield break;
            }
        }
    }

    /// <summary>
    /// Stores the exchange (also when the stream broke part way, keeping what arrived) and the
    /// proposals, against the message that made them.
    /// </summary>
    private async Task<List<PendingActionDto>> PersistAsync(
        Conversation conversation, string userMessage, ReplyDraft reply, ModelRequest request, bool unverifiedClaim)
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
            Content = reply.Text,
            InputTokens = request.Usage.Input is long input ? (int)input : null,
            OutputTokens = request.Usage.Output is long output ? (int)output : null,
            ToolActionsJson = turn.Receipts.Count == 0 ? null : JsonSerializer.Serialize(turn.Receipts),
            UnverifiedClaim = unverifiedClaim,
            UnknownItems = reply.UnknownItems.Count == 0 ? null : string.Join(",", reply.UnknownItems),
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
                Summary = ProposedActionSummary.Describe(proposal.ToolName, proposal.InputJson, proposal.Target),
            };
            db.PendingActions.Add(action);
            pending.Add(new PendingActionDto(
                action.PublicId, action.ToolName, action.Summary, PendingActionStatuses.Pending, null, null));
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return pending;
    }

    /// <summary>Everything one turn's model rounds share. <see cref="Turns"/> grows as tools run.</summary>
    private sealed record ModelRequest(
        IAiMessageStreamer Streamer,
        AiRequest Model,
        ModelProfile Profile,
        List<AiChatTurn> Turns,
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

        /// <summary>Tools this round ran (not those a guard answered for).</summary>
        public int ToolRuns { get; set; }
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
}
