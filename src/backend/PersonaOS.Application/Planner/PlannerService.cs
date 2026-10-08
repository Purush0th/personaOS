using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Domain.Services;

namespace PersonaOS.Application.Planner;

public class PlannerService(IAppDbContext db) : IPlannerService
{
    /// <summary>A year view asks for a year plus the weeks that spill over its edges.</summary>
    private const int MaxRangeDays = 400;

    // --- Reads -------------------------------------------------------------------------

    public async Task<PlannerDay> GetDayAsync(DateOnly date, CancellationToken ct = default)
    {
        var views = await db.EventsAsync(date, date, withLinks: true, ct: ct);
        return new PlannerDay(date, views.Select(Map).ToList());
    }

    public async Task<IReadOnlyList<PlannerDay>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            throw new PlannerValidationException("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber > MaxRangeDays)
            throw new PlannerValidationException($"Date range must span at most {MaxRangeDays} days.");

        var views = await db.EventsAsync(from, to, withLinks: true, ct: ct);
        // An event over several days is listed once, so a client that lays out by each item's own
        // days does not see it twice; one that started before the range is listed on its first day.
        return views
            .GroupBy(v => v.Date < from ? from : v.Date)
            .OrderBy(g => g.Key)
            .Select(g => new PlannerDay(g.Key, g.Select(Map).ToList()))
            .ToList();
    }

    public async Task<PlannerItemDto?> GetAsync(int id, DateOnly? occurrence = null, CancellationToken ct = default)
    {
        var item = await Items().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;

        var original = occurrence ?? item.Date;
        var change = item.Recurrence is null
            ? null
            : await db.PlannerOccurrences.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ItemId == id && c.OriginalDate == original, ct);

