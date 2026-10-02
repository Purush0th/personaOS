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

/// <summary>The chat loop: what the model is told when a reply has to be corrected, and which tool a lookup needs.</summary>
public partial class ChatService
{
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
        + CorrectionStyle;

    /// <summary>
    /// Sent when the reply promised to remember something and saved nothing. Names the tool: told
    /// only to "call the right tool", qwen2.5:3b answered "I have noted this preference" again.
    /// </summary>
    private static string MemoryClaimInstruction(string claim) =>
        "[Automatic check, not from the user] Your last reply says \"" + claim + "\", but you did not "
        + "call create_memory, so nothing was remembered. Call create_memory now, with arguments like "
        + "{\"content\": \"<what the user told you, as one short sentence>\", \"category\": \"preference\"} "
        + "(category: fact, preference, project or decision). If there is nothing worth remembering, "
        + "rewrite your reply without saying you will remember it. " + CorrectionStyle;

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
