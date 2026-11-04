using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Board;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// Owner's chat b4c7pvc2 (2026-11-04): asked three times to start the planned sprint, qwen2.5:3b
/// said "The sprint “Rebound” is now in progress" and never called start_sprint.
/// </summary>
public class SprintStartTests
{
    [Theory]
    [InlineData("okay I need to start the sprint that is currently in the backlog could you start it")]
    [InlineData("Start the sprint")]
    [InlineData("Please begin my next sprint")]
    [InlineData("kick off the planned sprint")]
    public void A_plain_request_to_start_the_sprint_is_read(string message)
    {
        Assert.Equal(new SprintCommand.StartRequest(null), SprintCommand.Parse(message, []));
    }

    [Fact]
    public void A_sprint_named_by_key_is_the_one_started()
    {
        Assert.Equal("SPRINT-2", SprintCommand.Parse("start SPRINT-2", [])!.SprintKey);
    }

    [Fact]
    public void Start_it_counts_only_after_talking_about_a_sprint()
    {
        Assert.NotNull(SprintCommand.Parse("Just start it.", ["Can you check the current sprint status?"]));
        Assert.Null(SprintCommand.Parse("Just start it.", ["Set a timer for my run"]));
    }

    [Theory]
    [InlineData("How do I start a sprint?")]
    [InlineData("When should I start the sprint?")]
    [InlineData("Don't start the sprint yet")]
    [InlineData("Start SPRINT-2 and SPRINT-3")]
    public void Questions_and_negatives_are_not_requests(string message)
    {
        Assert.Null(SprintCommand.Parse(message, []));
    }

    [Theory]
    [InlineData("The sprint “Rebound” is now in progress.")]
    [InlineData("I have started SPRINT-2 for you.")]
    [InlineData("SPRINT-2 has been started.")]
    [InlineData("I will now start the sprint “Rebound” for you.")]
    public void Saying_a_sprint_started_is_a_claim(string reply)
    {
        Assert.NotNull(ActionClaimDetector.FindClaim(reply));
    }

    [Theory]
    [InlineData("SPRINT-2 is planned and has not started yet.")]
    [InlineData("SPRINT-1 is currently running until Sunday.")]
    [InlineData("Would you like me to start the sprint?")]
    public void Describing_a_sprint_is_not_a_claim(string reply)
    {
        Assert.Null(ActionClaimDetector.FindClaim(reply));
    }

    [Fact]
    public async Task The_planned_sprint_is_proposed_when_the_model_only_says_it_started()
    {
        var db = TestDbContext.Create();
        db.Sprints.Add(new Sprint { Number = 2, Name = "Rebound", Status = SprintStatuses.Planned });
        await db.SaveChangesAsync();
        var streamer = new FakeAiMessageStreamer();
        for (var i = 0; i < 10; i++) streamer.EnqueueText("The sprint “Rebound” is now in progress.");
        var start = new FakeTool("start_sprint", mutates: true);
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)), [start]);

        ChatStreamEvent? done = null;
        await foreach (var evt in chat.StreamChatAsync(null, "Yeah, the sprint is already there just start it"))
            if (evt.Type == "done") done = evt;

        var card = Assert.Single(done!.Pending!);
        Assert.Equal("start_sprint", card.Tool);
        Assert.Contains("SPRINT-2", card.Summary);
        Assert.Equal(EmptyReplyGuard.ProposalOnly, done.Text);
        Assert.Empty(start.Invocations);
    }

    [Fact]
    public async Task With_a_sprint_already_running_the_reply_says_so()
    {
        var db = TestDbContext.Create();
        db.Sprints.Add(new Sprint { Number = 1, Status = SprintStatuses.Active });
        await db.SaveChangesAsync();
        var streamer = new FakeAiMessageStreamer();
        for (var i = 0; i < 10; i++) streamer.EnqueueText("The sprint is now in progress.");
        var chat = TestChat.Create(db, new FakeInstanceConfigService(db), streamer,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)), [new FakeTool("start_sprint", mutates: true)]);

        ChatStreamEvent? done = null;
        await foreach (var evt in chat.StreamChatAsync(null, "Start the sprint"))
            if (evt.Type == "done") done = evt;

        Assert.StartsWith("SPRINT-1 is already running", done!.Text);
        Assert.Null(done.UnverifiedClaim);
    }
}
