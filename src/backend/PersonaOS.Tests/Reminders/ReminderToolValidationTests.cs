using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Reminders;
using PersonaOS.Application.Reminders.Tools;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Reminders;

/// <summary>
/// A reminder the server would refuse must never reach a card: qwen2.5:3b proposed one for the day
/// before (2026-10-01), and the card only failed after the user confirmed it.
/// </summary>
public class ReminderToolValidationTests
{
    private static async Task<CreateReminderTool> ToolAsync()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.GetOrCreateAsync();
        var publisher = new ReminderAlarmPublisher(db, new FakePushSender(), config, NullLogger<ReminderAlarmPublisher>.Instance);
        return new CreateReminderTool(new ReminderService(db, config, publisher), TestGoals.Service(db));
    }

    private static JsonElement Input(string message, DateTime local) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { message, dueAtLocal = local.ToString("yyyy-MM-ddTHH:mm:ss") })).RootElement.Clone();

    [Fact]
    public async Task A_time_in_the_past_is_refused_before_the_card_and_says_what_time_it_is()
    {
        var tool = await ToolAsync();

        var ex = await Assert.ThrowsAsync<ReminderValidationException>(
            () => tool.ValidateAsync(Input("Consult a professional", DateTime.UtcNow.AddDays(-1))));

        Assert.Contains("must be in the future; it is now", ex.Message);
    }

    [Fact]
    public async Task A_time_ahead_is_fine()
    {
        var tool = await ToolAsync();

        await tool.ValidateAsync(Input("Consult a professional", DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public async Task A_goal_is_linked_by_its_key_and_an_unknown_key_says_what_to_do()
    {
        var tool = await ToolAsync();
        var due = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss");

        var ex = await Assert.ThrowsAsync<ReminderValidationException>(() => tool.ValidateAsync(
            JsonDocument.Parse(JsonSerializer.Serialize(new { message = "Call mum", dueAtLocal = due, goalKey = "GOAL-9" })).RootElement.Clone()));

        Assert.Contains("There is no goal GOAL-9", ex.Message);
        Assert.Contains("leave it out", ex.Message);
    }

    [Fact]
    public async Task A_reminder_with_no_message_is_refused_before_the_card()
    {
        var tool = await ToolAsync();

        await Assert.ThrowsAsync<ReminderValidationException>(() => tool.ValidateAsync(Input(" ", DateTime.UtcNow.AddDays(1))));
    }
}
