namespace PersonaOS.Domain.Entities;

/// <summary>A chat conversation between the user and their assistant.</summary>
public class Conversation
{
    public int Id { get; set; }

    public string Title { get; set; } = "New conversation";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
