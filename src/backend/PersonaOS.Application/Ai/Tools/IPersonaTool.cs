using System.Text.Json;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai.Tools;

/// <summary>
/// A native Claude tool exposed to the model during chat. Implementations are thin
/// wrappers over domain services: parse the input, call the service, serialize the
/// result. Registered in DI as <see cref="IPersonaTool"/> and dispatched by
/// <see cref="IPersonaToolRegistry"/>.
/// </summary>
public interface IPersonaTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema (object) describing the tool input.</summary>
    string InputSchemaJson { get; }

    /// <summary>Feature toggle gating this tool, or null when always available.</summary>
    string? RequiredFeature { get; }

    /// <summary>
    /// Whether the tool is offered on this install. By default, when its module is on; a tool that
    /// another one replaces while a module is on (remember_about_user, when memory is on) says so here.
    /// </summary>
    bool IsOffered(InstanceConfig config) => RequiredFeature is null || config.IsEnabled(RequiredFeature);

    /// <summary>
    /// True when running this tool changes the user's data. Those are never executed straight
    /// from a model's request: they are proposed to the user and only run once confirmed.
    /// Declared per tool rather than guessed from the name, so a new tool cannot quietly slip
    /// past the gate by being called something unexpected. Defaults to the safe answer.
    /// </summary>
    bool Mutates => true;

    /// <summary>
    /// Whether a call waits on a card for the user's confirmation. Every tool that changes data
    /// does, except where the user chose otherwise: memories are saved straight away while memory
    /// auto-save is on. <see cref="Mutates"/> still says the tool writes, which is what decides
    /// whether it is offered in a mode that must not change anything.
    /// </summary>
    bool NeedsConfirmation(InstanceConfig config) => Mutates;

    /// <summary>Executes the tool and returns a JSON string result for the model.</summary>
    Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    /// <summary>
    /// Checks a proposed call without changing anything, and throws a
    /// <see cref="Common.Exceptions.DomainValidationException"/> when it cannot work — typically
    /// because the item it names does not exist.
    ///
    /// A mutating tool is proposed first and run only after the user confirms, so without this the
    /// user confirmed a card and only then saw "Goal 5 does not exist". Checking at proposal time
    /// hands the model the error while it can still correct itself, in the same turn.
    ///
    /// Tools that create something new have nothing to check and keep the default.
    /// </summary>
    Task ValidateAsync(JsonElement input, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Names the existing item a call acts on, in the user's terms, for its confirmation card:
    /// "“Gym” on 2026-09-23", "GOAL-2 “Learn Rust”". The call's own fields only say
    /// <c>itemId: 2</c>, so without this a card could not show which item a wrong id points at,
    /// and the user would confirm blind. Null for tools that create something new.
    /// </summary>
    Task<string?> DescribeTargetAsync(JsonElement input, CancellationToken ct = default) => Task.FromResult<string?>(null);

    /// <summary>
    /// Fills in what the call left to its default, so the card shows it and confirming later runs
    /// exactly what was shown: "add Buy milk" with no date becomes today's date now, not the day
    /// the user happens to tap Confirm. Returns the input JSON; the default changes nothing.
    /// </summary>
    Task<string> CompleteInputAsync(JsonElement input, CancellationToken ct = default) => Task.FromResult(input.GetRawText());
}

public interface IPersonaToolRegistry
{
    /// <summary>Definitions of every tool whose feature toggle is enabled.</summary>
    Task<IReadOnlyList<AiToolDefinition>> GetEnabledToolDefinitionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Definitions of every enabled tool; without <paramref name="includeWrites"/>, only those that
    /// change nothing (a mode that must not write is not offered a single write tool).
    /// </summary>
    Task<IReadOnlyList<AiToolDefinition>> GetEnabledToolDefinitionsAsync(bool includeWrites, CancellationToken ct = default);

    /// <summary>
    /// Executes a tool call from the model. Never throws: unknown tools, disabled
    /// features, and execution failures come back as error results for the model.
    /// </summary>
    Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default);

    /// <summary>
    /// Checks a call the model wants to make before it is proposed to the user. Returns the
    /// reason it cannot work, or null when it looks fine. Never throws.
    /// </summary>
    Task<string?> ValidateAsync(AiToolCall call, CancellationToken ct = default);

    /// <summary>The item a call acts on, for its confirmation card, or null. Never throws.</summary>
    Task<string?> DescribeTargetAsync(AiToolCall call, CancellationToken ct = default);

    /// <summary>
    /// The call with its defaults filled in (see <see cref="IPersonaTool.CompleteInputAsync"/>);
    /// the call unchanged when the tool has none or completing fails. Never throws.
    /// </summary>
    Task<AiToolCall> CompleteAsync(AiToolCall call, CancellationToken ct = default) => Task.FromResult(call);

    /// <summary>
    /// Names of the enabled tools that change data, and so need the user's confirmation before
    /// they run. Fetched once per turn rather than asked per call, because answering it reads
    /// the instance config.
    /// </summary>
    Task<IReadOnlySet<string>> GetMutatingToolNamesAsync(CancellationToken ct = default);
}
