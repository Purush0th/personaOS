using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;
using PersonaOS.Application.WorkItems;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Board;

/// <summary>
/// Comments and attachments hang off both tasks and goals, addressed the way the user sees them:
/// by key. A wrong key must fail loudly rather than attach a file to whatever happens to have that id.
/// </summary>
public class WorkItemServiceTests
{
    private sealed record Rig(
        TestDbContext Db, WorkItemService Items, BoardService Board, GoalService Goals, FakeDocumentStorage Storage);

    private static Rig Setup()
    {
        var db = TestDbContext.Create();
        var storage = new FakeDocumentStorage();
        var board = new BoardService(
            db, new FakeInstanceConfigService(db), new FakePushSender(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<BoardService>.Instance);
        return new Rig(db, new WorkItemService(db, storage), board, new GoalService(db), storage);
    }

    private static AddAttachmentRequest File(string name, string text, string contentType = "text/plain") =>
        new(name, contentType, new MemoryStream(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task Comments_belong_to_the_item_the_key_names()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Write the letter"));
        await rig.Goals.CreateAsync(new CreateGoalRequest("Get masters", null, GoalPeriods.Year, new DateOnly(2026, 1, 1)));

        var task = (await rig.Items.ResolveAsync(WorkItemTypes.Task, "TASK-1"))!;
        var goal = (await rig.Items.ResolveAsync(WorkItemTypes.Goal, "GOAL-1"))!;
        await rig.Items.AddCommentAsync(task, new AddCommentRequest("Draft is in the shared folder."));
        await rig.Items.AddCommentAsync(goal, new AddCommentRequest("Deadline moved.", CommentAuthors.Assistant));

        var onTask = Assert.Single(await rig.Items.ListCommentsAsync(task));
        var onGoal = Assert.Single(await rig.Items.ListCommentsAsync(goal));
        Assert.Equal("Draft is in the shared folder.", onTask.Body);
        Assert.Equal(CommentAuthors.User, onTask.Author);
        Assert.Equal(CommentAuthors.Assistant, onGoal.Author);
        Assert.Equal("Write the letter", task.Title);
    }

    [Fact]
    public async Task A_key_that_names_nothing_resolves_to_nothing()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Only task"));

        Assert.Null(await rig.Items.ResolveAsync(WorkItemTypes.Task, "TASK-9"));
        // A goal key must not quietly reach the task with that number.
        Assert.Null(await rig.Items.ResolveAsync(WorkItemTypes.Task, "GOAL-1"));
        await Assert.ThrowsAsync<WorkItemValidationException>(() => rig.Items.ResolveAsync("sprint", "SPRINT-1"));
    }

    [Fact]
    public async Task A_comment_can_be_edited_and_deleted_and_is_never_empty()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Task"));
        var task = (await rig.Items.ResolveAsync(WorkItemTypes.Task, "TASK-1"))!;

        await Assert.ThrowsAsync<WorkItemValidationException>(() =>
            rig.Items.AddCommentAsync(task, new AddCommentRequest("   ")));

        var comment = await rig.Items.AddCommentAsync(task, new AddCommentRequest("First thought"));
        // A fresh comment is not an edited one, so the UI has nothing to flag.
        Assert.Equal(comment.CreatedAtUtc, comment.UpdatedAtUtc);

        var edited = await rig.Items.UpdateCommentAsync(comment.Id, "Second thought");
        Assert.Equal("Second thought", edited!.Body);
        Assert.True(edited.UpdatedAtUtc > edited.CreatedAtUtc);

        Assert.True(await rig.Items.DeleteCommentAsync(comment.Id));
        Assert.Empty(await rig.Items.ListCommentsAsync(task));
    }

    [Fact]
    public async Task An_attachment_stores_its_bytes_and_comes_back_for_download()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Task"));
        var task = (await rig.Items.ResolveAsync(WorkItemTypes.Task, "TASK-1"))!;

        var saved = await rig.Items.AddAttachmentAsync(task, File("quote.pdf", "%PDF-1.7 fake", "application/pdf"));

        Assert.Equal("quote.pdf", saved.FileName);
        Assert.Equal("application/pdf", saved.ContentType);
        Assert.Equal(13, saved.SizeBytes);
        Assert.Single(rig.Storage.Files);

        var download = await rig.Items.DownloadAttachmentAsync(saved.Id);
        using var reader = new StreamReader(download!.Content);
        Assert.Equal("%PDF-1.7 fake", await reader.ReadToEndAsync());
        Assert.Equal("quote.pdf", download.FileName);
    }

    [Fact]
    public async Task A_path_in_a_file_name_never_reaches_disk_and_empty_files_are_refused()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Task"));
        var task = (await rig.Items.ResolveAsync(WorkItemTypes.Task, "TASK-1"))!;

        var saved = await rig.Items.AddAttachmentAsync(task, File("../../etc/passwd", "x"));
        Assert.Equal("passwd", saved.FileName);
        Assert.DoesNotContain(rig.Storage.Files.Keys, name => name.Contains("passwd"));

        await Assert.ThrowsAsync<WorkItemValidationException>(() =>
            rig.Items.AddAttachmentAsync(task, File("empty.txt", string.Empty)));
        Assert.Single(rig.Storage.Files); // the empty upload left nothing behind
    }

    [Fact]
    public async Task Deleting_an_attachment_removes_the_bytes_too()
    {
        var rig = Setup();
        await rig.Board.CreateTaskAsync(new CreateTaskRequest("Task"));
        var task = (await rig.Items.ResolveAsync(WorkItemTypes.Task, "TASK-1"))!;
        var saved = await rig.Items.AddAttachmentAsync(task, File("note.txt", "hello"));

        Assert.True(await rig.Items.DeleteAttachmentAsync(saved.Id));

        Assert.Empty(await rig.Items.ListAttachmentsAsync(task));
        Assert.Empty(rig.Storage.Files);
    }

    [Fact]
    public async Task A_task_carries_its_comment_and_attachment_counts_and_loses_them_when_deleted()
    {
        var rig = Setup();
        var created = await rig.Board.CreateTaskAsync(new CreateTaskRequest("Task"));
        var task = (await rig.Items.ResolveAsync(WorkItemTypes.Task, created.Key))!;
        await rig.Items.AddCommentAsync(task, new AddCommentRequest("A note"));
        await rig.Items.AddAttachmentAsync(task, File("note.txt", "hello"));

        var detail = (await rig.Board.GetTaskAsync(created.Id))!;
        Assert.Equal(1, detail.Task.CommentCount);
        Assert.Equal(1, detail.Task.AttachmentCount);
        Assert.Single(detail.Comments);
        Assert.Single(detail.Attachments);

        await rig.Board.DeleteTaskAsync(created.Id);

        Assert.Empty(rig.Db.WorkItemComments);
        Assert.Empty(rig.Db.WorkItemAttachments);
    }
}
