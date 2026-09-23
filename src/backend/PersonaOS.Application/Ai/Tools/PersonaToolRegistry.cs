using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.Ai.Tools;

public class PersonaToolRegistry(
    IEnumerable<IPersonaTool> tools,
    IInstanceConfigService configService,
    ILogger<PersonaToolRegistry> logger) : IPersonaToolRegistry
{
    public async Task<IReadOnlyList<AiToolDefinition>> GetEnabledToolDefinitionsAsync(CancellationToken ct = default)
    {
        var enabled = await GetEnabledToolsAsync(ct);
        return enabled.Select(t => new AiToolDefinition(t.Name, t.Description, t.InputSchemaJson)).ToList();
    }

    public async Task<IReadOnlySet<string>> GetMutatingToolNamesAsync(CancellationToken ct = default)
    {
        var enabled = await GetEnabledToolsAsync(ct);
        // Only enabled tools appear here, so an unknown or disabled name is absent and falls
        // through to ExecuteAsync, which returns a clear error. Gating it instead would show
        // the user a confirm card for an action that cannot happen, and tell the model nothing.
        // There is no safety cost: ExecuteAsync refuses to run anything not in this list.
        return enabled.Where(t => t.Mutates).Select(t => t.Name).ToHashSet();
    }

    public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
    {
        var (tool, input, problem) = await PrepareAsync(call, ct);
        if (tool is null) return Error(call, problem!);

        try
        {
            return new AiToolResult(call.Id, await tool.ExecuteAsync(input, ct));
        }
        // Every module's validation exception derives from this one, and their messages are
        // written for the user. Catching only the goals flavour sent "the tool failed
        // unexpectedly" for a board, planner or reminder problem the model could have fixed.
        catch (DomainValidationException ex)
        {
            return Error(call, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool {Tool} failed", call.Name);
            return Error(call, "The tool failed unexpectedly.");
        }
    }

    public async Task<string?> ValidateAsync(AiToolCall call, CancellationToken ct = default)
    {
        var (tool, input, problem) = await PrepareAsync(call, ct);
        if (tool is null) return problem;

        try
        {
            await tool.ValidateAsync(input, ct);
            return null;
        }
        catch (DomainValidationException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            // A check that breaks must not block the proposal: the user still gets the card, and
            // the tool's own validation runs when they confirm it.
            logger.LogError(ex, "Validating {Tool} failed", call.Name);
            return null;
        }
    }

    /// <summary>
    /// Finds the enabled tool a call names and parses its input. Returns the reason in
    /// <c>Error</c> when either step fails, with a null tool.
    /// </summary>
    private async Task<(IPersonaTool? Tool, JsonElement Input, string? Error)> PrepareAsync(
        AiToolCall call, CancellationToken ct)
    {
        var enabled = await GetEnabledToolsAsync(ct);
        var tool = enabled.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null) return (null, default, $"Unknown or disabled tool '{call.Name}'.");

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson);
            return (tool, doc.RootElement.Clone(), null);
        }
        catch (JsonException)
        {
            return (null, default, "Tool input was not valid JSON.");
        }
    }

    private async Task<IReadOnlyList<IPersonaTool>> GetEnabledToolsAsync(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return tools
            .Where(t => t.RequiredFeature is null
                || config.IsEnabled(t.RequiredFeature))
            .ToList();
    }

    private static AiToolResult Error(AiToolCall call, string message) =>
        new(call.Id, JsonSerializer.Serialize(new { error = message }), IsError: true);
}
