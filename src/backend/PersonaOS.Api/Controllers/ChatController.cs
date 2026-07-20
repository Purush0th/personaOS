using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Application.Ai;

namespace PersonaOS.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController(IChatService chatService) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public record SendMessageRequest(string Message, int? ConversationId);

    /// <summary>
    /// Sends a message and streams the assistant's reply as Server-Sent Events:
    /// `start` {conversationId} → `delta` {text}* → `done` {usage} (or `error`).
    /// </summary>
    [HttpPost]
    public async Task Send([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await WriteEventAsync(new ChatStreamEvent("error", Error: "Message must not be empty."), ct);
            return;
        }

        await foreach (var evt in chatService.StreamChatAsync(request.ConversationId, request.Message.Trim(), ct))
        {
            await WriteEventAsync(evt, ct);
        }
    }

    /// <summary>Lists conversations, most recently active first.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations(CancellationToken ct) =>
        Ok(await chatService.ListConversationsAsync(ct));

    /// <summary>Returns one conversation with its full message history.</summary>
    [HttpGet("conversations/{id:int}")]
    public async Task<IActionResult> GetConversation(int id, CancellationToken ct)
    {
        var conversation = await chatService.GetConversationAsync(id, ct);
        return conversation is null ? NotFound() : Ok(conversation);
    }

    private async Task WriteEventAsync(ChatStreamEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(evt, JsonOpts);
        await Response.WriteAsync($"event: {evt.Type}\ndata: {payload}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
