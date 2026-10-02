using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonaOS.Application.Common;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Board;

/// <summary>The board: creating, editing, starting, completing and deleting sprints.</summary>
public partial class BoardService
{
    // ---------------------------------------------------------------- sprints

    public async Task<SprintDto> CreateSprintAsync(CreateSprintRequest request, CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var zone = config.TimeZone;

        // Default to the next free week in the Sunday-to-Sunday rhythm, so creating a sprint is
        // one click when the user has no opinion about the dates.
        var lastEnd = await db.Sprints.OrderByDescending(s => s.EndsAtUtc)
            .Select(s => (DateTime?)s.EndsAtUtc).FirstOrDefaultAsync(ct);

        // The first sprint starts now and runs to the coming Sunday; later ones take the next free
        // week in the Sunday 20:00 → Sunday 18:00 rhythm. All of it is editable afterwards.
        var startLocal = RequestedStart(request.StartsOn, request.StartsAtLocal)
            ?? (lastEnd is DateTime end ? SprintCalendar.StartAfterClose(UserClock.ToLocal(end, zone)) : LocalNow(config));
        var endLocal = request.EndsAtLocal ?? SprintCalendar.CloseAfterStart(startLocal);
        if (endLocal <= startLocal)
            throw new BoardValidationException("A sprint must end after it starts.");

        var sprint = new Sprint
        {
            // Lowest free, like task keys: deleting a planned sprint frees its number again.
            Number = KeyNumberAllocator.LowestFree(await db.Sprints.Select(s => s.Number).ToListAsync(ct)),
            Name = NormalizeText(request.Name),
            Status = SprintStatuses.Planned,
            StartsAtUtc = UserClock.ToUtc(startLocal, zone),
            EndsAtUtc = UserClock.ToUtc(endLocal, zone),
        };
        db.Sprints.Add(sprint);
        await db.SaveChangesAsync(ct);
        return ToSprintDto(sprint, []);
    }

    public async Task<SprintDto?> UpdateSprintAsync(int id, UpdateSprintRequest request, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;
        if (sprint.Status == SprintStatuses.Closed)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} is closed; its dates are history now.");

        var config = await configService.GetOrCreateAsync(ct);
        if (request.ClearName) sprint.Name = null;
        else if (request.Name is not null) sprint.Name = NormalizeText(request.Name);

        if (RequestedStart(request.StartsOn, request.StartsAtLocal) is DateTime start)
        {
            sprint.StartsAtUtc = UserClock.ToUtc(start, config.TimeZone);
            // The end follows the start, as on creation, unless the caller names one.
            sprint.EndsAtUtc = UserClock.ToUtc(request.EndsAtLocal ?? SprintCalendar.CloseAfterStart(start), config.TimeZone);
        }
        else if (request.EndsAtLocal is DateTime end)
        {
            sprint.EndsAtUtc = UserClock.ToUtc(end, config.TimeZone);
        }
        if (sprint.EndsAtUtc <= sprint.StartsAtUtc)
            throw new BoardValidationException("A sprint must end after it starts.");

