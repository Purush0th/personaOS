using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Notes;
using PersonaOS.Application.Notes.Tools;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Notes;

/// <summary>
/// Notes: things jotted down to deal with later, as in Google Keep. Plain text; the way out is a
/// task in the backlog.
/// </summary>
public class NoteTests
{
    private sealed record Rig(TestDbContext Db, NoteService Notes, FixedTimeProvider Clock, FakeInstanceConfigService Config);

    private static Rig Setup()
    {
        var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero));
        var config = new FakeInstanceConfigService(db);
        var board = new BoardService(db, config, new FakePushSender(), clock, NullLogger<BoardService>.Instance);
        return new Rig(db, new NoteService(db, board, config, clock), clock, config);
    }

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task A_note_is_kept_trimmed_and_the_latest_edited_comes_first()
    {
        var rig = Setup();
        var first = await rig.Notes.CreateAsync("  Call the plumber  \n");
        rig.Clock.Now = rig.Clock.Now.AddMinutes(1);
        await rig.Notes.CreateAsync("Buy batteries");
        rig.Clock.Now = rig.Clock.Now.AddMinutes(1);
        await rig.Notes.UpdateAsync(first.Id, "Call the plumber about the leak");

        var all = await rig.Notes.ListAsync();

        Assert.Equal(["Call the plumber about the leak", "Buy batteries"], all.Select(n => n.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public async Task An_empty_note_is_refused(string text)
    {
        var rig = Setup();

        await Assert.ThrowsAsync<NoteValidationException>(() => rig.Notes.CreateAsync(text));
    }

    [Fact]
    public async Task A_note_longer_than_the_limit_is_refused_and_says_how_long()
    {
        var rig = Setup();

        var error = await Assert.ThrowsAsync<NoteValidationException>(() => rig.Notes.CreateAsync(new string('x', Note.MaxLength + 1)));

        Assert.Contains("2001", error.Message);
    }

    [Fact]
    public async Task Search_ignores_case_in_any_script()
    {
        var rig = Setup();
        await rig.Notes.CreateAsync("Call the PLUMBER");
        await rig.Notes.CreateAsync("Ärztin anrufen");
        await rig.Notes.CreateAsync("Buy batteries");

        Assert.Equal("Call the PLUMBER", (await rig.Notes.ListAsync("plumber")).Single().Text);
        Assert.Equal("Ärztin anrufen", (await rig.Notes.ListAsync("ärztin")).Single().Text);
    }

    [Fact]
    public async Task Deleting_a_note_that_is_not_there_says_so()
    {
        var rig = Setup();
        var note = await rig.Notes.CreateAsync("Buy batteries");

        Assert.True(await rig.Notes.DeleteAsync(note.Id));
        Assert.False(await rig.Notes.DeleteAsync(note.Id));
        Assert.Null(await rig.Notes.UpdateAsync(note.Id, "again"));
    }

    [Fact]
    public async Task Moving_to_the_backlog_makes_a_task_of_the_first_line_and_removes_the_note()
    {
        var rig = Setup();
        var note = await rig.Notes.CreateAsync("Fix the balcony door\nThe hinge squeaks.\nBuy oil first.");

        var task = await rig.Notes.MoveToBacklogAsync(note.Id);

        Assert.Equal("Fix the balcony door", task!.Title);
        Assert.Equal("The hinge squeaks.\nBuy oil first.", task.Description);
        Assert.Null(task.SprintKey);
        Assert.Empty(await rig.Db.Notes.ToListAsync());
    }

    [Fact]
    public void A_first_line_too_long_for_a_title_is_cut_at_a_word_and_the_note_kept_whole()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 60));

        var (title, description) = NoteService.TaskFrom(text);

        Assert.True(title.Length <= NoteService.MaxTitleLength + 1);
        Assert.EndsWith("word…", title);
        Assert.Equal(text, description);
    }

    [Fact]
    public async Task With_the_board_off_a_note_stays_a_note()
    {
        var rig = Setup();
        var note = await rig.Notes.CreateAsync("Fix the door");
        await rig.Config.UpdateAsync(c => c.Features[InstanceConfig.Modules.Board] = false);

        await Assert.ThrowsAsync<NoteValidationException>(() => rig.Notes.MoveToBacklogAsync(note.Id));
        Assert.Single(await rig.Db.Notes.ToListAsync());
    }

    // --- Tools -------------------------------------------------------------------------

    [Fact]
    public async Task A_note_from_chat_is_saved_without_a_card_and_receipted_with_its_text()
    {
        // Owner, 2026-10-10: a note from chat saves straight away.
        var rig = Setup();
        var config = await rig.Config.GetOrCreateAsync();
        var tool = new CreateNoteTool(rig.Notes);

        Assert.False(tool.NeedsConfirmation(config));
        var result = await tool.ExecuteAsync(Input("""{"text":"Call the plumber"}"""));

        Assert.Contains("\"content\":\"Call the plumber\"", result);
        Assert.Equal("Call the plumber", PersonaOS.Application.Ai.ToolReceiptBuilder.Build("create_note", result, false).Summary);
        await Assert.ThrowsAsync<NoteValidationException>(() => tool.ValidateAsync(Input("""{"text":" "}""")));
    }

    [Fact]
    public async Task Moving_a_note_from_chat_is_a_card_naming_the_note_and_only_offered_with_the_board()
    {
        var rig = Setup();
        var config = await rig.Config.GetOrCreateAsync();
        var note = await rig.Notes.CreateAsync("Fix the balcony door\nThe hinge squeaks.");
        var tool = new MoveNoteToBacklogTool(rig.Notes);

        Assert.True(tool.NeedsConfirmation(config));
        Assert.Equal("“Fix the balcony door”", await tool.DescribeTargetAsync(Input($$"""{"noteId":{{note.Id}}}""")));
        await Assert.ThrowsAsync<NoteValidationException>(() => tool.ValidateAsync(Input("""{"noteId":999}""")));

        var result = await tool.ExecuteAsync(Input($$"""{"noteId":"#{{note.Id}}"}"""));
        Assert.Contains("TASK-", result);

        config.Features[InstanceConfig.Modules.Board] = false;
        Assert.False(tool.IsOffered(config));
    }

    [Fact]
    public async Task Reading_notes_from_chat_lists_them_with_their_ids()
    {
        var rig = Setup();
        var note = await rig.Notes.CreateAsync("Buy batteries");

        var result = await new GetNotesTool(rig.Notes).ExecuteAsync(Input("{}"));

        Assert.Contains($"\"noteId\":{note.Id}", result);
        Assert.Contains("\"total\":1", result);
    }
}
