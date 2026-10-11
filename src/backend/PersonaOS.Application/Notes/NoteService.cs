using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Board;
using PersonaOS.Application.Common.Exceptions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Notes;

public record NoteDto(int Id, string Text, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public class NoteValidationException(string message) : DomainValidationException(message, "note_invalid");

public interface INoteService
{
    /// <summary>Every note, the latest edited first, optionally only those containing <paramref name="search"/>.</summary>
    Task<IReadOnlyList<NoteDto>> ListAsync(string? search = null, CancellationToken ct = default);

    Task<NoteDto?> GetAsync(int id, CancellationToken ct = default);

    Task<NoteDto> CreateAsync(string text, CancellationToken ct = default);

    /// <summary>Replaces the text; null when there is no such note.</summary>
    Task<NoteDto?> UpdateAsync(int id, string text, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Turns the note into a task in the board's backlog, its first line the title and the rest
    /// the description, and removes the note. Null when there is no such note.
    /// </summary>
    Task<BoardTaskDto?> MoveToBacklogAsync(int id, CancellationToken ct = default);
}

public class NoteService(IAppDbContext db, IBoardService board, IInstanceConfigService configService, TimeProvider time)
    : INoteService
{
    /// <summary>The longest task title made from a note's first line; a longer line goes whole to the description.</summary>
    public const int MaxTitleLength = 200;

    public async Task<IReadOnlyList<NoteDto>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var notes = await db.Notes.AsNoTracking()
            .OrderByDescending(n => n.UpdatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Select(n => new NoteDto(n.Id, n.Text, n.CreatedAtUtc, n.UpdatedAtUtc))
            .ToListAsync(ct);

        // Notes are few and short, so the search runs here, ignoring case in any script (SQLite's
        // own matching would not, past ASCII).
        var words = search?.Trim();
        return string.IsNullOrEmpty(words)
            ? notes
            : notes.Where(n => n.Text.Contains(words, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<NoteDto?> GetAsync(int id, CancellationToken ct = default) =>
        await db.Notes.AsNoTracking()
            .Where(n => n.Id == id)
            .Select(n => new NoteDto(n.Id, n.Text, n.CreatedAtUtc, n.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);

    public async Task<NoteDto> CreateAsync(string text, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var note = new Note { Text = RequireText(text), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return Map(note);
    }

    public async Task<NoteDto?> UpdateAsync(int id, string text, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return null;

        var value = RequireText(text);
        if (value != note.Text)
        {
            note.Text = value;
            note.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        return Map(note);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return false;
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BoardTaskDto?> MoveToBacklogAsync(int id, CancellationToken ct = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return null;
        if (!(await configService.GetOrCreateAsync(ct)).IsEnabled(InstanceConfig.Modules.Board))
            throw new NoteValidationException("The board is switched off, so there is no backlog to move the note to.");

        var (title, description) = TaskFrom(note.Text);
        // The task first: if the board refuses it, the note is still there.
        var task = await board.CreateTaskAsync(new CreateTaskRequest(title, description), ct);
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// A task's title and description from a note: the first line is the title and the rest the
    /// description. A first line too long for a title is cut at a word for the title, and the
    /// whole note goes to the description so nothing is lost.
    /// </summary>
    public static (string Title, string? Description) TaskFrom(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var first = lines[0].Trim();
        var rest = string.Join('\n', lines.Skip(1)).Trim();

        if (first.Length <= MaxTitleLength)
            return (first, rest.Length == 0 ? null : rest);

        var cut = first[..MaxTitleLength];
        var space = cut.LastIndexOf(' ');
        if (space > MaxTitleLength / 2) cut = cut[..space];
        return (cut.TrimEnd() + "…", text.Trim());
    }

    private static string RequireText(string? text)
    {
        // Leading blank lines would leave the first line, a task's title, empty.
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) throw new NoteValidationException("A note needs some text.");
        if (value.Length > Note.MaxLength)
            throw new NoteValidationException($"A note can be at most {Note.MaxLength} characters; this one is {value.Length}.");
        return value;
    }

    private static NoteDto Map(Note n) => new(n.Id, n.Text, n.CreatedAtUtc, n.UpdatedAtUtc);
}
