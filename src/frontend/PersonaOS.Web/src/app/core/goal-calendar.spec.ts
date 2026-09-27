import { monthsOfQuarter, resolveGoalDates, slotLabel } from './goal-calendar';

/** The same rules as the server's GoalCalendar (GoalCalendarTests), on 27 September 2026. */
describe('goal calendar', () => {
  const today = '2026-09-27';

  it('runs a year from today, or a later start, to 31 December, with at least 90 days', () => {
    expect(resolveGoalDates('year', 2026, null, null, today).dates).toEqual({ start: today, end: '2026-12-31' });
    expect(resolveGoalDates('year', 2027, null, null, today).dates?.start).toBe('2027-01-01');
    expect(resolveGoalDates('year', 2026, null, '2026-10-03', today).dates).not.toBeNull();
    expect(resolveGoalDates('year', 2026, null, '2026-10-04', today).problem).toContain('at least 90');
    expect(resolveGoalDates('year', 2026, null, '2026-09-01', today).problem).toContain('past');
  });

  it('refuses a quarter or month with too few days left, and starts one under way today', () => {
    expect(resolveGoalDates('quarter', 2026, 3, null, today).problem).toContain('at least 45');
    expect(resolveGoalDates('month', 2026, 9, null, today).problem).toContain('at least 15');
    expect(resolveGoalDates('quarter', 2026, 4, null, today).dates).toEqual({ start: '2026-10-01', end: '2026-12-31' });
    expect(resolveGoalDates('month', 2026, 10, null, '2026-10-17').dates).toEqual({ start: '2026-10-17', end: '2026-10-31' });
  });

  it('keeps a child in its parent and no earlier than it', () => {
    const parent = { periodType: 'year' as const, periodStart: '2026-10-10', periodEnd: '2026-12-31' };

    expect(resolveGoalDates('quarter', 2026, 4, null, today, parent).dates?.start).toBe('2026-10-10');
    expect(resolveGoalDates('quarter', 2027, 1, null, today, parent).problem).toContain('not in 2026');
  });

  it('labels slots as the server does', () => {
    expect(slotLabel('year', '2026-12-31')).toBe('2026');
    expect(slotLabel('quarter', '2026-12-31')).toBe('Q4 2026');
    expect(slotLabel('month', '2026-10-31')).toBe('Oct 2026');
    expect(monthsOfQuarter('2026-12-31')).toEqual([10, 11, 12]);
  });
});
