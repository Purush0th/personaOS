using System.Text.Json;
using PersonaOS.Application.Profile;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Profile;

public class UserProfileServiceTests
{
    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task A_new_instance_knows_nothing_about_the_user()
    {
        var profile = await new UserProfileService(TestDbContext.Create()).GetAsync();

        Assert.Equal(string.Empty, profile.AboutMe);
    }

    [Fact]
    public async Task Saves_the_text_trimmed_and_reads_it_back()
    {
        var db = TestDbContext.Create();
        await new UserProfileService(db).UpdateAsync("  Vegetarian. Lives in Lisbon.  ");

        Assert.Equal("Vegetarian. Lives in Lisbon.", (await new UserProfileService(db).GetAsync()).AboutMe);
    }

    [Fact]
    public async Task Refuses_text_too_long_to_send_with_every_message()
    {
        var error = await Assert.ThrowsAsync<ProfileValidationException>(() =>
            new UserProfileService(TestDbContext.Create()).UpdateAsync(new string('x', UserProfileService.MaxLength + 1)));

        Assert.Contains("every message", error.Message);
    }

    [Fact]
    public async Task Remembering_adds_a_line_and_never_the_same_fact_twice()
    {
        var service = new UserProfileService(TestDbContext.Create());
        await service.UpdateAsync("Vegetarian.");

        await service.RememberAsync("Prefers short answers.");
        var again = await service.RememberAsync("prefers short answers.");

        Assert.Equal("Vegetarian.\nPrefers short answers.", again.AboutMe);
    }

    [Fact]
    public void The_tool_is_a_write_so_it_goes_through_a_card()
    {
        var tool = new RememberAboutUserTool(new UserProfileService(TestDbContext.Create()));

        Assert.True(((PersonaOS.Application.Ai.Tools.IPersonaTool)tool).Mutates);
        Assert.Null(tool.RequiredFeature);
    }

    [Fact]
    public async Task The_tool_refuses_to_remember_nothing()
    {
        var tool = new RememberAboutUserTool(new UserProfileService(TestDbContext.Create()));

        await Assert.ThrowsAsync<ProfileValidationException>(() => tool.ValidateAsync(Input("""{"message":"  "}""")));
    }

    [Fact]
    public async Task The_tool_adds_the_fact_and_the_next_prompt_carries_it()
    {
        var db = TestDbContext.Create();
        await new RememberAboutUserTool(new UserProfileService(db)).ExecuteAsync(Input("""{"message":"Is training for a marathon."}"""));

        var prompt = await new PersonaOS.Application.Ai.SystemPromptBuilder(
            new FakeInstanceConfigService(db), db, TestPrompts.Library()).BuildAsync();

        Assert.Contains("About the user:\nIs training for a marathon.", prompt);
    }
}
