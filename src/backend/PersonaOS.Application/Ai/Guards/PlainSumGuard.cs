using System.Text.Json;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// A plain sum ("How much is 20% of 250?") changes nothing. The trained qwen2.5:3b answered 50 and
/// proposed a task as well once in three (training run 2026-11-06b, bench-4); the stock model saved
/// a memory or a note for sums while examples were collected (2026-11-05). When the message is a
/// plain sum, a call that would write anything is answered here instead of running or becoming a card.
/// </summary>
public sealed class PlainSumGuard : IToolCallGuard
{
    public string Name => "plain-sum";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!turn.WriteTools.Contains(call.Name) || Arithmetic.Parse(turn.UserMessage) is not { } sum)
            return Task.FromResult<ToolCallVerdict?>(null);

        var message = $"The user asked a sum, not for a change. Nothing was saved. Answer in one sentence: {sum.Question} is {sum.Answer}.";
        return Task.FromResult<ToolCallVerdict?>(new ToolCallVerdict.Answer(
            JsonSerializer.Serialize(new { error = message }), IsError: true, $"{call.Name} for a plain sum"));
    }
}
