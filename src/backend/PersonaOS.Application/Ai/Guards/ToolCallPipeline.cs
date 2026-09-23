using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Every tool call the model makes passes these guards, in order, before it runs:
/// <list type="number">
/// <item><see cref="ArgumentEnvelopeGuard"/> unwraps arguments a model nested in an envelope, so
/// everything after it sees the real fields.</item>
/// <item><see cref="RepeatedCallGuard"/> answers a call already made this turn from memory.</item>
/// <item><see cref="ConfirmationGate"/> turns a call that would change the user's data into a card,
/// after checking the call names things that exist.</item>
/// </list>
/// The order is the design: a repeat is only recognisable once unwrapped, and a repeated proposal
/// must not become a second card.
/// </summary>
public sealed class ToolCallPipeline(IPersonaToolRegistry registry, ILogger<ToolCallPipeline> logger)
{
    private readonly IToolCallGuard[] _guards =
    [
        new ArgumentEnvelopeGuard(),
        new RepeatedCallGuard(),
        new ConfirmationGate(registry),
    ];

    public async Task<ToolCallDecision> DecideAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        foreach (var guard in _guards)
        {
            var verdict = await guard.CheckAsync(call, turn, ct);
            if (verdict is null) continue;

            GuardTelemetry.Record(logger, guard.Name, turn.Model, $"{call.Name}: {verdict.Reason}");
            switch (verdict)
            {
                case ToolCallVerdict.Rewrite rewrite:
                    call = rewrite.Call;
                    break;
                case ToolCallVerdict.Answer answer:
                    return new ToolCallDecision.Answered(call, new AiToolResult(call.Id, answer.Content, answer.IsError));
            }
        }

        return new ToolCallDecision.Run(call);
    }
}
