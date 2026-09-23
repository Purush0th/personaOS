using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// A module switched off in Settings must be gone for the model: not offered, not runnable even if
/// the model names it anyway, and never counted among the tools that need a card.
/// </summary>
public class PersonaToolRegistryTests
{
    private static async Task<(PersonaToolRegistry Registry, FakeTool Docs)> SetupWithDocsOff()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.UpdateAsync(c => c.Features[InstanceConfig.Modules.Docs] = false);
        var docs = new FakeTool("delete_document", requiredFeature: InstanceConfig.Modules.Docs, mutates: true);
        var goals = new FakeTool("create_goal", requiredFeature: InstanceConfig.Modules.Goals, mutates: true);
        var always = new FakeTool("remember_about_user", mutates: true);
        return (new PersonaToolRegistry([docs, goals, always], config, NullLogger<PersonaToolRegistry>.Instance), docs);
    }

    [Fact]
    public async Task A_switched_off_module_offers_no_tools()
    {
        var (registry, _) = await SetupWithDocsOff();

        Assert.Equal(["create_goal", "remember_about_user"], (await registry.GetEnabledToolDefinitionsAsync()).Select(t => t.Name));
    }

    [Fact]
    public async Task A_switched_off_tool_does_not_run_even_when_the_model_names_it()
    {
        var (registry, docs) = await SetupWithDocsOff();

        var result = await registry.ExecuteAsync(new AiToolCall("1", "delete_document", "{}"));

        Assert.True(result.IsError);
        Assert.Contains("Unknown or disabled tool", result.Content);
        Assert.Empty(docs.Invocations);
        Assert.NotNull(await registry.ValidateAsync(new AiToolCall("1", "delete_document", "{}")));
    }

    [Fact]
    public async Task Only_enabled_writes_need_a_card()
    {
        var (registry, _) = await SetupWithDocsOff();

        Assert.Equal(["create_goal", "remember_about_user"], (await registry.GetMutatingToolNamesAsync()).Order());
    }

    [Fact]
    public async Task Input_that_is_not_json_comes_back_as_an_error_for_the_model()
    {
        var (registry, _) = await SetupWithDocsOff();

        var result = await registry.ExecuteAsync(new AiToolCall("1", "create_goal", "{not json"));

        Assert.True(result.IsError);
        Assert.Contains("not valid JSON", result.Content);
    }
}
