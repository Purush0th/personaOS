using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// Checks a tool call the model made before it runs. A guard lets the call through (null),
/// rewrites it for the guards after it, or answers the model itself so the call never runs.
/// </summary>
public interface IToolCallGuard
{
    /// <summary>Short name, used in logs and metrics.</summary>
    string Name { get; }

    Task<ToolCallVerdict?> CheckAsync(AiToolCall call, ChatTurnState turn, CancellationToken ct);
}

/// <summary>What a guard decided about a call, and why (for the log).</summary>
public abstract record ToolCallVerdict(string Reason)
{
    /// <summary>Carry on with this call instead of the one the model made.</summary>
    public sealed record Rewrite(AiToolCall Call, string Reason) : ToolCallVerdict(Reason);

    /// <summary>Hand the model this result; the call does not run.</summary>
    public sealed record Answer(string Content, bool IsError, string Reason) : ToolCallVerdict(Reason);
}

/// <summary>The pipeline's outcome for one call.</summary>
public abstract record ToolCallDecision(AiToolCall Call)
{
    /// <summary>Every guard let it through: run it.</summary>
    public sealed record Run(AiToolCall Call) : ToolCallDecision(Call);

    /// <summary>A guard answered it; this result goes back to the model instead.</summary>
    public sealed record Answered(AiToolCall Call, AiToolResult Result) : ToolCallDecision(Call);
}
