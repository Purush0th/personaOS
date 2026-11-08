using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.LocalModels;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.LocalModels;

public class LocalModelServiceTests
{
    private static readonly LocalModelOptions Options = new() { Reference = "hf.co/owner/model-GGUF:Q4_K_M", Name = "personaos-model:4b" };

    private static LocalModelService Service(FakeLocalModelHost host, Action<InstanceConfig>? config = null)
    {
        var configService = new FakeInstanceConfigService(TestDbContext.Create());
        if (config is not null) configService.UpdateAsync(config).GetAwaiter().GetResult();
        return new LocalModelService(host, configService, Options);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434")]
    [InlineData("http://ollama:11434/v1/", "http://ollama:11434")]
    [InlineData(" http://192.168.2.10:11434/ ", "http://192.168.2.10:11434")]
    public void The_native_api_is_at_the_root(string address, string root)
    {
        Assert.Equal(root, LocalModelService.RootOf(address));
    }

    [Fact]
    public async Task With_no_address_the_usual_places_are_tried_in_order()
    {
        var host = new FakeLocalModelHost("http://localhost:11434");

        var status = await Service(host).StatusAsync(null);

        Assert.True(status.Found);
        Assert.Equal("http://localhost:11434", status.Address);
        Assert.False(status.Installed);
        Assert.Equal(["http://ollama:11434", "http://localhost:11434"], host.Probed);
    }

    [Fact]
    public async Task The_stored_ollama_address_is_tried_first()
    {
        var host = new FakeLocalModelHost("http://192.168.2.10:11434");

        var status = await Service(host, c => { c.AiProvider = InstanceConfig.Providers.OpenAiCompatible; c.AiBaseUrl = "http://192.168.2.10:11434/v1"; })
            .StatusAsync(null);

        Assert.Equal("http://192.168.2.10:11434", status.Address);
        Assert.Equal(["http://192.168.2.10:11434"], host.Probed);
    }

    [Fact]
    public async Task A_given_address_is_the_only_one_tried()
    {
        var host = new FakeLocalModelHost("http://localhost:11434");

        var status = await Service(host).StatusAsync("http://box:11434/v1");

        Assert.False(status.Found);
        Assert.Equal("http://box:11434", status.Address);
        Assert.Equal(["http://box:11434"], host.Probed);
    }

    [Fact]
    public async Task An_installed_model_is_reported_by_its_short_name()
    {
        var host = new FakeLocalModelHost("http://ollama:11434");
        host.Models.Add(Options.Name);

        var status = await Service(host).StatusAsync(null);

        Assert.True(status.Installed);
        Assert.Equal(Options.Name, status.Model);
    }

    [Fact]
    public async Task A_model_with_only_its_hugging_face_name_is_named_without_downloading_again()
    {
        var host = new FakeLocalModelHost("http://ollama:11434");
        host.Models.Add(Options.Reference);
        Assert.False((await Service(host).StatusAsync(null)).Installed);

        await foreach (var _ in Service(host).PullAsync("http://ollama:11434")) { }

        Assert.Empty(host.Pulled);
        Assert.Equal([(Options.Reference, Options.Name)], host.Copied);
        Assert.True((await Service(host).StatusAsync(null)).Installed);
    }

    [Fact]
    public async Task Pull_downloads_the_pinned_model_then_gives_it_its_short_name()
    {
        var host = new FakeLocalModelHost("http://ollama:11434");

        var steps = new List<ModelPullProgress>();
        await foreach (var step in Service(host).PullAsync("http://ollama:11434/v1")) steps.Add(step);

        Assert.Equal([Options.Reference], host.Pulled);
        Assert.Equal([(Options.Reference, Options.Name)], host.Copied);
        Assert.Equal("success", steps[^1].Status);
        Assert.True((await Service(host).StatusAsync("http://ollama:11434")).Installed);
    }

    [Fact]
    public async Task Pull_refuses_an_address_with_no_ollama()
    {
        var host = new FakeLocalModelHost();

        await Assert.ThrowsAsync<LocalModelHostException>(async () =>
        {
            await foreach (var _ in Service(host).PullAsync("http://nowhere:11434")) { }
        });
        Assert.Empty(host.Pulled);
    }
}
