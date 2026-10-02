using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.History;
using PersonaOS.Application.Ai.Models;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Ai;

/// <summary>Stored conversations and the cards waiting in them: list, read, delete, confirm, discard.</summary>
public partial class ChatService
{
    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(CancellationToken ct = default) =>
        await db.Conversations.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => new ConversationSummary(c.Id, c.PublicId, c.Title, c.CreatedAtUtc, c.UpdatedAtUtc, c.Mode))
            .ToListAsync(ct);

    public async Task<ConversationDetail?> GetConversationAsync(
        string idOrPublicId, CancellationToken ct = default)
    {
        // Public id is the normal case; a numeric id keeps links made before public ids working.
        var numericId = int.TryParse(idOrPublicId, out var parsed) ? parsed : (int?)null;

        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.PublicId == idOrPublicId || (numericId != null && c.Id == numericId))
            .Select(c => new
            {
                c.Id,
                c.PublicId,
                c.Title,
                c.CreatedAtUtc,
                c.Mode,
                Messages = c.Messages
                    .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
                    .Select(m => new
                    {
                        m.Id, m.Role, m.Content, m.InputTokens, m.OutputTokens, m.CreatedAtUtc,
                        m.ToolActionsJson, m.UnverifiedClaim, m.UnknownItems,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (conversation is null) return null;

        var actions = await db.PendingActions.AsNoTracking()
            .Where(a => a.ConversationId == conversation.Id)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        // Receipts are deserialized here rather than in the query: EF cannot translate it,
        // and a malformed row must not take the whole conversation down.
        var messages = conversation.Messages
            .Select(m => new ChatMessageDto(
                m.Id, m.Role, m.Content, m.InputTokens, m.OutputTokens, m.CreatedAtUtc,
                ReadReceipts(m.ToolActionsJson),
                actions.Where(a => a.ChatMessageId == m.Id).Select(ToDto).ToList() is { Count: > 0 } p
                    ? p
                    : null,
                m.UnverifiedClaim,
                m.UnknownItems is { Length: > 0 } unknown ? unknown.Split(',') : null))
            .ToList();

        return new ConversationDetail(
            conversation.Id, conversation.PublicId, conversation.Title, conversation.CreatedAtUtc, messages,
            ChatModes.Parse(conversation.Mode) ?? ChatModes.Chat);
    }

    private IReadOnlyList<ToolReceipt>? ReadReceipts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<List<ToolReceipt>>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read stored tool receipts; showing the message without them.");
            return null;
        }
    }

    public async Task<bool> DeleteConversationAsync(string idOrPublicId, CancellationToken ct = default)
    {
        var numericId = int.TryParse(idOrPublicId, out var parsed) ? parsed : (int?)null;

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.PublicId == idOrPublicId || (numericId != null && c.Id == numericId), ct);
        if (conversation is null) return false;

        // Messages and pending actions are cascade-deleted by their foreign keys, so the
        // thread leaves nothing orphaned behind it.
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Deleted conversation {PublicId}", conversation.PublicId);
        return true;
    }

    public async Task<PendingActionDto?> ConfirmActionAsync(string actionId, CancellationToken ct = default)
    {
        var action = await db.PendingActions.FirstOrDefaultAsync(a => a.PublicId == actionId, ct);
        if (action is null) return null;

        // Confirming twice must not run the tool twice.
        if (action.Status != PendingActionStatuses.Pending) return ToDto(action);

        chatContext.ConversationId = action.ConversationId;
        var result = await toolRegistry.ExecuteAsync(
            new AiToolCall(action.PublicId, action.ToolName, action.InputJson), ct);

        var receipt = ToolReceiptBuilder.Build(action.ToolName, result.Content, result.IsError);
        action.Status = PendingActionStatuses.Confirmed;
        action.ResultOk = !result.IsError;
        action.ResultSummary = receipt.Summary;
        action.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Confirmed action {Tool} ({Action}); ok={Ok}", action.ToolName, action.PublicId, action.ResultOk);

        return ToDto(action);
    }

    public async Task<PendingActionDto?> DiscardActionAsync(string actionId, CancellationToken ct = default)
    {
        var action = await db.PendingActions.FirstOrDefaultAsync(a => a.PublicId == actionId, ct);
        if (action is null) return null;
        if (action.Status != PendingActionStatuses.Pending) return ToDto(action);

        action.Status = PendingActionStatuses.Discarded;
        action.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        return ToDto(action);
    }

    private static PendingActionDto ToDto(PendingAction a) =>
        new(a.PublicId, a.ToolName, a.Summary, a.Status, a.ResultSummary, a.ResultOk);

    private static Conversation CreateConversation(string firstMessage)
    {
        var title = firstMessage.Trim();
        if (title.Length > 60) title = title[..57] + "…";
        return new Conversation { Title = title.Length == 0 ? "New conversation" : title };
    }
}