        // An occurrence that was moved is looked up by the day it started on, so the window covers both.
        var first = change?.Date is DateOnly moved && moved < original ? moved : original;
        var last = change?.Date is DateOnly later && later > original ? later : original;
        var views = PlannerCalendar.Expand([item], change is null ? [] : [change], first, last);
        var view = views.FirstOrDefault(v => v.OriginalDate == original);
        return view is null ? null : Map(view);
    }

    // --- Create ------------------------------------------------------------------------

    public async Task<PlannerItemDto> CreateAsync(CreatePlannerItemRequest request, CancellationToken ct = default)
    {
        var task = await FindTaskAsync(request.TaskId, ct);
        // Picking a board task is enough: its title and goal carry over unless given explicitly.
        var title = RequireTitle(string.IsNullOrWhiteSpace(request.Title) && task is not null ? task.Title : request.Title);
        var goalId = request.GoalId ?? task?.GoalId;
        await RequireGoalExistsAsync(goalId, ct);
        var endDate = LastDay(request.Date, request.EndDate);
        CheckTimes(request.ScheduledTime, request.EndTime, multiDay: endDate is not null);
        CheckNotify(request.NotifyBeforeMinutes);

        var item = new PlannerItem
        {
            Title = title,
            Notes = request.Notes?.Trim(),
            Date = request.Date,
            EndDate = endDate,
            ScheduledTime = request.ScheduledTime,
            EndTime = request.EndTime,
            NotifyBeforeMinutes = request.NotifyBeforeMinutes,
            Recurrence = NormalizeRecurrence(request.Recurrence, request.Date),
            SortOrder = request.SortOrder ?? await NextSortOrderAsync(request.Date, ct),
            Status = PlannerItemStatuses.Planned,
            GoalId = goalId,
            TaskId = task?.Id,
        };

        db.PlannerItems.Add(item);
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, ct: ct) ?? throw new InvalidOperationException("The event was just saved but cannot be read.");
    }

    // --- Update ------------------------------------------------------------------------

    public async Task<PlannerItemDto?> UpdateAsync(
        int id, UpdatePlannerItemRequest request, PlannerTarget? target = null, CancellationToken ct = default)
    {
        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;
        target ??= new PlannerTarget();

        if (item.Recurrence is null || target.Scope == PlannerEditScope.All)
            return await UpdateSeriesAsync(item, request, target.Occurrence, ct);

        var occurrence = await RequireOccurrenceAsync(item, target.Occurrence, ct);
        if (target.Scope == PlannerEditScope.This)
            return await UpdateOccurrenceAsync(item, request, occurrence, ct);

        // "This and following" from the first occurrence is the whole series.
        return occurrence == item.Date
            ? await UpdateSeriesAsync(item, request, occurrence, ct)
            : await UpdateFollowingAsync(item, request, occurrence, ct);
    }

    /// <summary>A change to the whole series, or to an event that does not repeat.</summary>
    private async Task<PlannerItemDto?> UpdateSeriesAsync(
        PlannerItem item, UpdatePlannerItemRequest request, DateOnly? occurrence, CancellationToken ct)
    {
        var wasRecurring = item.Recurrence is not null;
        var oldDate = item.Date;
        var oldRule = item.Recurrence;
        var span = PlannerCalendar.SpanDays(item);
        // The occurrence the person was looking at, on the day they saw it (it may have been moved
        // on its own): moving it moves the whole series by as much.
        var anchor = item.Date;
        if (wasRecurring && occurrence is DateOnly original)
        {
            anchor = await db.PlannerOccurrences.AsNoTracking()
                .Where(c => c.ItemId == item.Id && c.OriginalDate == original)
                .Select(c => c.Date)
                .FirstOrDefaultAsync(ct) ?? original;
        }

        if (wasRecurring && request.ClearRecurrence)
        {
            item.Date = request.Date ?? anchor;
            item.Recurrence = null;
            item.Status = PlannerItemStatuses.Planned;
            await DropChangesAsync(item.Id, from: null, ct);
        }
        else if (request.Date is DateOnly requested)
        {
            item.Date = wasRecurring ? item.Date.AddDays(requested.DayNumber - anchor.DayNumber) : requested;
        }

        // A new last day is counted from the day the person was looking at, like a new first day.
        if (request.EndDate is DateOnly end) span = SpanFrom(request.Date ?? anchor, end);
        item.EndDate = span == 0 ? null : item.Date.AddDays(span);

        await ApplyFieldsAsync(item, request, ct);

        if (item.Recurrence is not null || request.Recurrence is not null)
        {
            if (request.Recurrence is not null && !(wasRecurring && request.ClearRecurrence))
            {
                item.Recurrence = NormalizeRecurrence(request.Recurrence, item.Date);
            }
            else if (item.Recurrence is not null && item.Date != oldDate
                     && RecurrenceRule.TryParse(item.Recurrence, out var rule, out _))
            {
                item.Recurrence = rule.MovedTo(oldDate, item.Date).ToString();
            }

            // The changes to single occurrences are keyed by the days the old rule produced.
            if (wasRecurring && (item.Date != oldDate || item.Recurrence != oldRule))
                await DropChangesAsync(item.Id, from: null, ct);
        }

        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, item.Recurrence is null ? null : item.Date, ct);
    }

    /// <summary>A change to one occurrence of a repeating event.</summary>
    private async Task<PlannerItemDto?> UpdateOccurrenceAsync(
        PlannerItem item, UpdatePlannerItemRequest request, DateOnly occurrence, CancellationToken ct)
    {
        RequireSeriesLevelUnchanged(item, request);

        var change = await db.PlannerOccurrences.FirstOrDefaultAsync(
            c => c.ItemId == item.Id && c.OriginalDate == occurrence, ct);
        if (change is null)
        {
            change = new PlannerOccurrence { ItemId = item.Id, OriginalDate = occurrence };
            db.PlannerOccurrences.Add(change);
        }

        if (request.Title is not null) change.Title = RequireTitle(request.Title);
        if (request.Notes is not null) change.Notes = request.Notes.Trim();

        // The days: moving keeps the number of days it lasts; a new last day sets it.
        var span = PlannerCalendar.SpanDays(item);
        var wasOn = change.Date ?? occurrence;
        var lastDay = change.EndDate is DateOnly own && own >= wasOn ? own : wasOn.AddDays(span);
        if (request.Date is DateOnly date) change.Date = date == occurrence ? null : date;
        var isOn = change.Date ?? occurrence;
        lastDay = request.EndDate is DateOnly newLast
            ? isOn.AddDays(SpanFrom(isOn, newLast))
            : lastDay.AddDays(isOn.DayNumber - wasOn.DayNumber);
        change.EndDate = lastDay == isOn.AddDays(span) ? null : lastDay;
        var multiDay = lastDay > isOn;

        var (currentStart, currentEnd) = change.AllDay switch
        {
            true => ((TimeOnly?)null, (TimeOnly?)null),
            false => (change.ScheduledTime, change.EndTime),
            _ => (item.ScheduledTime, item.EndTime),
        };
        if (request.ScheduledTime is not null || request.ClearScheduledTime || request.EndTime is not null || request.ClearEndTime)
        {
            var (start, end) = ResolveTimes(currentStart, currentEnd, request, multiDay);
            change.AllDay = start is null;
            change.ScheduledTime = start;
            change.EndTime = end;
        }
        else
        {
            // Made longer or shorter, its times must still make sense.
            CheckTimes(currentStart, currentEnd, multiDay);
        }

        change.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, occurrence, ct);
    }

    /// <summary>
    /// "This and following": the series ends the day before the occurrence and a new one, carrying
    /// the change, starts on it. Changes already made to later occurrences follow the new series
    /// unless the change moves the days around, when they no longer line up and are dropped.
    /// </summary>
    private async Task<PlannerItemDto?> UpdateFollowingAsync(
        PlannerItem item, UpdatePlannerItemRequest request, DateOnly occurrence, CancellationToken ct)
    {
        var rule = RuleOf(item);
        var newStart = request.Date ?? occurrence;
        var span = request.EndDate is DateOnly end ? SpanFrom(newStart, end) : PlannerCalendar.SpanDays(item);

        string? newRecurrence;
        if (request.ClearRecurrence)
            newRecurrence = null;
        else if (request.Recurrence is not null)
            newRecurrence = NormalizeRecurrence(request.Recurrence, newStart);
        else
            newRecurrence = rule.StartingFrom(item.Date, occurrence).MovedTo(occurrence, newStart).ToString();

        var next = new PlannerItem
        {
            Title = item.Title,
            Notes = item.Notes,
            Date = newStart,
            EndDate = span == 0 ? null : newStart.AddDays(span),
            ScheduledTime = item.ScheduledTime,
            EndTime = item.EndTime,
            NotifyBeforeMinutes = item.NotifyBeforeMinutes,
            Recurrence = newRecurrence,
            SortOrder = item.SortOrder,
            Status = PlannerItemStatuses.Planned,
            GoalId = item.GoalId,
            TaskId = item.TaskId,
        };
        await ApplyFieldsAsync(next, request with { Date = null, Recurrence = null, ClearRecurrence = false }, ct);

        var keepLaterChanges = newStart == occurrence && !request.ClearRecurrence && request.Recurrence is null;
        // The occurrence the split starts from becomes the new series' first. What the edit sets
        // replaces its own changes; the rest, like a done mark, stays with it.
        var thisOne = await db.PlannerOccurrences.FirstOrDefaultAsync(c => c.ItemId == item.Id && c.OriginalDate == occurrence, ct);
        if (thisOne is not null)
        {
            if (request.Title is not null) thisOne.Title = null;
            if (request.Notes is not null) thisOne.Notes = null;
            if (request.ScheduledTime is not null || request.ClearScheduledTime || request.EndTime is not null || request.ClearEndTime)
            {
                thisOne.AllDay = null;
                thisOne.ScheduledTime = thisOne.EndTime = null;
            }

            // Its own last day was counted from where it was; the new series sets where it is.
            if (request.EndDate is not null || request.Date is not null || thisOne.Date is not null) thisOne.EndDate = null;
            thisOne.Date = null;
            if (IsEmpty(thisOne) || newRecurrence is null)
            {
                // A series that no longer repeats keeps a done mark on the event itself.
                if (newRecurrence is null && thisOne.Status is { } status) next.Status = status;
                db.PlannerOccurrences.Remove(thisOne);
            }
            else
            {
                thisOne.OriginalDate = newStart;
                thisOne.Item = next;
                thisOne.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var laterChanges = await db.PlannerOccurrences.Where(c => c.ItemId == item.Id && c.OriginalDate > occurrence).ToListAsync(ct);
        var laterLogs = await db.PlannerAlertLogs.Where(l => l.ItemId == item.Id && l.OccurrenceDate >= occurrence).ToListAsync(ct);
        if (keepLaterChanges && newRecurrence is not null)
        {
            foreach (var change in laterChanges) change.Item = next;
            foreach (var log in laterLogs) log.Item = next;
        }
        else
        {
            db.PlannerOccurrences.RemoveRange(laterChanges);
            db.PlannerAlertLogs.RemoveRange(laterLogs);
        }

        item.Recurrence = rule.EndingBefore(item.Date, occurrence).ToString();
        item.UpdatedAtUtc = DateTime.UtcNow;
        db.PlannerItems.Add(next);
        await db.SaveChangesAsync(ct);
        return await GetAsync(next.Id, next.Recurrence is null ? null : next.Date, ct);
    }

    public async Task<PlannerItemDto?> UpdateStatusAsync(
        int id, string status, DateOnly? occurrence = null, CancellationToken ct = default)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!PlannerItemStatuses.All.Contains(normalized))
            throw new PlannerValidationException(
                $"Status must be one of: {string.Join(", ", PlannerItemStatuses.All)}.");

        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;

        if (item.Recurrence is null)
        {
            item.Status = normalized;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return await GetAsync(id, ct: ct);
        }

        var day = await RequireOccurrenceAsync(item, occurrence, ct);
        var change = await db.PlannerOccurrences.FirstOrDefaultAsync(c => c.ItemId == id && c.OriginalDate == day, ct);
        if (change is null && normalized == PlannerItemStatuses.Planned) return await GetAsync(id, day, ct);

        if (change is null)
        {
            change = new PlannerOccurrence { ItemId = id, OriginalDate = day };
            db.PlannerOccurrences.Add(change);
        }

        change.Status = normalized == PlannerItemStatuses.Planned ? null : normalized;
        change.UpdatedAtUtc = DateTime.UtcNow;
        if (IsEmpty(change)) db.PlannerOccurrences.Remove(change);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, day, ct);
    }

    public async Task<PlannerItemDto?> MoveAsync(
        int id, DateOnly date, PlannerTarget? target = null, CancellationToken ct = default)
    {
        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return null;

        if (item.Recurrence is not null)
        {
            // Moving a day of a repeating event is moving that one, unless said otherwise.
            return await UpdateAsync(id, new UpdatePlannerItemRequest(Date: date), target ?? new PlannerTarget(PlannerEditScope.This), ct);
        }

        var span = PlannerCalendar.SpanDays(item);
        item.Date = date;
        item.EndDate = span == 0 ? null : date.AddDays(span);
        item.SortOrder = await NextSortOrderAsync(date, ct);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct: ct);
    }

    // --- Delete ------------------------------------------------------------------------

    public async Task<bool> DeleteAsync(int id, PlannerTarget? target = null, CancellationToken ct = default)
    {
        var item = await db.PlannerItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return false;
        target ??= new PlannerTarget();

        if (item.Recurrence is null || target.Scope == PlannerEditScope.All)
        {
            await DropChangesAsync(id, from: null, ct);
            db.PlannerItems.Remove(item);
            await db.SaveChangesAsync(ct);
            return true;
        }

        var occurrence = await RequireOccurrenceAsync(item, target.Occurrence, ct);
        if (target.Scope == PlannerEditScope.This)
        {
            var change = await db.PlannerOccurrences.FirstOrDefaultAsync(
                c => c.ItemId == id && c.OriginalDate == occurrence, ct);
            if (change is null)
            {
                change = new PlannerOccurrence { ItemId = id, OriginalDate = occurrence };
                db.PlannerOccurrences.Add(change);
            }

            // Whatever was changed on it before no longer matters.
            change.IsCancelled = true;
            change.Title = change.Notes = change.Status = null;
            change.Date = change.EndDate = null;
            change.ScheduledTime = change.EndTime = null;
            change.AllDay = null;
            change.UpdatedAtUtc = DateTime.UtcNow;
        }
        else if (occurrence <= item.Date)
        {
            await DropChangesAsync(id, from: null, ct);
            db.PlannerItems.Remove(item);
        }
        else
        {
            item.Recurrence = RuleOf(item).EndingBefore(item.Date, occurrence).ToString();
            item.UpdatedAtUtc = DateTime.UtcNow;
            await DropChangesAsync(id, from: occurrence, ct);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // --- Helpers -----------------------------------------------------------------------

    private IQueryable<PlannerItem> Items() =>
        db.PlannerItems.AsNoTracking()
            .Include(i => i.Goal)
            .Include(i => i.Task).ThenInclude(t => t!.Goal);

    /// <summary>Removes the single-occurrence changes and sent-notification records of a series, from a day on or all of them.</summary>
    private async Task DropChangesAsync(int itemId, DateOnly? from, CancellationToken ct)
    {
        var changes = await db.PlannerOccurrences
            .Where(c => c.ItemId == itemId && (from == null || c.OriginalDate >= from))
            .ToListAsync(ct);
        db.PlannerOccurrences.RemoveRange(changes);

        var logs = await db.PlannerAlertLogs
            .Where(l => l.ItemId == itemId && (from == null || l.OccurrenceDate >= from))
            .ToListAsync(ct);
        db.PlannerAlertLogs.RemoveRange(logs);
    }

    /// <summary>The title, notes, times, alert and links of an update, on a tracked item whose days are already set.</summary>
    private async Task ApplyFieldsAsync(PlannerItem item, UpdatePlannerItemRequest request, CancellationToken ct)
    {
        if (request.Title is not null) item.Title = RequireTitle(request.Title);
        if (request.Notes is not null) item.Notes = request.Notes.Trim();
        if (request.SortOrder is int sortOrder) item.SortOrder = sortOrder;

        (item.ScheduledTime, item.EndTime) = ResolveTimes(item.ScheduledTime, item.EndTime, request, multiDay: item.EndDate is not null);

        if (request.ClearGoal)
        {
            item.GoalId = null;
        }
        else if (request.GoalId is int goalId)
        {
            await RequireGoalExistsAsync(goalId, ct);
            item.GoalId = goalId;
        }

        if (request.ClearTask)
        {
            item.TaskId = null;
        }
        else if (request.TaskId is int taskId)
        {
            item.TaskId = (await FindTaskAsync(taskId, ct))!.Id;
        }

        if (request.ClearNotify)
        {
            item.NotifyBeforeMinutes = null;
        }
        else if (request.NotifyBeforeMinutes is int minutes)
        {
            CheckNotify(minutes);
            item.NotifyBeforeMinutes = minutes;
        }
    }

    /// <summary>
    /// The start and end after a change. Moving the start keeps the length (over several days the
    /// end is on another day and stays put); going all-day drops the end; an end needs a start and,
    /// on the same day, must come after it.
    /// </summary>
    private static (TimeOnly? Start, TimeOnly? End) ResolveTimes(
        TimeOnly? start, TimeOnly? end, UpdatePlannerItemRequest request, bool multiDay)
    {
        var newStart = start;
        var newEnd = end;

        if (request.ClearScheduledTime)
        {
            newStart = null;
            newEnd = null;
        }
        else if (request.ScheduledTime is TimeOnly moved)
        {
            newStart = moved;
            if (!multiDay && request.EndTime is null && !request.ClearEndTime && start is TimeOnly was && end is TimeOnly wasEnd && wasEnd > was)
                newEnd = PlannerCalendar.WithinDay(moved, (int)(wasEnd - was).TotalMinutes);
            else if (start is null)
                newEnd = null;
        }

        if (request.ClearEndTime) newEnd = null;
        else if (request.EndTime is TimeOnly given) newEnd = given;

        CheckTimes(newStart, newEnd, multiDay);
        return (newStart, newEnd);
    }

    private static void CheckTimes(TimeOnly? start, TimeOnly? end, bool multiDay)
    {
        if (end is not null && start is null)
            throw new PlannerValidationException("An end time needs a start time; leave both out for an all-day event.");
        if (multiDay)
        {
            if (start is not null && end is null)
                throw new PlannerValidationException("An event over several days with a start time needs an end time too.");
            return;
        }
        if (end is not null && end <= start)
            throw new PlannerValidationException("The end time must be after the start time.");
    }

    /// <summary>The last day of a new event: null when it ends on the day it starts.</summary>
    private static DateOnly? LastDay(DateOnly date, DateOnly? endDate) =>
        SpanFrom(date, endDate ?? date) == 0 ? null : endDate;

    /// <summary>How many days after <paramref name="first"/> the event ends; an end before the start is refused.</summary>
    private static int SpanFrom(DateOnly first, DateOnly last) =>
        last >= first
            ? last.DayNumber - first.DayNumber
            : throw new PlannerValidationException("The end day must be on or after the start day.");

    private static void CheckNotify(int? minutes)
    {
        if (minutes is not int value) return;
        if (value < 0)
            throw new PlannerValidationException("An alert cannot be before zero minutes.");
        if (value > PlannerEvents.MaxNotifyBeforeMinutes)
            throw new PlannerValidationException(string.Create(
                CultureInfo.InvariantCulture,
                $"An alert can be at most 4 weeks ({PlannerEvents.MaxNotifyBeforeMinutes:N0} minutes) before the event; {value:N0} minutes is too far ahead."));
    }

    /// <summary>The rule in its canonical text, or a validation error saying what is wrong with it.</summary>
    private static string? NormalizeRecurrence(string? text, DateOnly start)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!RecurrenceRule.TryParse(text, out var rule, out var error))
            throw new PlannerValidationException(error);
        if (rule.Until is DateOnly until && until < start)
            throw new PlannerValidationException("The repeat ends before the event starts.");
        return rule.ToString();
    }

    /// <summary>A repeat, an alert or a link applies to every event in the series; it cannot differ for one.</summary>
    private static void RequireSeriesLevelUnchanged(PlannerItem item, UpdatePlannerItemRequest request)
    {
        var differs = request.ClearGoal
                      || (request.GoalId is not null && request.GoalId != item.GoalId)
                      || request.ClearTask
                      || (request.TaskId is not null && request.TaskId != item.TaskId)
                      || request.ClearNotify
                      || (request.NotifyBeforeMinutes is not null && request.NotifyBeforeMinutes != item.NotifyBeforeMinutes)
                      || request.ClearRecurrence
                      || (request.Recurrence is not null && NormalizeRecurrence(request.Recurrence, item.Date) != item.Recurrence);
        if (differs)
            throw new PlannerValidationException(
                "The repeat, alert and links belong to the whole series: choose 'All events' or 'This and following'.");
    }

    /// <summary>The parsed repeat rule of a repeating item; one that cannot be read is reported rather than guessed at.</summary>
    private static RecurrenceRule RuleOf(PlannerItem item) =>
        RecurrenceRule.TryParse(item.Recurrence, out var rule, out var error)
            ? rule
            : throw new PlannerValidationException($"The repeat rule of “{item.Title}” cannot be read: {error}");

    /// <summary>The day, checked to be one the series has an occurrence on (or had one changed).</summary>
    private async Task<DateOnly> RequireOccurrenceAsync(PlannerItem item, DateOnly? occurrence, CancellationToken ct)
    {
        var day = occurrence ?? throw new PlannerValidationException(
            "This event repeats: say which occurrence you mean (its date).");

        if (RecurrenceRule.TryParse(item.Recurrence, out var rule, out _) && rule.Dates(item.Date, day, day).Any())
            return day;
        if (await db.PlannerOccurrences.AnyAsync(c => c.ItemId == item.Id && c.OriginalDate == day, ct))
            return day;

        throw new PlannerValidationException(string.Create(
            CultureInfo.InvariantCulture, $"“{item.Title}” does not repeat on {day:yyyy-MM-dd}."));
    }

    private static bool IsEmpty(PlannerOccurrence c) =>
        !c.IsCancelled && c.Title is null && c.Notes is null && c.Date is null && c.EndDate is null
        && c.ScheduledTime is null && c.EndTime is null && c.AllDay is null && c.Status is null;

    private async Task<int> NextSortOrderAsync(DateOnly date, CancellationToken ct)
    {
        var max = await db.PlannerItems
            .Where(i => i.Date == date)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    private async Task<BoardTask?> FindTaskAsync(int? taskId, CancellationToken ct)
    {
        if (taskId is not int id) return null;
        return await db.BoardTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new PlannerValidationException($"Task {id} does not exist.");
    }

    private async Task RequireGoalExistsAsync(int? goalId, CancellationToken ct)
    {
        if (goalId is not int id) return;
        if (!await db.Goals.AnyAsync(g => g.Id == id, ct))
            throw new PlannerValidationException($"Goal {id} does not exist.");
    }

    private static string RequireTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new PlannerValidationException("Title is required.");
        return trimmed.Length > 300 ? trimmed[..300] : trimmed;
    }

    private static PlannerItemDto Map(PlannerOccurrenceView view)
    {
        var i = view.Item;
        // A linked task's goal wins: the chip should match the card on the board.
        var goal = i.Task?.Goal ?? i.Goal;
        var recurrenceText = RecurrenceRule.TryParse(i.Recurrence, out var rule, out _) ? rule.Describe(i.Date) : null;
        return new(
            i.Id, view.Title, view.Notes, view.Date, view.Start, i.SortOrder, view.Status,
            goal?.Id, goal?.Title, i.CreatedAtUtc, i.UpdatedAtUtc,
            goal is null ? null : ItemKeys.Goal(goal.Number),
            i.TaskId,
            i.Task is null ? null : ItemKeys.Task(i.Task.Number),
            EndTime: view.EffectiveEnd,
            NotifyBeforeMinutes: i.NotifyBeforeMinutes,
            Recurrence: i.Recurrence,
            RecurrenceText: recurrenceText,
            OccurrenceDate: view.OriginalDate,
            IsChanged: view.IsChanged,
            EndDate: view.EndDate);
    }
}
