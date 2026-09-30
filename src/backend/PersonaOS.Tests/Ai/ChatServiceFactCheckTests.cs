using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Board;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// A reply that gets a task's value points or status wrong. Scripted with the real failure
/// (qwen2.5:3b, 2026-09-27): TASK-1 had 3 points, the model said it had none, and then defended
/// that from its own earlier reply. The data wins: one correction round with the facts, and the
/// facts said outright if the model still gets them wrong.
/// </summary>
public class ChatServiceFactCheckTests
{
    private static (ChatService Chat, FakeAiMessageStreamer Streamer) Setup()
    {
        var db = TestDbContext.Create();
        var sprint = new Sprint { Number = 1, Status = SprintStatuses.Active };
        db.Sprints.Add(sprint);
        db.BoardTasks.Add(new BoardTask
        {
            Number = 1, Title = "5 hours strength training", Points = 3, Status = BoardTaskStatuses.InProgress, Sprint = sprint,
        });
        db.SaveChanges();
        var streamer = new FakeAiMessageStreamer();
        return (TestChat.Create(db, new FakeInstanceConfigService(db), streamer), streamer);
    }

    private static async Task<ChatStreamEvent> DoneAsync(ChatService chat, string message)
    {
        ChatStreamEvent? done = null;
        await foreach (var evt in chat.StreamChatAsync(null, message)) if (evt.Type == "done") done = evt;
        return done!;
    }

