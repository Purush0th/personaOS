using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Reminders;

public class ReminderService(
    IAppDbContext db,
    IInstanceConfigService configService,
    IReminderAlarmPublisher alarms) : IReminderService
{
    public async Task<IReadOnlyList<ReminderDto>> ListAsync(
        bool includeCompleted = false, CancellationToken ct = default)
    {
        var timeZone = await TimeZoneAsync(ct);
        var query = db.Reminders.AsNoTracking()
            .Include(r => r.Goal)
            .Include(r => r.PlannerItem)
            .AsQueryable();

        if (!includeCompleted)
        {
            query = query.Where(r => r.Status == ReminderStatuses.Pending);
        }

        var reminders = await query.OrderBy(r => r.DueAtUtc).ThenBy(r => r.Id).ToListAsync(ct);
        return reminders.Select(r => Map(r, timeZone)).ToList();
    }

    public async Task<ReminderDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var reminder = await db.Reminders.AsNoTracking()
            .Include(r => r.Goal)
            .Include(r => r.PlannerItem)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        return reminder is null ? null : Map(reminder, await TimeZoneAsync(ct));
    }

    public async Task<ReminderDto> CreateAsync(CreateReminderRequest request, CancellationToken ct = default)
    {
        var timeZone = await TimeZoneAsync(ct);
        var message = RequireMessage(request.Message);
        var dueAtUtc = ResolveDue(request.DueAtUtc, request.DueAtLocal, timeZone);

        if (dueAtUtc <= DateTime.UtcNow)
            throw new ReminderValidationException("The reminder time must be in the future.");

        await RequireLinksExistAsync(request.GoalId, request.PlannerItemId, ct);

        var reminder = new Reminder
        {
            Message = message,
            DueAtUtc = dueAtUtc,
            Status = ReminderStatuses.Pending,
            GoalId = request.GoalId,
            PlannerItemId = request.PlannerItemId,
        };

        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        await alarms.PublishScheduledAsync(reminder, ct);
        return await GetAsync(reminder.Id, ct) ?? Map(reminder, timeZone);
    }

    public async Task<ReminderDto?> UpdateAsync(int id, UpdateReminderRequest request, CancellationToken ct = default)
    {
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reminder is null) return null;

        if (reminder.Status != ReminderStatuses.Pending)
            throw new ReminderValidationException("Only pending reminders can be changed.");

        if (request.Message is not null) reminder.Message = RequireMessage(request.Message);

        if (request.DueAtUtc is not null || request.DueAtLocal is not null)
        {
            var timeZone = await TimeZoneAsync(ct);
            var dueAtUtc = ResolveDue(request.DueAtUtc, request.DueAtLocal, timeZone);
            if (dueAtUtc <= DateTime.UtcNow)
                throw new ReminderValidationException("The reminder time must be in the future.");
            reminder.DueAtUtc = dueAtUtc;
        }

        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await alarms.PublishScheduledAsync(reminder, ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReminderDto?> CancelAsync(int id, CancellationToken ct = default)
    {
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reminder is null) return null;

        if (reminder.Status == ReminderStatuses.Delivered)
            throw new ReminderValidationException("That reminder has already been delivered.");

        reminder.Status = ReminderStatuses.Cancelled;
        reminder.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await alarms.PublishRemovedAsync(reminder.Id, ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reminder is null) return false;

        db.Reminders.Remove(reminder);
        await db.SaveChangesAsync(ct);
        await alarms.PublishRemovedAsync(id, ct);
        return true;
    }

    public async Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var token = (request.Token ?? string.Empty).Trim();
        if (token.Length == 0)
            throw new ReminderValidationException("Device token is required.");

        var platform = (request.Platform ?? string.Empty).Trim().ToLowerInvariant();
        if (!DevicePlatforms.All.Contains(platform))
            throw new ReminderValidationException(
                $"Platform must be one of: {string.Join(", ", DevicePlatforms.All)}.");

        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is not null)
        {
            existing.Platform = platform;
            existing.DeviceName = request.DeviceName?.Trim() ?? existing.DeviceName;
            existing.LastSeenUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var added = new DeviceToken
        {
            Token = token,
            Platform = platform,
            DeviceName = request.DeviceName?.Trim(),
        };
        db.DeviceTokens.Add(added);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Check-then-insert races with itself. The app registers from two places that fire
            // together on first launch — the initial token fetch and FCM's token-refresh event —
            // so two requests both saw "not registered" and both inserted; the unique index
            // rejected the second and it surfaced as a 500. Losing that race means the token IS
            // registered, which is exactly what the caller asked for.
            db.DeviceTokens.Remove(added);
            if (!await db.DeviceTokens.AnyAsync(d => d.Token == token, ct)) throw;
        }
    }

    public async Task<bool> UnregisterDeviceAsync(string token, CancellationToken ct = default)
    {
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is null) return false;

        db.DeviceTokens.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> TimeZoneAsync(CancellationToken ct) =>
        (await configService.GetOrCreateAsync(ct)).TimeZone;

    private static DateTime ResolveDue(DateTime? dueAtUtc, DateTime? dueAtLocal, string timeZone)
    {
        if (dueAtUtc is not null && dueAtLocal is not null)
            throw new ReminderValidationException("Provide either dueAtUtc or dueAtLocal, not both.");

        if (dueAtUtc is DateTime utc)
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        if (dueAtLocal is DateTime local)
            return UserClock.ToUtc(local, timeZone);

        throw new ReminderValidationException("A due time is required (dueAtLocal or dueAtUtc).");
    }

    private async Task RequireLinksExistAsync(int? goalId, int? plannerItemId, CancellationToken ct)
    {
        if (goalId is int gid && !await db.Goals.AnyAsync(g => g.Id == gid, ct))
            throw new ReminderValidationException($"Goal {gid} does not exist.");

        if (plannerItemId is int pid && !await db.PlannerItems.AnyAsync(p => p.Id == pid, ct))
            throw new ReminderValidationException($"Planner item {pid} does not exist.");
    }

    private static string RequireMessage(string message)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ReminderValidationException("Reminder message is required.");
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static ReminderDto Map(Reminder r, string timeZone) => new(
        r.Id, r.Message, r.DueAtUtc, UserClock.ToLocal(r.DueAtUtc, timeZone), r.Status,
        r.DeliveredAtUtc, r.GoalId, r.Goal?.Title, r.PlannerItemId, r.PlannerItem?.Title,
        r.CreatedAtUtc);
}
