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

namespace PersonaOS.Application.Ai;

/// <summary>The chat loop: what the model is told when a reply has to be corrected, and which tool a lookup needs.</summary>
public partial class ChatService
{
    /// <summary>
    /// Sent when the reply claimed a change nothing made. The last sentence matters: without it,
    /// models answer the check conversationally and the user reads "Sure, here is the corrected
    /// message:" above their reply.
    /// When the claim names what it changed, the tool is named too: told only to "call the right
    /// tool", qwen2.5:3b answered "I have scheduled a reminder" again (chat dawdsabu, 2026-10-11),
    /// as it had for memories and notes before their instructions named theirs.
    /// </summary>
    private static string ClaimCheckInstruction(string claim, string? tool, string userMessage) =>
        "[Automatic check, not from the user] Your last reply says a change was made or proposed — \""
        + claim + "\" — but you did not call any tool, so nothing was saved and there is no card to confirm. "
        + (tool is null
            ? "If the user asked for that change, call the right tool now. "
            : "The user wrote: \"" + userMessage.Trim() + "\". If they asked for that change, call "
                + tool + " now; the user then sees a card to confirm it. ")
        + "If they did not, rewrite your reply so it does not say anything was done. "
        + CorrectionStyle;

    /// <summary>
    /// Told to the chat model with the prompt: what the intent check found the message wants, and
    /// what that means it should do. The check itself is <see cref="IntentClassifier"/>.
    /// </summary>
    private static string IntentHint(IntentResult intent, ChatTurnState turn)
    {
        var found = "Intent check: in their latest message the user " + ChatIntents.Meaning[intent.Intent] + ". ";
        return intent switch
        {
            // "I need a title for the task" answered "Create a to-do for following up with Ravi".
            { Tool: { } tool } => found + "Call " + tool + " for it, taking the details from their own words"
                + (turn.MutatingTools.Contains(tool) ? "; the user confirms it on a card. " : ". ")
                + "Ask only for something it needs that they did not say, such as the time of a reminder; never guess it.",
            { Intent: ChatIntents.ChangeItem } => found
                + "Look the item up first if you need its key, then call the tool that changes it; the user confirms it on a card.",
            { Intent: ChatIntents.Ambiguous } => found
                + "Ask them in one short question whether they want it as a task, a reminder, an event or a note, and change nothing until they say.",
            _ => found + "Answer it; do not change anything.",
        };
    }

    /// <summary>
    /// The tool for a focused try when the turn made nothing, or null for none. With a
    /// classification, the router's tool for its intent, unless that tool already ran or was
    /// refused, or the reply asks the user something without claiming the change (a missing time
    /// is theirs to give). Without one, the tool a claim that survived the correction names.
    /// </summary>
    private static string? FocusTool(IntentResult? intent, string? survivingClaim, string replyText, string userMessage, ChatTurnState turn)
    {
        if (turn.Refusals.Count > 0) return null;
        if (intent is not null)
        {
            // A reply that looked something up and claims nothing answered a question, whatever the
            // check said: "What are my tasks for today?" came back create_task, and a task card
            // appeared under the right answer (suite, 2026-10-12).
            var answered = turn.Receipts.Count > 0 && survivingClaim is null;
            return intent.Tool is { } wanted
                && !answered
                && (survivingClaim is not null || !replyText.Contains('?'))
                ? wanted
                : null;
        }
        return survivingClaim is null ? null : ChangeToolFor($"{survivingClaim} {userMessage}", turn.ToolNames);
    }

    /// <summary>The last try at a change the message asked for: only <paramref name="tool"/> is offered.</summary>
    private static string FocusedChangeInstruction(string tool, string? claim) =>
        "[Automatic check, not from the user] "
        + (claim is null ? "The user asked for this above, but nothing was done. " : "You said \"" + claim + "\", but nothing was done. ")
        + "Call " + tool + " now for what the user asked for above, using their own words and the "
        + "time they gave. The user will confirm it on a card. If they did not give something it needs, "
        + "such as the time of a reminder, do not call it: ask for that in one short question instead."
        // The card read "I will remind you to buy milk at 9 AM tomorrow." taken from the claim.
        + (tool == "create_reminder"
            ? " 'message' is only what to remember, in a few words like \"Buy milk\", without the time."
            : "");

    /// <summary>The tool that makes the change a claim describes, among those offered; null when it names none.</summary>
    public static string? ChangeToolFor(string claim, IEnumerable<string> enabled)
    {
        var names = enabled.ToHashSet();
        // Reminders first: "I have scheduled a reminder" is about a reminder, not the planner.
        (string Tool, string[] Words)[] byTopic =
        [
            ("create_reminder", ["remind", "alarm"]),
            ("add_planner_item", ["event", "planner", "calendar"]),
            ("create_goal", ["goal"]),
            ("create_task", ["task", "backlog"]),
        ];
        return byTopic.FirstOrDefault(t => names.Contains(t.Tool)
            && t.Words.Any(w => claim.Contains(w, StringComparison.OrdinalIgnoreCase))).Tool;
    }

