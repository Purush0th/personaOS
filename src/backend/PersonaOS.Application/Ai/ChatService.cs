using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.History;
using PersonaOS.Application.Ai.Intents;
using PersonaOS.Application.Ai.Models;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

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
    IntentClassifier intentClassifier,
    ILogger<ChatService> logger) : IChatService
{
    /// <summary>Stored messages considered for the history window; far more than any context holds.</summary>
    private const int MaxHistoryMessages = 200;

    /// <summary>Turns of the conversation a focused try sees: the last few exchanges, ending with the user's message.</summary>
    private const int FocusedHistoryTurns = 6;

    /// <summary>The user's messages before this one that guards read, for "tomorrow at 9" answering a question.</summary>
    private const int EarlierUserMessages = 2;

    /// <summary>The reply to "Make a task" when the model's own reply did not ask.</summary>
    internal const string AskTaskTitle = "What should the task be? Tell me what needs doing and I'll add it.";

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
        var readOnly = (await toolRegistry.GetEnabledToolDefinitionsAsync(includeWrites: false, ct)).Select(t => t.Name).ToHashSet();
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
                Common.UserClock.Today(config.TimeZone, time),
                tools.Select(t => t.Name).Where(n => !readOnly.Contains(n)).ToHashSet(),
                Common.UserClock.ToLocal(time.GetUtcNow().UtcDateTime, config.TimeZone),
                [.. turns.SkipLast(1).Where(t => t.Role == ChatRoles.User && t.ToolResults is null && t.Content.Length > 0)
                    .TakeLast(EarlierUserMessages).Select(t => t.Content)]),
            conversation.Id);
        // The conversation as the model first saw it, before any round adds to it.
        var firstTurns = request.Turns.ToList();

        // A question with one right answer in the user's data is answered from the data, before
        // the model is asked. Answered after it, the model's reply streamed in and was then swapped
        // for this one (owner's chat 4nzrb5af, 2026-11-07).
        if (await AnswerFromDataAsync(request.Turn, userMessage, ct) is { } dataAnswer)
        {
            yield return new ChatStreamEvent("delta", Text: dataAnswer);
            var answered = new ReplyDraft(dataAnswer, request.Turn);
            await PersistAsync(conversation, userMessage, answered, request, unverifiedClaim: false);
            var read = request.Turn.Receipts;
            yield return new ChatStreamEvent(
                "done",
                ConversationId: conversation.Id,
                Actions: read.Count == 0 ? null : read);
            yield break;
        }

        // What the message wants, decided before the chat model answers (IntentClassifier), and
        // told to it; skipped where nothing can be changed. Owner, 2026-10-12: classify every message.
        var intent = request.Turn.WriteTools.Count > 0
            ? await intentClassifier.ClassifyAsync(streamer, request.Model, firstTurns, userMessage, request.Turn.ToolNames, ct)
            : null;
        if (intent is not null)
        {
            request = request with { Model = request.Model with { SystemPrompt = $"{request.Model.SystemPrompt}\n\n{IntentHint(intent, request.Turn)}" } };
        }

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
            // "I have noted that you need to remind yourself to buy milk" is about a reminder, not
            // a memory: asked for create_memory, the model had nothing right to call (dawdsabu).
            var claimTool = ChangeToolFor(claim, request.Turn.ToolNames);
            request.Turns.Add(new AiChatTurn(ChatRoles.User, reply.ClaimAwaitsConfirmation
                ? AwaitingCardInstruction(claim)
                : ActionClaimDetector.FindMemoryClaim(claim) is not null && claimTool is null
                    ? ClaimIsAboutANote(request.Turn, userMessage)
                        ? NoteClaimInstruction(claim, userMessage)
                        : MemoryClaimInstruction(claim)
                    : ClaimCheckInstruction(claim, claimTool, userMessage)));

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

        // The message asked for something to be made and the turn made nothing: one focused try,
        // offered only the tool the router gives that intent, and the recent conversation. In chat
        // dawdsabu (2026-10-11) qwen2.5:3b kept answering "I have scheduled a reminder" with the
        // whole prompt in front of it, the correction round included, while a short exchange gets
        // the call right. Without a classification, a claim that survived the correction and names
        // what it changed picks the tool. A reply that asks the user something is left to them,
        // unless it also claimed the change: asking for a missing time is right, guessing one is not.
        if (first.Error is null && request.Turn.Proposals.Count == 0
            && FocusTool(intent, unverifiedClaim ? final.UnbackedClaim ?? reply.UnbackedClaim : null, final.Text, userMessage, request.Turn) is { } changeTool)
        {
            logger.LogWarning("Model {Model} made nothing the message asked for; asking for {Tool} alone.", config.AiModel, changeTool);
            var focused = request with
            {
                Model = request.Model with { Tools = [.. request.Model.Tools.Where(t => t.Name == changeTool)] },
                Turns = [.. firstTurns.TakeLast(FocusedHistoryTurns),
                    new AiChatTurn(ChatRoles.User, FocusedChangeInstruction(changeTool, unverifiedClaim ? final.UnbackedClaim ?? reply.UnbackedClaim : null))],
            };
            var focus = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(focused, focus, streamText: false, ct)) yield return evt;

            // The reply is what really happened: a card waiting, or what the tool saved.
            if (request.Turn.Proposals.Count > 0)
            {
                final.Rewrite(EmptyReplyGuard.ProposalOnly);
                unverifiedClaim = false;
            }
            else if (request.Turn.Receipts.LastOrDefault(r => r.Ok && r.Tool == changeTool) is { } saved)
            {
                final.Rewrite(saved.Summary is { Length: > 0 } what ? $"{saved.Label}: “{what}”." : $"{saved.Label}.");
                unverifiedClaim = false;
            }
            // Told not to guess, it asked for what is missing instead ("What time should I remind
            // you?"): that question is the honest reply, over a claim or an answer that skipped it.
            else if (focus.Error is null
                && await replyGuards.ReviewAsync(new ReplyDraft(focus.Text.ToString(), request.Turn, isCorrection: true), ct) is
                    { UnbackedClaim: null, Text: { Length: > 0 } askedBack } question
                && askedBack.TrimEnd().EndsWith('?'))
            {
                final = question;
                unverifiedClaim = false;
            }
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

        // A question about data the prompt does not hold (the calendar beyond today, notes,
        // reminders) answered without reading it: one focused try that offers only the tool that
        // reads it. Asked "What is on my calendar tomorrow?", qwen2.5:3b read the sprint board and
        // called its tasks tomorrow's calendar; asked about a haircut appointment, it said "today"
        // (accuracy suite, 2026-11-02). The answer from the read replaces the one without it.
        if (first.Error is null && intent?.Intent == ChatIntents.Question && request.Turn.Proposals.Count == 0
            && QuestionReadTool(userMessage, request.Model.Tools.Select(t => t.Name)) is { } readTool
            && !request.Turn.Receipts.Any(r => r.Tool == readTool))
        {
            logger.LogWarning("Model {Model} answered a question without reading {Tool}; asking for it alone.", config.AiModel, readTool);
            var focused = request with
            {
                Model = request.Model with { Tools = [.. request.Model.Tools.Where(t => t.Name == readTool)] },
                Turns = [.. firstTurns.TakeLast(FocusedHistoryTurns),
                    new AiChatTurn(ChatRoles.User, FocusedReadInstruction(readTool, request.Turn.UserToday))],
            };
            var read = new ModelRound();
            await foreach (var evt in RunToolLoopAsync(focused, read, streamText: false, ct)) yield return evt;

            var answered = read.Error is null && read.ToolRuns > 0
                ? await replyGuards.ReviewAsync(new ReplyDraft(read.Text.ToString(), request.Turn, isCorrection: true), ct)
                : null;
            if (answered is { Text.Length: > 0, UnbackedClaim: null }) final = answered;
        }

        // The question named something (a haircut, the electrician) that nothing read this turn
        // mentions, and the reply does not say so: it was found nowhere, and the reply says that
        // first. With the planner read in front of it, qwen2.5:3b still answered "Your haircut
        // appointment is planned for today" (accuracy suite, 2026-11-02).
        if (first.Error is null && intent?.Intent == ChatIntents.Question
            && QuestionReadTool(userMessage, request.Model.Tools.Select(t => t.Name)) is { } searched
            && request.Turn.Receipts.Any(r => r.Ok && r.Tool == searched)
            && NotFound(userMessage, request.Turn.Results, final.Text) is { } missing)
        {
            logger.LogWarning("Model {Model} answered about \"{Missing}\", which nothing read mentions.", config.AiModel, missing);
            var where = searched switch { "get_notes" => "your notes", "get_reminders" => "your reminders", _ => "your planner" };
            final.Rewrite($"I couldn't find anything about “{missing}” in {where}.");
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

        // The user plainly asked for a note and none was saved: the note is saved from their own
        // words. In chat jdeu9b8n qwen2.5:3b answered "I've noted that…" four times running, the
        // correction round included, copying its own earlier replies (owner, 2026-10-11). A note
        // is quick to delete and the receipt says what was saved, so nothing is risked by it.
        if (first.Error is null
            && request.Turn.ToolNames.Contains("create_note")
            && request.Turn.Proposals.Count == 0
            && !request.Turn.Receipts.Any(r => r.Ok && r.Tool == "create_note")
            && Notes.NoteRequest.TextOf(userMessage) is { } asked)
        {
            logger.LogWarning("Model {Model} did not save the note the user asked for; saving it.", config.AiModel);
            var call = new AiToolCall($"server-note-{Guid.NewGuid():N}", "create_note", JsonSerializer.Serialize(new { text = asked }));
            var result = await toolRegistry.ExecuteAsync(call, ct);
            request.Turn.RecordExecution(call, result);
            if (!result.IsError)
            {
                final.Rewrite($"Noted: “{asked}”.");
                unverifiedClaim = false;
            }
        }

        // A plain "mark TASK-11 as done" that ended with no card: the backend proposes the move
        // itself, through the same guards, so the card is checked like any other.
        // Any other card gives way to the move: an update_task that changes nothing (it once came
        // with only the task's own title), or complete_sprint for "I've finished TASK-11".
        if (first.Error is null
            && request.Turn.ToolNames.Contains("move_task")
            && Board.TaskCommand.Parse(userMessage) is var (taskKey, column)
            && !request.Turn.Proposals.Any(p => p.ToolName == "move_task"))
        {
            logger.LogWarning("Model {Model} made no move card for a plain task move; proposing it.", config.AiModel);
            var before = request.Turn.Proposals.Count;
            var call = new AiToolCall($"server-move-{Guid.NewGuid():N}", "move_task", JsonSerializer.Serialize(new { taskKey, column }));
            await toolCallGuards.DecideAsync(call, request.Turn, ct);
            if (request.Turn.Proposals.Count > before)
            {
                request.Turn.Proposals.RemoveAll(p => p.ToolName != "move_task");
                final.Rewrite(EmptyReplyGuard.ProposalOnly);
                unverifiedClaim = false;
            }
        }

        // "Make TASK-11 low priority" or "Rename TASK-11 to …" that ended with no card making that
        // change: proposed from its words, and any other card gives way to it.
        if (first.Error is null
            && request.Turn.ToolNames.Contains("update_task")
            && Board.TaskCommand.Edit(userMessage) is { } edit
            && !request.Turn.Proposals.Any(p => p.ToolName == "update_task" && edit.IsMetBy(p.InputJson)))
        {
            logger.LogWarning("Model {Model} made no card for a plain task edit; proposing it.", config.AiModel);
            var before = request.Turn.Proposals.Count;
            await toolCallGuards.DecideAsync(new AiToolCall($"server-edit-{Guid.NewGuid():N}", "update_task", edit.ToJson()), request.Turn, ct);
            if (request.Turn.Proposals.Count > before)
            {
                var mine = request.Turn.Proposals[^1];
                request.Turn.Proposals.RemoveAll(p => !ReferenceEquals(p, mine));
                final.Rewrite(EmptyReplyGuard.ProposalOnly);
                unverifiedClaim = false;
            }
        }

        // "Start the sprint" or "just start it" that ended with no start card: the planned sprint's
        // start is proposed by the backend, and any other card gives way. Owner's chat b4c7pvc2
        // (2026-11-04): told three times, qwen2.5:3b said "The sprint is now in progress" each time
        // and never called start_sprint.
        if (first.Error is null
            && request.Turn.ToolNames.Contains("start_sprint")
            && !request.Turn.Proposals.Any(p => p.ToolName == "start_sprint")
            && Board.SprintCommand.Parse(userMessage, request.Turn.EarlierUserMessages) is { } start)
        {
            var running = await db.Sprints.AsNoTracking().Where(s => s.Status == SprintStatuses.Active).Select(s => (int?)s.Number).FirstOrDefaultAsync(ct);
            var sprintKey = start.SprintKey
                ?? await db.Sprints.AsNoTracking()
                    .Where(s => s.Status == SprintStatuses.Planned)
                    .OrderBy(s => s.Number)
                    .Select(s => ItemKeys.Sprint(s.Number))
                    .FirstOrDefaultAsync(ct);
            if (start.SprintKey is null && running is int runningNumber)
            {
                // Only one sprint runs at a time: say so rather than claim anything.
                final.Rewrite($"{ItemKeys.Sprint(runningNumber)} is already running, so there is no sprint to start until it is completed.");
                request.Turn.Proposals.RemoveAll(p => p.ToolName is "create_sprint" or "complete_sprint");
                unverifiedClaim = false;
            }
            else if (sprintKey is null)
            {
                final.Rewrite("There is no planned sprint to start. Would you like me to create one?");
                request.Turn.Proposals.RemoveAll(p => p.ToolName is "create_sprint" or "complete_sprint");
                unverifiedClaim = false;
            }
            else
            {
                logger.LogWarning("Model {Model} made no start card for a plain sprint start; proposing it.", config.AiModel);
                var before = request.Turn.Proposals.Count;
                await toolCallGuards.DecideAsync(new AiToolCall($"server-sprint-{Guid.NewGuid():N}", "start_sprint", JsonSerializer.Serialize(new { sprintKey })), request.Turn, ct);
                if (request.Turn.Proposals.Count > before)
                {
                    request.Turn.Proposals.RemoveAll(p => p.ToolName != "start_sprint");
                    final.Rewrite(EmptyReplyGuard.ProposalOnly);
                    unverifiedClaim = false;
                }
            }
        }

        // "Make a task" says nothing about the task, so the reply has to ask. qwen2.5:3b answered
        // "Please provide a title for the task." most times, which leaves the user unsure whether
        // anything happened (holdout, 2026-11-06); a reply that does not ask is replaced by the question.
        if (first.Error is null
            && request.Turn.Proposals.Count == 0
            && Board.TaskRequest.AsksWithoutTitle(userMessage)
            && !final.Text.Contains('?', StringComparison.Ordinal))
        {
            final.Rewrite(AskTaskTitle);
            unverifiedClaim = false;
        }

        // A routine pasted as a table for the calendar: every row proposed from the table itself,
        // and whatever the model made instead gives way (ScheduleRequest; production chat, 2026-11-03).
        if (first.Error is null
            && request.Turn.ToolNames.Contains("add_planner_item")
            && request.Turn.UserNow is DateTime scheduleNow
            && ScheduleFor(userMessage, request.Turn.EarlierUserMessages, DateOnly.FromDateTime(scheduleNow)) is { } schedule)
        {
            logger.LogWarning("Model {Model} was given a {Rows}-row routine; proposing its rows from the table.", config.AiModel, schedule.Rows.Count);
            var startDay = DateOnly.FromDateTime(scheduleNow);
            if (TimeOnly.FromDateTime(scheduleNow) >= schedule.Rows[0].Start) startDay = startDay.AddDays(1);
            request.Turn.Proposals.Clear();
            foreach (var row in schedule.Rows)
            {
                var item = new Dictionary<string, object>
                {
                    ["title"] = row.Title,
                    ["date"] = startDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["scheduledTime"] = row.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                };
                if (row.End is TimeOnly end) item["endTime"] = end.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (schedule.Repeat is { } repeat) item["repeat"] = repeat;
                if (schedule.Until is DateOnly until) item["repeatUntil"] = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (schedule.NoNotification) item["notifyBeforeMinutes"] = -1;
                await toolCallGuards.DecideAsync(new AiToolCall($"server-schedule-{Guid.NewGuid():N}", "add_planner_item", JsonSerializer.Serialize(item)), request.Turn, ct);
            }
            if (request.Turn.Proposals.Count > 0)
            {
                final.Rewrite(ScheduleReply(request.Turn.Proposals.Count, schedule.Rows.Count, startDay, schedule));
                unverifiedClaim = false;
            }
        }

        // "Add repaint the hallway to my to-do list" that ended with no task card: proposed from its
        // words, and the calendar event, plan or sprint made for it instead gives way (training
        // capture, 2026-11-02: 11 of 60 plain task requests went astray).
        if (first.Error is null
            && request.Turn.ToolNames.Contains("create_task")
            && !request.Turn.Proposals.Any(p => p.ToolName == "create_task")
            && (Board.TaskRequest.TitleOf(userMessage) ?? Board.TaskRequest.TitleAfterAsking(userMessage, request.Turn.EarlierUserMessages)) is { } taskTitle)
        {
            logger.LogWarning("Model {Model} made no task card for a plain task request; proposing it.", config.AiModel);
            var before = request.Turn.Proposals.Count;
            await toolCallGuards.DecideAsync(new AiToolCall($"server-task-{Guid.NewGuid():N}", "create_task", JsonSerializer.Serialize(new { title = taskTitle })), request.Turn, ct);
            if (request.Turn.Proposals.Count > before)
            {
                request.Turn.Proposals.RemoveAll(p => p.ToolName != "create_task");
                final.Rewrite(EmptyReplyGuard.ProposalOnly);
                unverifiedClaim = false;
            }
        }

        // "Book lunch with Marco tomorrow at 1pm" that ended with no planner card: proposed from its
        // words, and a task or goal card made for it instead gives way.
        if (first.Error is null
            && request.Turn.ToolNames.Contains("add_planner_item")
            && !request.Turn.Proposals.Any(p => p.ToolName == "add_planner_item")
            && request.Turn.UserNow is DateTime userNow
            && Planner.PlannerCommand.Parse(userMessage, userNow) is { } booking)
        {
            logger.LogWarning("Model {Model} made no planner card for a plain booking; proposing it.", config.AiModel);
            var before = request.Turn.Proposals.Count;
            var input = booking.Time is TimeOnly at
                ? JsonSerializer.Serialize(new { title = booking.Title, date = booking.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), scheduledTime = at.ToString("HH:mm", CultureInfo.InvariantCulture) })
                : JsonSerializer.Serialize(new { title = booking.Title, date = booking.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            await toolCallGuards.DecideAsync(new AiToolCall($"server-book-{Guid.NewGuid():N}", "add_planner_item", input), request.Turn, ct);
            if (request.Turn.Proposals.Count > before)
            {
                request.Turn.Proposals.RemoveAll(p => p.ToolName != "add_planner_item");
                final.Rewrite(EmptyReplyGuard.ProposalOnly);
                unverifiedClaim = false;
            }
        }

        // The model was told to ask when to remind them, and asked without a question: one plain
        // question instead ("Please tell me the time, in one short sentence" twice, holdout2).
        if (first.Error is null && request.Turn.Proposals.Count == 0
            && request.Turn.Refusals.Contains(UserTimeGuard.NoTimeGiven) && !final.Text.Contains('?'))
        {
            final.Rewrite("When should I remind you? Tell me a day and time, like “tomorrow at 9am”.");
            unverifiedClaim = false;
        }

        // The same for a goal with no period: the owner chose asking over a default (2026-11-03).
        if (first.Error is null && request.Turn.Proposals.Count == 0
            && request.Turn.Refusals.Contains(GoalPeriodGuard.NoPeriodGiven) && !final.Text.Contains('?'))
        {
            final.Rewrite(GoalPeriodGuard.Ask);
            unverifiedClaim = false;
        }

        // "Cancel my reminder to call the bank" that ended with no card: proposed from its words.
        if (first.Error is null
            && request.Turn.Proposals.Count == 0
            && request.Turn.ToolNames.Contains("cancel_reminder")
            && Reminders.ReminderCommand.CancelWords(userMessage) is { } reminderWords)
        {
            logger.LogWarning("Model {Model} made no card for a plain reminder cancel; proposing it.", config.AiModel);
            var call = new AiToolCall($"server-cancel-{Guid.NewGuid():N}", "cancel_reminder", JsonSerializer.Serialize(new { message = reminderWords }));
            await toolCallGuards.DecideAsync(call, request.Turn, ct);
            if (request.Turn.Proposals.Count > 0)
            {
                final.Rewrite(EmptyReplyGuard.ProposalOnly);
                unverifiedClaim = false;
            }
        }

        // A plain sum the reply got wrong: the sum is worked out in code (see Arithmetic) and said
        // instead, and a card made for it goes, since a sum changes nothing.
        if (first.Error is null && Arithmetic.Parse(userMessage) is { } sum && !Arithmetic.Says(final.Text, sum.Value))
        {
            logger.LogWarning("Model {Model} got a sum wrong; saying it from code.", config.AiModel);
            request.Turn.Proposals.Clear();
            final.Rewrite($"{sum.Question} = {sum.Answer}");
            unverifiedClaim = false;
        }

        // Asked where to get PersonaOS, the reply named "the GitHub releases page" without the link,
        // three tries in three (accuracy suite, 2026-11-02): the real link is added.
        if (first.Error is null && AsksWhereToGetPersonaOs(userMessage)
            && !final.Text.Contains(Domain.PersonaOsProject.Url, StringComparison.OrdinalIgnoreCase))
        {
            final.Rewrite($"{final.Text.TrimEnd()}\n\nDownloads: {Domain.PersonaOsProject.ReleasesUrl}");
        }

        // The reply still claims a change nothing made, after every try to make it: it says so
        // plainly, without the claim, and offers to go ahead or gives the reason the change was
        // refused (chat 9wuxkb2b: asked five times, the user never saw why). No warning under a
        // false claim (owner, 2026-11-07).
        if (unverifiedClaim)
        {
            final.Rewrite(History.TurnRecord.Honest(final.Text, request.Turn.Refusals.LastOrDefault()));
            unverifiedClaim = false;
        }

        // What the user watched stream in is no longer the reply when a guard rewrote it or a
        // correction replaced it, and a held reply was never streamed: the client must show the
        // stored text.
        var replaceStreamedText = retried || first.Held || reply.Rewritten || final.Text != reply.Text;
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

        // A claim a reply was flagged for goes back without it (see TurnRecord), for the window and
        // the summary alike; a reply that was nothing but the claim goes back not at all. The
        // messages are not tracked, so this never reaches the database.
        foreach (var message in messages.Where(m => m.Role == ChatRoles.Assistant))
        {
            message.Content = TurnRecord.ForModel(message.Content, message.UnverifiedClaim);
        }
        messages.RemoveAll(m => m.Role == ChatRoles.Assistant && m.Content.Length == 0);

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
                        if (streamText && !round.Held) yield return new ChatStreamEvent("delta", Text: delta);
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
                // A reply built on the user's data is checked against it before it is shown, so
                // a guard's fix does not swap out text the user already read (owner, 2026-11-07).
                if (!turn.WriteTools.Contains(decision.Call.Name)) round.Held = true;
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
    /// The answer to a question that has one right answer in the user's data, worked out in code,
    /// or null when the message is not such a question: "what's next?" and "when is my X?"
    /// (PlannerQuestion, owner's chat b4c7pvc2), "am I free on Saturday afternoon?", "is there a
    /// note about the wedding?" and "is TASK-17 in the running sprint?". What it reads goes through
    /// the tools, so the receipts show what was looked at.
    /// </summary>
    private async Task<string?> AnswerFromDataAsync(ChatTurnState turn, string userMessage, CancellationToken ct)
    {
        if (turn.ToolNames.Contains("get_notes") && Notes.NoteQuestion.TopicOf(userMessage) is { Count: > 0 } noteTopic)
        {
            await ReadForAnswerAsync(turn, "get_notes", new { query = noteTopic[0] }, ct);
            var noteTexts = await db.Notes.AsNoTracking().OrderByDescending(n => n.UpdatedAtUtc).Select(n => n.Text).ToListAsync(ct);
            return Notes.NoteQuestion.Answer(noteTopic, [.. noteTexts.Where(t => Notes.NoteQuestion.Matches(t, noteTopic))]);
        }

        if (Board.TaskWhereQuestion.Parse(userMessage) is int askedNumber
            && Board.TaskWhereQuestion.Answer(askedNumber,
                await db.BoardTasks.AsNoTracking().Include(t => t.Sprint).FirstOrDefaultAsync(t => t.Number == askedNumber, ct)) is { } whereAnswer)
        {
            return whereAnswer;
        }

        if (turn.ToolNames.Contains("get_task") && Board.TaskWhereQuestion.ParsePoints(userMessage) is int pointsNumber
            && Board.TaskWhereQuestion.PointsAnswer(pointsNumber,
                await db.BoardTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Number == pointsNumber, ct)) is { } pointsAnswer)
        {
            await ReadForAnswerAsync(turn, "get_task", new { taskKey = $"TASK-{pointsNumber}" }, ct);
            return pointsAnswer;
        }

        if (!turn.ToolNames.Contains("get_planner") || turn.UserNow is not DateTime now) return null;

        if (Planner.PlannerSlotQuestion.Read(userMessage, now) is { } slot)
        {
            var today = DateOnly.FromDateTime(now);
            var day = slot.Day ?? today;
            await ReadForAnswerAsync(turn, "get_planner", new { date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, ct);
            return Planner.PlannerSlotQuestion.Answer(slot, await Planner.PlannerQueries.EventsAsync(db, day, day, ct: ct), day, today);
        }

        if (await PlannerAnswerAsync(userMessage, now, ct) is { } plannerAnswer) return plannerAnswer;

        if (Planner.PlannerQuestion.AsksFree(userMessage))
        {
            var today = DateOnly.FromDateTime(now);
            var day = WhenPhrase.Read(userMessage, now)?.Day ?? today;
            await ReadForAnswerAsync(turn, "get_planner", new { date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, ct);
            return Planner.PlannerQuestion.FreeAnswer(userMessage, day, await Planner.PlannerQueries.EventsAsync(db, day, day, ct: ct), today);
        }
        return null;
    }

    /// <summary>Runs a read tool for an answer worked out in code, so its receipt is shown.</summary>
    private async Task ReadForAnswerAsync(ChatTurnState turn, string tool, object arguments, CancellationToken ct)
    {
        var read = new AiToolCall($"server-{tool}-{Guid.NewGuid():N}", tool, JsonSerializer.Serialize(arguments));
        turn.RecordExecution(read, await toolRegistry.ExecuteAsync(read, ct));
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

        /// <summary>Whether the round read the user's data, so its text is held until the reply is checked.</summary>
        public bool Held { get; set; }
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
