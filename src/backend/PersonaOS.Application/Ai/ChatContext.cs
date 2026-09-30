namespace PersonaOS.Application.Ai;

/// <summary>
/// What the current request is about, for the tools that need it: a memory saved in chat links
/// back to the conversation it came from. Set by <see cref="ChatService"/> for a chat turn and for
/// a confirmed card; empty anywhere else (the Memories screen, a brief).
/// </summary>
public interface IChatContext
{
    /// <summary>The conversation the running tool call belongs to, if any.</summary>
    int? ConversationId { get; set; }
}

public sealed class ChatContext : IChatContext
{
    public int? ConversationId { get; set; }
}
