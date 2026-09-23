using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Proactive;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Proactive;

/// <summary>
/// Rewording a brief is an enhancement that must never cost a fact: the model's version is used
/// only when every item, time and number of the composed brief is still in it.
/// </summary>
public class BriefPhraserTests
{
    private const string Brief =
        "Good morning. Here's Wednesday, 23 Sep.\n\nOn your plan (2):\n• 07:30 — Gym\n• Read chapter 4\n\n"
        + "Reminders today:\n• 19:00 — Call mum\n\nGoals worth a nudge:\n• Ship PersonaOS (40%)";

    private static async Task<(BriefPhraser Phraser, FakeAiMessageStreamer Streamer, PersonaOS.Domain.Entities.InstanceConfig Config)> Setup(
        bool enabled = true)
    {
        var db = TestDbContext.Create();
        var configService = new FakeInstanceConfigService(db);
        var config = await configService.GetOrCreateAsync();
        config.PhraseBriefs = enabled;
        var streamer = new FakeAiMessageStreamer();
        var phraser = new BriefPhraser(configService, new FakeAiMessageStreamerFactory(streamer), TestPrompts.Library(),
            NullLogger<BriefPhraser>.Instance);
        return (phraser, streamer, config);
    }

    [Fact]
    public void A_rewording_that_keeps_every_item_time_and_number_is_accepted()
    {
        const string phrased = "Morning! It's Wednesday 23 Sep. Two things on the plan (2): Gym at 07:30, then Read chapter 4. "
            + "At 19:00, Call mum. Ship PersonaOS is at 40% — worth a nudge.";

        Assert.True(BriefPhraser.KeepsEveryFact(Brief, phrased));
    }

    [Theory]
    [InlineData("Morning! 23 Sep, (2): Gym at 07:30, Read chapter 4, Ship PersonaOS 40%.")] // dropped the reminder
    [InlineData("Morning! 23 Sep (2): Gym at 7:30, Read chapter 4, 19:00 Call mum, Ship PersonaOS 40%.")] // changed a time
    [InlineData("Morning! 23 Sep (2): Gym 07:30, Read chapter 4, 19:00 Call mum, Ship PersonaOS 45%.")] // changed a figure
    [InlineData("")]
    public void A_rewording_that_drops_or_changes_a_fact_is_refused(string phrased)
    {
        Assert.False(BriefPhraser.KeepsEveryFact(Brief, phrased));
    }

    [Fact]
    public void A_rewording_that_rambles_is_refused()
    {
        Assert.False(BriefPhraser.KeepsEveryFact(Brief, Brief + new string(' ', Brief.Length * 3)));
    }

    [Fact]
    public async Task Switched_off_it_never_calls_the_model()
    {
        var (phraser, streamer, config) = await Setup(enabled: false);

        Assert.Null(await phraser.PhraseAsync(config, Brief));
        Assert.Equal(0, streamer.CallCount);
    }

    [Fact]
    public async Task Switched_on_it_sends_the_brief_and_uses_a_faithful_rewording()
    {
        var (phraser, streamer, config) = await Setup();
        const string phrased = "Morning! Wednesday 23 Sep. On the plan (2): Gym at 07:30 and Read chapter 4. "
            + "19:00: Call mum. Ship PersonaOS sits at 40%.";
        streamer.EnqueueText(phrased);

        Assert.Equal(phrased, await phraser.PhraseAsync(config, Brief));
        var request = Assert.Single(streamer.ReceivedRequests);
        Assert.Contains("You are Friday", request.SystemPrompt);
        Assert.Equal(Brief, request.Turns.Single().Content);
        Assert.Empty(request.Tools);
    }

    [Fact]
    public async Task A_model_that_fails_leaves_the_brief_as_composed()
    {
        var (phraser, streamer, config) = await Setup();
        streamer.ThrowOnFirstCall = new AiStreamException("offline");

        Assert.Null(await phraser.PhraseAsync(config, Brief));
    }

    [Fact]
    public async Task A_model_that_invents_or_drops_leaves_the_brief_as_composed()
    {
        var (phraser, streamer, config) = await Setup();
        streamer.EnqueueText("Busy day! Don't forget your dentist at 10:00.");

        Assert.Null(await phraser.PhraseAsync(config, Brief));
    }
}
