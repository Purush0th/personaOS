import { PlannerItemDto } from '../core/planner.service';
import { describeWhen } from './event-peek';
import {
  customRule, daysBetween, defaultCustomRepeat, eventsByDay, formatMoment, formatRange, formatTime, hourLabel, layoutBars, layoutDay, leadText,
  monthGrid, movedTo, notifyAt, parseCustom, repeatOptions, snap, startOfWeek, stepAnchor, viewRange, viewTitle,
} from './calendar';

function item(id: number, start: string | null, end: string | null): PlannerItemDto {
  return {
    id, title: `Event ${id}`, notes: null, date: '2026-10-03', scheduledTime: start, endTime: end, sortOrder: 0,
    status: 'planned', goalId: null, goalTitle: null, goalKey: null, taskId: null, taskKey: null,
    notifyBeforeMinutes: null, recurrence: null, recurrenceText: null, occurrenceDate: '2026-10-03',
    isChanged: false, createdAtUtc: '', updatedAtUtc: '',
  };
}

describe('calendar', () => {
  // 3 October 2026 is a Saturday.
  const sat = '2026-10-03';

  describe('views', () => {
    it('weeks run Monday to Sunday', () => {
      expect(startOfWeek(sat)).toBe('2026-09-28');
      expect(startOfWeek('2026-09-28')).toBe('2026-09-28');
      expect(startOfWeek('2026-10-04')).toBe('2026-09-28');
      expect(viewRange('week', sat)).toEqual({ from: '2026-09-28', to: '2026-10-04' });
    });

    it('a month shows whole weeks from the week of the 1st to the week of the last day', () => {
      const grid = monthGrid(sat);
      expect(grid[0][0]).toBe('2026-09-28');
      expect(grid[grid.length - 1][6]).toBe('2026-11-01');
      expect(grid.length).toBe(5);
      expect(grid.every(week => week.length === 7)).toBeTrue();
    });

    it('a year loads the weeks spilling over its edges', () => {
      expect(viewRange('year', sat)).toEqual({ from: '2025-12-29', to: '2027-01-03' });
    });

    it('stepping moves by the size of the view', () => {
      expect(stepAnchor('day', sat, 1)).toBe('2026-10-04');
      expect(stepAnchor('4day', sat, -1)).toBe('2026-09-29');
      expect(stepAnchor('week', sat, 1)).toBe('2026-10-10');
      expect(stepAnchor('month', '2026-01-31', 1)).toBe('2026-02-01');
      expect(stepAnchor('year', sat, -1)).toBe('2025-01-01');
    });

    it('titles read like Google Calendar', () => {
      expect(viewTitle('day', sat)).toBe('3 October 2026');
      expect(viewTitle('week', sat)).toBe('Sept – Oct 2026');
      expect(viewTitle('week', '2026-10-14')).toBe('October 2026');
      expect(viewTitle('week', '2026-12-31')).toBe('Dec 2026 – Jan 2027');
      expect(viewTitle('month', sat)).toBe('October 2026');
      expect(viewTitle('year', sat)).toBe('2026');
    });
  });

  describe('times', () => {
    it('are written the way Google Calendar writes them', () => {
      expect(formatTime('08:30:00')).toBe('8:30am');
      expect(formatTime('12:00')).toBe('12pm');
      expect(formatTime('00:15')).toBe('12:15am');
      expect(formatRange('08:30:00', '09:30:00')).toBe('8:30 – 9:30am');
      expect(formatRange('12:00', '15:30')).toBe('12 – 3:30pm');
      expect(formatRange('11:00', '13:00')).toBe('11am – 1pm');
      expect(hourLabel(0)).toBe('12 AM');
      expect(hourLabel(13)).toBe('1 PM');
    });
  });

  describe('laying out a day', () => {
    it('gives events that do not overlap the whole width', () => {
      const placed = layoutDay([item(1, '08:30', '09:30'), item(2, '12:00', '15:30'), item(3, '16:00', '17:00')]);
      expect(placed.map(p => [p.column, p.columns])).toEqual([[0, 1], [0, 1], [0, 1]]);
    });

    it('puts overlapping events side by side and reuses a column once it is free', () => {
      const placed = layoutDay([item(1, '09:00', '11:00'), item(2, '09:30', '10:00'), item(3, '10:15', '10:45')]);
      const byId = new Map(placed.map(p => [p.item.id, p]));
      expect(byId.get(1)!.column).toBe(0);
      expect(byId.get(2)!.column).toBe(1);
      expect(byId.get(3)!.column).toBe(1);
      expect(placed.every(p => p.columns === 2)).toBeTrue();
    });

    it('leaves all-day events out and draws a tiny event tall enough to read', () => {
      const placed = layoutDay([item(1, null, null), item(2, '09:00', '09:05')]);
      expect(placed.length).toBe(1);
      expect(placed[0].endMinutes - placed[0].startMinutes).toBe(20);
    });
  });

  describe('repeat', () => {
    it('offers the choices Google Calendar offers for the day', () => {
      expect(repeatOptions(sat).map(o => o.label)).toEqual([
        'Does not repeat',
        'Daily',
        'Weekly on Saturday',
        'Monthly on the first Saturday',
        'Annually on October 3',
        'Every weekday (Monday to Friday)',
        'Custom…',
      ]);
    });

    it('offers "the last" for the last weekday of a month, and only that for a fifth one', () => {
      const labels = repeatOptions('2026-10-31').map(o => o.label);
      expect(labels).toContain('Monthly on the last Saturday');
      expect(labels.some(l => l.includes('fifth') || l.includes('fourth'))).toBeFalse();
      expect(repeatOptions('2026-10-24').map(o => o.label)).toContain('Monthly on the fourth Saturday');
    });

    it('builds a custom rule and reads it back', () => {
      const repeat = { ...defaultCustomRepeat(sat), interval: 2, days: ['SA', 'MO'], end: 'on' as const, until: '2026-12-31' };
      const rule = customRule(repeat, sat);
      expect(rule).toBe('FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,SA;UNTIL=20261231');

      const back = parseCustom(rule, sat);
      expect(back.interval).toBe(2);
      expect(back.days).toEqual(['MO', 'SA']);
      expect(back.end).toBe('on');
      expect(back.until).toBe('2026-12-31');
    });

    it('builds a monthly rule on the day or on the weekday', () => {
      const monthly = { ...defaultCustomRepeat(sat), unit: 'MONTHLY' as const, end: 'after' as const, count: 6 };
      expect(customRule(monthly, sat)).toBe('FREQ=MONTHLY;BYMONTHDAY=3;COUNT=6');
      expect(customRule({ ...monthly, monthlyBy: 'weekday' }, '2026-10-31')).toBe('FREQ=MONTHLY;BYDAY=-1SA;COUNT=6');
    });
  });

  describe('notification', () => {
    it('says the lead in the largest whole unit', () => {
      expect(leadText(25)).toBe('25 minutes');
      expect(leadText(60)).toBe('1 hour');
      expect(leadText(90)).toBe('90 minutes');
      expect(leadText(2880)).toBe('2 days');
      expect(leadText(10080)).toBe('1 week');
    });

    it('works out when the phone is notified, all day counting from 12 am', () => {
      expect(notifyAt(sat, '10:00:00', 30)).toEqual(new Date(2026, 9, 3, 9, 30));
      expect(notifyAt(sat, null, 60)).toEqual(new Date(2026, 9, 2, 23, 0));
      expect(notifyAt(sat, '10:00', 1440)).toEqual(new Date(2026, 9, 2, 10, 0));
    });

    it('writes the moment as the planner writes times, whatever the browser locale', () => {
      expect(formatMoment(new Date(2026, 9, 2, 9, 30))).toBe('Fri 2 Oct, 9:30am');
      expect(formatMoment(new Date(2026, 9, 2, 0, 0))).toBe('Fri 2 Oct, 12am');
    });
  });
  describe('several days', () => {
    // Friday 9 to Sunday 11 October.
    const trip = { ...item(1, null, null), date: '2026-10-09', endDate: '2026-10-11', occurrenceDate: '2026-10-09' };
    const week = daysBetween('2026-10-05', '2026-10-11');

    it('is under every day it covers, within the range', () => {
      const byDay = eventsByDay([trip], '2026-10-10', '2026-10-31');
      expect([...byDay.keys()]).toEqual(['2026-10-10', '2026-10-11']);
    });

    it('is a bar across its days, cut square where it runs past the days shown', () => {
      const [bar] = layoutBars(week, [trip]);
      expect(bar).toEqual(jasmine.objectContaining({ start: 4, span: 3, lane: 0, startsBefore: false, endsAfter: false }));

      const [cut] = layoutBars(daysBetween('2026-10-10', '2026-10-16'), [trip]);
      expect(cut).toEqual(jasmine.objectContaining({ start: 0, span: 2, startsBefore: true, endsAfter: false }));

      // Weekends hidden: only the Friday is shown, and it goes on past it.
      const [weekdays] = layoutBars(week.slice(0, 5), [trip]);
      expect(weekdays).toEqual(jasmine.objectContaining({ start: 4, span: 1, endsAfter: true }));
    });

    it('stacks overlapping bars in lanes, the longest first', () => {
      const saturday = { ...item(2, null, null), date: '2026-10-10' };
      const monday = { ...item(3, null, null), date: '2026-10-05' };
      const lanes = layoutBars(week, [saturday, monday, trip]).map(b => [b.item.id, b.lane]);
      expect(lanes).toEqual([[3, 0], [1, 0], [2, 1]]);
    });

    it('describes its days in the quick view', () => {
      expect(describeWhen(trip)).toBe('Friday, 9 October – Sunday, 11 October');
      const timed = { ...trip, scheduledTime: '18:00:00', endTime: '09:00:00', endDate: '2026-10-10' };
      expect(describeWhen(timed)).toBe('Friday, 9 October, 6pm – Saturday, 10 October, 9am');
    });
  });

  describe('dragging', () => {
    it('snaps to quarter hours', () => {
      expect(snap(7)).toBe(0);
      expect(snap(8)).toBe(15);
      expect(snap(-22)).toBe(-15);
    });

    it('moves a timed event keeping its length, cut at midnight', () => {
      const meeting = item(1, '09:00:00', '10:30:00');
      expect(movedTo(meeting, '2026-10-03', 14 * 60)).toEqual({ scheduledTime: '14:00', endTime: '15:30' });
      expect(movedTo(meeting, '2026-10-04', 23 * 60)).toEqual({ scheduledTime: '23:00', endTime: '23:59', date: '2026-10-04' });
    });
  });
});
