using System.Text.Json;
using PersonaOS.Application.Common.Interfaces;

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
    /// True when running this tool changes the user's data. Those are never executed straight
    /// from a model's request: they are proposed to the user and only run once confirmed.
    /// Declared per tool rather than guessed from the name, so a new tool cannot quietly slip
    /// past the gate by being called something unexpected. Defaults to the safe answer.
    /// </summary>
    bool Mutates => true;

    /// <summary>Executes the tool and returns a JSON string result for the model.</summary>
    Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);
}

public interface IPersonaToolRegistry
{
    /// <summary>Definitions of every tool whose feature toggle is enabled.</summary>
    Task<IReadOnlyList<AiToolDefinition>> GetEnabledToolDefinitionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes a tool call from the model. Never throws: unknown tools, disabled
    /// features, and execution failures come back as error results for the model.
    /// </summary>
    Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default);

    /// <summary>
    /// Names of the enabled tools that change data, and so need the user's confirmation before
    /// they run. Fetched once per turn rather than asked per call, because answering it reads
    /// the instance config.
    /// </summary>
    Task<IReadOnlySet<string>> GetMutatingToolNamesAsync(CancellationToken ct = default);
}
