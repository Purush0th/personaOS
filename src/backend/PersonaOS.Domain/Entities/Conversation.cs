namespace PersonaOS.Domain.Entities;

/// <summary>A chat conversation between the user and their assistant.</summary>
public class Conversation
{
    public int Id { get; set; }

    /// <summary>
    /// Short opaque id used in URLs (<c>/chat/k3n9x2qp</c>), so neither the title nor the
    /// sequential row id ends up in the address bar. Assigned once, never reused.
    /// </summary>
    public string PublicId { get; set; } = Services.PublicIdGenerator.Next();

    public string Title { get; set; } = "New conversation";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
