namespace PersonaOS.Domain.Entities;

/// <summary>
/// Something jotted down to deal with later, as in Google Keep: plain text, one line or a few. A
/// note is not a permanent home; it is meant to be moved to the board's backlog (or deleted) soon,
/// so it has no status, tags or dates of its own.
/// </summary>
public class Note
{
    /// <summary>The longest a note can be: a thought, not a document.</summary>
    public const int MaxLength = 2000;

    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
