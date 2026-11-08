using System.Text.Json;
using System.Text.RegularExpressions;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// "Write a two-line poem about Monday mornings" asks for words in the reply, not for anything to be
/// kept. The fine-tuned qwen3:4b saved it as a note in two runs of three (training run
/// 2026-11-07-qwen3), and stock models did the same while examples were collected (2026-11-05).
/// When the message asks for something to be written or made up and says nothing of keeping it,
/// a call that would write anything is answered here instead of running or becoming a card.
/// </summary>
public sealed partial class CreativeAskGuard : IToolCallGuard
{
    [GeneratedRegex(
        @"^\s*(?:(?:can|could|would)\s+you\s+|please\s+)?(?:write|compose|make\s+up|invent|come\s+up\s+with|suggest|give\s+me|think\s+of|draft)\b"
        + @".{0,40}?\b(?:poems?|haiku|limericks?|verses?|lines|story|stories|riddles?|jokes?|toasts?|captions?|taglines?|slogans?|names?|ideas?|tongue\s+twisters?|rhymes?|puns?|titles?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksToMakeSomething();

    /// <summary>Words that ask for it to be kept, or say where.</summary>
    [GeneratedRegex(
        @"\b(?:notes?|jot|save|store|keep|remember|memory|board|task|backlog|to-?do|calendar|planner|remind|reminder|goal|list)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksToKeepIt();

    public string Name => "creative-ask";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!turn.WriteTools.Contains(call.Name) || turn.UserMessage is not { } message
            || !AsksToMakeSomething().IsMatch(message) || AsksToKeepIt().IsMatch(message))
            return Task.FromResult<ToolCallVerdict?>(null);

        const string Reply = "The user asked you to write or suggest something, not to save it. Nothing was saved. "
            + "Write it in your reply now, without calling any tool.";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = Reply }), IsError: true, $"{call.Name} for a creative ask"));
    }
}