        await db.SaveChangesAsync(ct);
        return ToSprintDto(sprint, await db.BoardTasks.AsNoTracking().Where(t => t.SprintId == id).ToListAsync(ct));
    }

    public async Task<SprintDto?> StartSprintAsync(int id, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;
        if (sprint.Status != SprintStatuses.Planned)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} has already been started.");

        var running = await db.Sprints.FirstOrDefaultAsync(s => s.Status == SprintStatuses.Active, ct);
        if (running is not null)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Sprint(running.Number)} is still running. Complete it before starting another.");
        }

        // A sprint for a later week waits for its week. On its first day it can start at any hour,
        // so the Sunday planning window (18:00-20:00) can start the next sprint early.
        var config = await configService.GetOrCreateAsync(ct);
        var startsOn = DateOnly.FromDateTime(UserClock.ToLocal(sprint.StartsAtUtc, config.TimeZone));
        if (DateOnly.FromDateTime(LocalNow(config)) < startsOn)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Sprint(sprint.Number)} starts on {startsOn.ToString("ddd, MMM d", CultureInfo.InvariantCulture)}. " +
                "Move its start to today to begin it now.");
        }

        var tasks = await db.BoardTasks.Where(t => t.SprintId == id).ToListAsync(ct);
        sprint.Status = SprintStatuses.Active;
        sprint.StartedAtUtc = UtcNow();
        sprint.CommittedPoints = tasks.Sum(t => t.Points ?? 0);
        foreach (var task in tasks) task.AddedMidSprint = false;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Started sprint {Number} with {Points} points.", sprint.Number, sprint.CommittedPoints);
        return ToSprintDto(sprint, tasks);
    }

    public async Task<SprintDto?> CompleteSprintAsync(
        int id, CompleteSprintRequest request, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return null;
        if (sprint.Status != SprintStatuses.Active)
            throw new BoardValidationException($"{ItemKeys.Sprint(sprint.Number)} is not running.");

        // Unfinished work goes to the named sprint, or the next planned one, or the backlog.
        Sprint? target = null;
        if (!request.ToBacklog)
        {
            target = await FindSprintAsync(request.MoveUnfinishedToSprintKey, ct)
                     ?? await db.Sprints.Where(s => s.Status == SprintStatuses.Planned)
                         .OrderBy(s => s.Number).FirstOrDefaultAsync(ct);

            if (target is not null && target.Id == sprint.Id)
                throw new BoardValidationException("Unfinished work cannot move into the sprint being completed.");
            if (target is not null && target.Status == SprintStatuses.Closed)
                throw new BoardValidationException($"{ItemKeys.Sprint(target.Number)} is closed.");
        }

        var tasks = await db.BoardTasks.Where(t => t.SprintId == id).ToListAsync(ct);
        var done = tasks.Where(t => t.Status == BoardTaskStatuses.Done).ToList();
        var unfinished = tasks.Where(t => t.Status != BoardTaskStatuses.Done)
            .OrderBy(t => t.Status == BoardTaskStatuses.InProgress ? 0 : 1).ThenBy(t => t.SortOrder).ToList();

        sprint.CommittedPoints ??= tasks.Sum(t => t.Points ?? 0);
        sprint.AddedPoints = tasks.Where(t => t.AddedMidSprint).Sum(t => t.Points ?? 0);
        sprint.CompletedPoints = done.Sum(t => t.Points ?? 0);
        sprint.CarriedOverPoints = unfinished.Sum(t => t.Points ?? 0);
        sprint.Status = SprintStatuses.Closed;
        sprint.ClosedAtUtc = UtcNow();

        // Unfinished work moves on, ahead of anything already waiting there.
        var waiting = target is null
            ? []
            : await db.BoardTasks.Where(t => t.SprintId == target.Id).OrderBy(t => t.SortOrder).ToListAsync(ct);
        var order = 0;
        foreach (var task in unfinished)
        {
            task.SprintId = target?.Id;
            task.CarryOverCount++;
            task.AddedMidSprint = false;
            task.SortOrder = order++;
            task.UpdatedAtUtc = UtcNow();
        }
        foreach (var task in waiting) task.SortOrder = order++;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Completed sprint {Number}: {Completed} of {Committed} points, {Carried} carried to {Target}.",
            sprint.Number, sprint.CompletedPoints, sprint.CommittedPoints, sprint.CarriedOverPoints,
            target is null ? "the backlog" : ItemKeys.Sprint(target.Number));
        return ToSprintDto(sprint, tasks);
    }

    public async Task<bool> DeleteSprintAsync(int id, CancellationToken ct = default)
    {
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sprint is null) return false;

        // A started sprint is a record of a week's work, so it is completed rather than deleted —
        // unless it holds nothing. An empty sprint records nothing, and refusing to remove it left
        // sprint history growing forever with no way to prune a mistake or a test run.
        var holdsWork = await db.BoardTasks.AnyAsync(t => t.SprintId == id, ct);
        if (sprint.Status != SprintStatuses.Planned && holdsWork)
        {
            throw new BoardValidationException(
                $"{ItemKeys.Sprint(sprint.Number)} has already started and holds tasks; complete it "
                + "instead of deleting it.");
        }

        foreach (var task in await db.BoardTasks.Where(t => t.SprintId == id).ToListAsync(ct))
        {
            task.SprintId = null;
            task.Status = BoardTaskStatuses.Todo;
            task.AddedMidSprint = false;
            task.UpdatedAtUtc = UtcNow();
        }

        db.Sprints.Remove(sprint);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
