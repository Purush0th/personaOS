using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Unwraps <c>{"function":…,"arguments":{…}}</c> and similar envelopes a model puts around the
/// arguments. The tool expects them bare, so every field read as missing: a planner card said only
/// "Add planner item", and confirming it failed with "'date' is required".
/// </summary>
public sealed class ArgumentEnvelopeGuard : IToolCallGuard
{
    public string Name => "argument-envelope";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        var normalized = ToolCallInput.Normalize(call.InputJson);
        return Task.FromResult<ToolCallVerdict?>(ReferenceEquals(normalized, call.InputJson)
            ? null
            : new ToolCallVerdict.Rewrite(call with { InputJson = normalized }, "unwrapped nested arguments"));
    }
}

/// <summary>
/// The same call twice in one turn asks a question already answered. A small model asked "what are
/// my tasks for today?" called get_planner eight times with the same date and never answered, so a
/// repeat gets the first result again with a note to answer now.
/// </summary>
public sealed class RepeatedCallGuard : IToolCallGuard
{
    public const string Note =
        "[You already called this tool with these arguments in this turn. This is the same result. "
        + "Answer the user now; do not call it again.]";

    public string Name => "repeated-call";

    public Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!turn.TryRecall(call, out var earlier)) return Task.FromResult<ToolCallVerdict?>(null);

        turn.CountRepeat();
        return Task.FromResult<ToolCallVerdict?>(
            new ToolCallVerdict.Answer(earlier + "\n\n" + Note, IsError: false, $"repeat #{turn.Repeats}"));
    }
}

/// <summary>
/// Anything that writes is proposed, not run. Models created goals and reminders nobody asked for,
/// including straight after "let's discuss before we add anything", and prompt rules did not stop
/// it. The user decides, on a card.
///
/// The call is validated first: a proposal naming something that does not exist used to fail only
/// after the user tapped Confirm, and handing the model the reason now lets it fix the call this turn.
/// </summary>
public sealed class ConfirmationGate(IPersonaToolRegistry registry) : IToolCallGuard
{
    /// <summary>
    /// Phrased as a status, not as instructions: told "tell them what you are proposing", a small
    /// model repeated that sentence to the user word for word instead of following it.
    /// </summary>
    public static string Waiting(string tool) =>
        $"{{\"status\":\"not_executed\",\"reason\":\"'{tool}' changes the user's data and is waiting for their "
        + "confirmation on a card shown under your reply\",\"next\":\"describe the change in your own words; "
        + "it is not done yet; do not call this tool again\"}}";

    public string Name => "confirmation-gate";

    public async Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct)
    {
        if (!turn.MutatingTools.Contains(call.Name)) return null;

        if (await registry.ValidateAsync(call, ct) is { } problem)
        {
            return new ToolCallVerdict.Answer(
                JsonSerializer.Serialize(new { error = problem }), IsError: true, $"refused to propose: {problem}");
        }

        turn.Proposals.Add(new ProposedAction(call.Name, call.InputJson, await registry.DescribeTargetAsync(call, ct)));
        var waiting = Waiting(call.Name);
        turn.Remember(call, waiting);
        return new ToolCallVerdict.Answer(waiting, IsError: false, "proposed for confirmation");
    }
}