    /// <summary>
    /// Sent when the reply promised to remember something and saved nothing. Names the tool: told
    /// only to "call the right tool", qwen2.5:3b answered "I have noted this preference" again.
    /// </summary>
    private static string MemoryClaimInstruction(string claim) =>
        "[Automatic check, not from the user] Your last reply says \"" + claim + "\", but you did not "
        + "call create_memory, so nothing was remembered. Call create_memory now, with arguments like "
        + "{\"content\": \"<what the user told you, as one short sentence>\", \"category\": \"preference\"} "
        + "(category: fact, preference, project or decision). If the user told you nothing to keep, "
        + "rewrite your reply without that sentence. " + CorrectionStyle;

    /// <summary>
    /// Sent when the user asked for a note and the reply says "I've noted that" with no note saved.
    /// Told to save a memory instead, qwen2.5:3b answered with the memory check's own words
    /// ("Nothing worth remembering was said, so nothing was remembered"; owner, 2026-10-11).
    /// </summary>
    private static string NoteClaimInstruction(string claim, string userMessage) =>
        "[Automatic check, not from the user] Your last reply says \"" + claim + "\", but you did not "
        + "call create_note, so no note was saved. The user wrote: \"" + userMessage.Trim() + "\". Call "
        + "create_note now, with {\"text\": \"<what to note, in the user's words>\"}. " + CorrectionStyle;

    /// <summary>"Add a note to cook today", "note: buy milk", "jot down …": the user asked for a note.</summary>
    [GeneratedRegex(@"\bnotes?\b|\bjot\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForNote();

    /// <summary>A "noted" claim is about a note when notes are on and the user asked for one, or memory is off.</summary>
    private static bool ClaimIsAboutANote(ChatTurnState turn, string userMessage) =>
        turn.ToolNames.Contains("create_note")
        && (AsksForNote().IsMatch(userMessage) || !turn.ToolNames.Contains("create_memory"));

    /// <summary>Sent when the reply promised to look something up and called no tool.</summary>
    private static string LookupInstruction(string promise, string? tool) =>
        "[Automatic check, not from the user] Your last reply says \"" + promise + "\", but you did "
        + "not call any tool, so nothing was looked up and the user is still waiting. "
        + (tool is null ? "Call the tool" : $"Call {tool}") + " now and answer the user's question "
        + "from its result. Do not ask the user for keys or details a tool can give you. " + CorrectionStyle;

    [GeneratedRegex(@"\bTASK-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex TaskKeyMention();

    /// <summary>
    /// The read tool a promised lookup most likely needs, going by what the user and the reply
    /// talk about; null when nothing points at one of the enabled tools. qwen2.5:3b asked for a task
    /// key three times when told only to "call the tool", so the instruction names it.
    /// </summary>
    public static string? LookupToolFor(string text, IEnumerable<string> enabled)
    {
        var names = enabled.ToHashSet();
        // A task named by its key is read on its own, wherever it is: the board shows only the
        // running sprint.
        if (names.Contains("get_task") && TaskKeyMention().IsMatch(text)) return "get_task";
        (string Tool, string[] Words)[] byTopic =
        [
            ("get_board", ["task", "sprint", "point", "estimat", "board", "backlog"]),
            ("get_goals", ["goal"]),
            ("get_reminders", ["remind"]),
            ("get_planner", ["plan", "today", "schedule", "calendar"]),
        ];
        return byTopic.FirstOrDefault(t => names.Contains(t.Tool)
            && t.Words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))).Tool;
    }

    /// <summary>Sent when the reply said something about a task that the data contradicts.</summary>
    private static string FactCheckInstruction(IReadOnlyList<string> facts) =>
        "[Automatic check, not from the user] Your last reply got a task wrong. The data says: "
        + string.Join(" ", facts) + " Rewrite your reply using these facts. If your earlier replies "
        + "said otherwise, say plainly that they were wrong. " + CorrectionStyle;

    /// <summary>Sent when the reply called a change done while its card still waits for the user.</summary>
    private static string AwaitingCardInstruction(string claim) =>
        "[Automatic check, not from the user] Your last reply says \"" + claim + "\", but nothing "
        + "has been done yet: the change is on a card under your reply, waiting for the user to tap "
        + "Confirm. Rewrite your reply to describe the change as proposed, not done. Do not call the "
        + "tool again. " + CorrectionStyle;

    /// <summary>The reply without the sentence calling the change done, led by a pointer to the card.</summary>
    private static string WithoutClaim(string text, string claim)
    {
        var rest = text.Replace(claim, string.Empty, StringComparison.Ordinal).Trim();
        return rest.Length == 0 ? EmptyReplyGuard.ProposalOnly : $"{EmptyReplyGuard.ProposalOnly} {rest}";
    }

    private const string CorrectionStyle =
        "Write only what the user should read, as if for the first time. Do not "
        + "mention this check, and do not introduce your reply.";
}
