using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.Reminders;

/// <summary>A reminder with both UTC and user-local due times resolved.</summary>
public record ReminderDto(
    int Id,
    string Message,
    DateTime DueAtUtc,
    /// <summary>Due time rendered in the install's configured zone, for display.</summary>
    DateTime DueAtLocal,
    string Status,
    DateTime? DeliveredAtUtc,
    int? GoalId,
    string? GoalTitle,
    int? PlannerItemId,
    string? PlannerItemTitle,
    DateTime CreatedAtUtc);

/// <summary>
/// Creating a reminder. Supply exactly one of <paramref name="DueAtUtc"/> or
/// <paramref name="DueAtLocal"/> — the latter is interpreted in the install's time zone.
/// </summary>
public record CreateReminderRequest(
    string Message,
    DateTime? DueAtUtc = null,
    DateTime? DueAtLocal = null,
    int? GoalId = null,
    int? PlannerItemId = null);

public record UpdateReminderRequest(
    string? Message = null,
    DateTime? DueAtUtc = null,
    DateTime? DueAtLocal = null);

public record RegisterDeviceRequest(string Token, string Platform, string? DeviceName = null);

/// <summary>Invalid input to a reminder operation; message is user/model-presentable.</summary>
public class ReminderValidationException(string message)
    : DomainValidationException(message, "reminder_validation_failed");

public interface IReminderService
{
    /// <summary>Reminders, newest due first. Pending-only unless includeCompleted.</summary>
    Task<IReadOnlyList<ReminderDto>> ListAsync(bool includeCompleted = false, CancellationToken ct = default);

    Task<ReminderDto?> GetAsync(int id, CancellationToken ct = default);

    Task<ReminderDto> CreateAsync(CreateReminderRequest request, CancellationToken ct = default);

    Task<ReminderDto?> UpdateAsync(int id, UpdateReminderRequest request, CancellationToken ct = default);

    /// <summary>Cancels a pending reminder so it will not fire.</summary>
    Task<ReminderDto?> CancelAsync(int id, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Registers or refreshes a device's push token (idempotent per token).</summary>
    Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default);

    /// <summary>Removes a device token (logout / uninstall).</summary>
    Task<bool> UnregisterDeviceAsync(string token, CancellationToken ct = default);
}
