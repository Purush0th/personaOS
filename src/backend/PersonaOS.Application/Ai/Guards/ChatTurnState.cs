using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// What one user message has produced so far, shared by the guards and the chat loop: which calls
/// were already answered, what was proposed, and what actually ran.
/// </summary>
public sealed class ChatTurnState(string model, IReadOnlySet<string> mutatingTools, IReadOnlyList<string> toolNames)
{
    /// <summary>After this many repeated calls the model is stuck, and more rounds only cost time.</summary>
    private const int StuckAfterRepeats = 2;

    private readonly Dictionary<string, string> _answered = new(StringComparer.Ordinal);

    public string Model { get; } = model;

    /// <summary>Tools that change the user's data, so a call becomes a card instead of running.</summary>
    public IReadOnlySet<string> MutatingTools { get; } = mutatingTools;

    /// <summary>Every tool offered this turn; the scrubber only removes calls naming one of these.</summary>
    public IReadOnlyList<string> ToolNames { get; } = toolNames;

    /// <summary>Changes the model asked for, each waiting on a card for the user's confirmation.</summary>
    public List<ProposedAction> Proposals { get; } = [];

    /// <summary>What the tools actually did, from their own results.</summary>
    public List<ToolReceipt> Receipts { get; } = [];

    /// <summary>How many times the model repeated a call it had already made this turn.</summary>
    public int Repeats { get; private set; }

    public bool IsStuck => Repeats >= StuckAfterRepeats;

    public bool TryRecall(AiToolCall call, out string result) => _answered.TryGetValue(Signature(call), out result!);

    public void Remember(AiToolCall call, string result) => _answered[Signature(call)] = result;

    public void CountRepeat() => Repeats++;

    /// <summary>A call that ran: remembered for the repeat check, and receipted for the user.</summary>
    public void RecordExecution(AiToolCall call, AiToolResult result)
    {
        Remember(call, result.Content);
        Receipts.Add(ToolReceiptBuilder.Build(call.Name, result.Content, result.IsError));
    }

    private static string Signature(AiToolCall call) => call.Name + '|' + call.InputJson.Trim();
}
