using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Every tool call the model makes passes these guards, in order, before it runs:
/// <list type="number">
/// <item><see cref="ArgumentEnvelopeGuard"/> unwraps arguments a model nested in an envelope, so
/// everything after it sees the real fields.</item>
/// <item><see cref="OfferedToolGuard"/> refuses a tool that was not offered this turn, such as a
/// write tool in Brainstorm, which would otherwise still become a card.</item>
/// <item><see cref="PlainSumGuard"/> stops a write for a message that only asks a sum.</item>
/// <item><see cref="CreativeAskGuard"/> stops a write for a message that asks for a poem, a name or
/// ideas and says nothing of keeping it.</item>
/// <item><see cref="NoteDeleteGuard"/> stops a write for a message that asks for a note to be
/// deleted, which no tool can do.</item>
/// <item><see cref="CalendarRequestGuard"/> turns a reminder for something the user asked to put on
/// their calendar into a planner item.</item>
/// <item><see cref="ReminderRequestGuard"/> turns a note for something the user asked to be reminded
/// of at a day and time into a reminder.</item>
/// <item><see cref="ReminderNotEventGuard"/> turns a planner item for something the user asked to be
/// reminded of at a time into a reminder.</item>
/// <item><see cref="UserTimeGuard"/> holds a reminder or event to the day and time the user gave,
/// and refuses a reminder whose time nobody gave.</item>
/// <item><see cref="UnaskedLinkGuard"/> drops a task or goal link from a planner item when the user
/// spoke of neither.</item>
/// <item><see cref="RepeatedCallGuard"/> answers a call already made this turn from memory.</item>
/// <item><see cref="RelativeDateGuard"/> sends back a change for a different day than the
/// "today", "tomorrow" or "yesterday" the user said, with the right date.</item>
/// <item><see cref="TaskTitleGuard"/> refuses a task or goal titled with words the user never said.</item>
/// <item><see cref="GoalPeriodGuard"/> holds a new goal to the period the user named, and refuses
/// one whose period nobody gave.</item>
/// <item><see cref="NoteWordsGuard"/> and <see cref="MemoryWordsGuard"/> keep what is saved without a
/// card to the user's own words.</item>
/// <item><see cref="ConfirmationGate"/> turns a call that would change the user's data into a card,
/// after checking the call names things that exist.</item>
/// </list>
/// The order is the design: a repeat is only recognisable once unwrapped and set to the user's time,
/// and a repeated proposal must not become a second card.
/// </summary>
public sealed class ToolCallPipeline(IPersonaToolRegistry registry, ILogger<ToolCallPipeline> logger)
{
    private readonly IToolCallGuard[] _guards =
    [
        new ArgumentEnvelopeGuard(),
        new OfferedToolGuard(),
        new PlainSumGuard(),
        new CreativeAskGuard(),
        new NoteDeleteGuard(),
        new CalendarRequestGuard(),
        new ReminderRequestGuard(),
        new ReminderNotEventGuard(),
        new UserTimeGuard(),
        new UnaskedLinkGuard(),
        new RepeatedCallGuard(),
        new RelativeDateGuard(),
        new TaskTitleGuard(),
        new GoalPeriodGuard(),
        new NoteWordsGuard(),
        new MemoryWordsGuard(),
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