    [Fact]
    public async Task A_wrong_fact_the_model_corrects_is_replaced()
    {
        var (chat, streamer) = Setup();
        streamer
            .EnqueueText("The task \"TASK-1\" 5 hours strength training does not have a points value assigned, which makes it unestimated.")
            .EnqueueText("I was wrong earlier: TASK-1 has 3 value points and is in progress.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Equal("I was wrong earlier: TASK-1 has 3 value points and is in progress.", done.Text);
        Assert.Contains(streamer.ReceivedRequests[1].Turns, t => t.Content.Contains("TASK-1 “5 hours strength training” has 3 value points and is In progress."));
    }

    [Fact]
    public async Task A_wrong_fact_that_survives_the_correction_is_taken_out_for_the_facts()
    {
        var (chat, streamer) = Setup();
        streamer
            .EnqueueText("TASK-1 was marked as unassigned with no value points, so it is unestimated.")
            .EnqueueText("Your sprint is on track.\nTASK-1 “5 hours strength training” is unestimated with 3 value points.");

        var done = await DoneAsync(chat, "So why did you say it was not estimated?");

        // The wrong sentence goes (seen live: it sat right above its own correction); the rest stays.
        Assert.Equal("Your sprint is on track.\n\nTo be exact: TASK-1 “5 hours strength training” has 3 value points and is In progress.", done.Text);
    }

    [Fact]
    public async Task A_reply_that_is_all_wrong_becomes_the_facts()
    {
        var (chat, streamer) = Setup();
        streamer
            .EnqueueText("TASK-1 is unestimated.")
            .EnqueueText("TASK-1 is unestimated until you assign a value point.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Equal("TASK-1 “5 hours strength training” has 3 value points and is In progress.", done.Text);
    }

    [Fact]
    public async Task A_task_named_by_title_alone_is_checked()
    {
        var (chat, streamer) = Setup();
        streamer
            .EnqueueText("Task 3, \"5 hours strength training\", is the only one that is not yet estimated.")
            .EnqueueText("I was wrong: every task has value points.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Equal("I was wrong: every task has value points.", done.Text);
        Assert.Contains(streamer.ReceivedRequests[1].Turns, t => t.Content.Contains("TASK-1 “5 hours strength training” has 3 value points"));
    }

    [Fact]
    public async Task Tasks_said_unestimated_with_no_key_are_checked_against_the_running_sprint()
    {
        var (chat, streamer) = Setup();
        streamer
            .EnqueueText("Since all tasks are unestimated, the sprint has no value points yet.")
            .EnqueueText("Every task in SPRINT-1 has value points.");

        var done = await DoneAsync(chat, "How is the sprint so far?");

        Assert.Equal("Every task in SPRINT-1 has value points.", done.Text);
        Assert.Contains(streamer.ReceivedRequests[1].Turns,
            t => t.Content.Contains("SPRINT-1 has 1 task, 0 of it done, and every one has value points (none is unestimated)."));
    }

    [Fact]
    public async Task A_sprint_said_to_have_unestimated_tasks_is_corrected()
    {
        var (chat, streamer) = Setup();
        streamer
            .EnqueueText("SPRINT-1 has tasks that are not yet estimated.")
            .EnqueueText("Every task in SPRINT-1 has value points.");

        var done = await DoneAsync(chat, "which one task is not estimated?");

        Assert.Equal("Every task in SPRINT-1 has value points.", done.Text);
        Assert.Contains(streamer.ReceivedRequests[1].Turns,
            t => t.Content.Contains("SPRINT-1 has 1 task, 0 of it done, and every one has value points (none is unestimated)."));
    }

    [Fact]
    public async Task A_right_fact_costs_nothing_extra()
    {
        var (chat, streamer) = Setup();
        streamer.EnqueueText("TASK-1 5 hours strength training has a value point of 3.");

        var done = await DoneAsync(chat, "Check the current point of the task now");

        Assert.Null(done.Text); // nothing replaced what streamed
        Assert.Single(streamer.ReceivedRequests);
    }
}

public class ItemFactGuardTests
{
    [Theory]
    // The live replies, word for word.
    [InlineData("The task \"TASK-1\" 5 hours strength training does not have a points value assigned, which makes it unestimated.", 3, "in_progress", true)]
    [InlineData("The task \"TASK-1\" 5 hours strength training is unassigned since you haven't provided a value point for it yet.", 3, "in_progress", false)]
    [InlineData("The task \"TASK-1\" 5 hours strength training has a value point of 3.", 3, "in_progress", false)]
    [InlineData("TASK-1 has 5 points.", 3, "in_progress", true)]
    [InlineData("TASK-1 has 3 pts.", 3, "in_progress", false)]
    [InlineData("TASK-1 is not estimated yet.", null, "todo", false)]
    [InlineData("TASK-1 has no value points.", null, "todo", false)]
    [InlineData("TASK-1 and its friends don't have any points assigned yet.", 3, "todo", true)]
    [InlineData("- **TASK-1 File the tax return** - Status: todo, Value: None", 5, "todo", true)]
    [InlineData("- **TASK-1 File the tax return** - Status: todo, Value: 5", 5, "todo", false)]
    // Status: only a claim without a negation counts.
    [InlineData("TASK-1 is done.", 3, "in_progress", true)]
    [InlineData("TASK-1 is not done yet.", 3, "in_progress", false)]
    [InlineData("TASK-1 is in progress.", 3, "in_progress", false)]
    [InlineData("TASK-1 is in the backlog.", 3, "todo", true)]
    public void Checks_what_a_sentence_says_about_one_task(string sentence, int? points, string column, bool wrong)
    {
        Assert.Equal(wrong, ItemFactGuard.Contradicts(sentence, points, column));
    }

    [Theory]
    // The live replies (run 2 of the replay, 2026-09-27) and their opposites.
    [InlineData("SPRINT-1, \"Paperwork week\", has tasks that are not yet estimated as Fibonacci value points (1, 2, 3, 5, 8, 13, 21).", 0, true)]
    [InlineData("I initially mentioned that one task in SPRINT-1, \"Paperwork week\", was not yet estimated.", 0, true)]
    [InlineData("In SPRINT-1, \"Paperwork week\", none of the tasks are unestimated.", 0, false)]
    [InlineData("All tasks in SPRINT-1 have been estimated.", 0, false)]
    [InlineData("All tasks in SPRINT-1 have been estimated.", 2, true)]
    [InlineData("SPRINT-1 has 2 tasks with no value points.", 2, false)]
    [InlineData("SPRINT-1 has 10 points committed.", 0, false)]
    // Run 1 of the replay, with no key, and its opposites.
    [InlineData("Since all tasks are unestimated, the sprint has no value points yet.", 0, true)]
    [InlineData("All tasks are unestimated.", 3, false)]
    [InlineData("All tasks are unestimated.", 1, true)]
    [InlineData("Not all tasks are estimated yet.", 0, true)]
    [InlineData("Every task has value points.", 0, false)]
    [InlineData("Every task has value points.", 1, true)]
    [InlineData("SPRINT-1 has 3 tasks, and every one has value points (none is unestimated).", 0, false)]
    // The third replay: "none … estimated", and a rule read as a claim.
    [InlineData("None of the tasks are currently estimated.", 0, true)]
    [InlineData("So far, there are no estimated tasks yet.", 0, true)]
    [InlineData("None of the tasks in this sprint are yet estimated.", 0, true)]
    [InlineData("None of the tasks have been marked as unestimated yet.", 0, false)]
    [InlineData("Unestimated tasks are not allowed, as every task has a point value.", 0, false)]
    [InlineData("There are no unestimated tasks, as every task in the sprint has a point value.", 0, false)]
    [InlineData("Let's estimate a few unestimated tasks.", 0, false)]
    [InlineData("If any were unestimated, we can review and adjust their value points.", 0, false)]
    // The fourth replay: a "none" in brackets that means every one.
    [InlineData("SPRINT-1 has 3 tasks, all unestimated (none have value points).", 0, true)]
    [InlineData("I earlier noted that all tasks in sprint SPRINT-1 are unestimated (None = unestimated), so they do not have assigned Fibonacci point values.", 0, true)]
    public void Checks_what_a_sentence_says_about_a_sprints_estimates(string sentence, int unestimated, bool wrong)
    {
        Assert.Equal(wrong, ItemFactGuard.SprintContradicts(sentence, 3, unestimated, 0));
    }

    [Theory]
    // Run 2 of the second replay: the correction invented this.
    [InlineData("So far, it has committed 10 value points, of which all 3 tasks have been completed.", 0, true)]
    [InlineData("So far, it has committed 10 value points, of which all 3 tasks have been completed.", 3, false)]
    [InlineData("None of the tasks are done yet.", 0, false)]
    [InlineData("None of the tasks are done yet.", 1, true)]
    [InlineData("Once all tasks are done, the sprint can close.", 0, false)]
    [InlineData("Not all tasks are done.", 1, false)]
    public void Checks_what_a_sentence_says_about_a_sprints_done_tasks(string sentence, int done, bool wrong)
    {
        Assert.Equal(wrong, ItemFactGuard.SprintContradicts(sentence, 3, 0, done));
    }

    [Fact]
    public async Task A_sentence_about_the_backlog_is_not_read_as_the_running_sprint()
    {
        var db = TestDbContext.Create();
        var sprint = new Sprint { Number = 1, Status = SprintStatuses.Active };
        db.Sprints.Add(sprint);
        db.BoardTasks.Add(new BoardTask { Number = 1, Title = "Read chapter 4", Points = 3, Sprint = sprint });
        await db.SaveChangesAsync();
        var draft = new ReplyDraft("Two tasks in the backlog are not estimated yet.", new ChatTurnState("m", new HashSet<string>(), []));

        await new ItemFactGuard(db).ReviewAsync(draft, CancellationToken.None);

        Assert.Empty(draft.WrongFacts);
    }

    [Fact]
    public async Task A_sentence_naming_two_tasks_is_not_pinned_on_either()
    {
        var db = TestDbContext.Create();
        db.BoardTasks.Add(new BoardTask { Number = 1, Title = "A", Points = 3 });
        db.BoardTasks.Add(new BoardTask { Number = 2, Title = "B" });
        await db.SaveChangesAsync();
        var draft = new ReplyDraft("TASK-1 and TASK-2: one of them has no value points.", new ChatTurnState("m", new HashSet<string>(), []));

        await new ItemFactGuard(db).ReviewAsync(draft, CancellationToken.None);

        Assert.Empty(draft.WrongFacts);
    }

    [Fact]
    public void The_fact_reads_the_way_the_board_does()
    {
        Assert.Equal("TASK-7 “File taxes” has no value points yet (unestimated) and is in the backlog.",
            ItemFactGuard.Fact("TASK-7", "File taxes", null, BoardColumns.Backlog));
        Assert.Equal("TASK-2 “Run” has 1 value point and is Done.", ItemFactGuard.Fact("TASK-2", "Run", 1, BoardColumns.Done));
    }
}
