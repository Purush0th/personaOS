using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Application.Goals;

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

    public async Task<AiToolResult> ExecuteAsync(AiToolCall call, CancellationToken ct = default)
    {
        var enabled = await GetEnabledToolsAsync(ct);
        var tool = enabled.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null)
        {
            return Error(call, $"Unknown or disabled tool '{call.Name}'.");
        }

        JsonElement input;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.InputJson) ? "{}" : call.InputJson);
            input = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Error(call, "Tool input was not valid JSON.");
        }

        try
        {
            return new AiToolResult(call.Id, await tool.ExecuteAsync(input, ct));
        }
        catch (GoalValidationException ex)
        {
            return Error(call, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool {Tool} failed", call.Name);
            return Error(call, "The tool failed unexpectedly.");
        }
    }

    private async Task<IReadOnlyList<IPersonaTool>> GetEnabledToolsAsync(CancellationToken ct)
    {
        var config = await configService.GetOrCreateAsync(ct);
        return tools
            .Where(t => t.RequiredFeature is null
                || (config.Features.TryGetValue(t.RequiredFeature, out var on) && on))
            .ToList();
    }

    private static AiToolResult Error(AiToolCall call, string message) =>
        new(call.Id, JsonSerializer.Serialize(new { error = message }), IsError: true);
}
