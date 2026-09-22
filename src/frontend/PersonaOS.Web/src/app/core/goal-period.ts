import { shiftLocalDate, toLocalDate } from './local-date';

/**
 * The rules a goal's dates keep, mirrored from the server's GoalPeriodCalculator so the form can
 * say what is wrong before saving. The server stays the authority: it checks again, and it is
 * what the assistant and the phone go through.
 *
 * A month goal runs at most 31 days, a quarter at most 90, and a year ends on 31 December of the
 * year it starts. Both ends count.
 */
export type GoalPeriod = 'year' | 'quarter' | 'month';

export const MAX_MONTH_DAYS = 31;
export const MAX_QUARTER_DAYS = 90;

/** The end a goal gets when the user does not pick one. Dates are local `yyyy-MM-dd`. */
export function defaultGoalEnd(period: GoalPeriod, start: string): string {
  switch (period) {
    case 'year':
      return `${start.slice(0, 4)}-12-31`;
    case 'quarter':
      return shiftLocalDate(start, MAX_QUARTER_DAYS - 1);
    case 'month': {
      // A month on, less a day — clamped the way the server's AddMonths clamps, so 31 Jan
      // runs to 27 Feb there and here alike rather than JavaScript rolling into March.
      const [year, month, day] = start.split('-').map(Number);
      const lastOfNext = new Date(year, month + 1, 0).getDate();
      return shiftLocalDate(toLocalDate(new Date(year, month, Math.min(day, lastOfNext))), -1);
    }
  }
}

/** Days from start to end, counting both. */
export function goalDays(start: string, end: string): number {
  const ms = new Date(`${end}T00:00:00`).getTime() - new Date(`${start}T00:00:00`).getTime();
  return Math.round(ms / 86_400_000) + 1;
}

/** Why these dates are not a valid goal of this period, or null when they are. */
export function goalPeriodProblem(period: GoalPeriod, start: string, end: string): string | null {
  if (!start || !end) return 'Pick a start and an end date.';
  if (end < start) return 'The end date is before the start date.';

  const days = goalDays(start, end);
  if (period === 'month' && days > MAX_MONTH_DAYS) {
    return `A monthly goal runs at most ${MAX_MONTH_DAYS} days — this is ${days}. Make it quarterly, or end it sooner.`;
  }
  if (period === 'quarter' && days > MAX_QUARTER_DAYS) {
    return `A quarterly goal runs at most ${MAX_QUARTER_DAYS} days — this is ${days}. Make it yearly, or end it sooner.`;
  }
  if (period === 'year' && end !== `${start.slice(0, 4)}-12-31`) {
    return 'A yearly goal ends on 31 December of the year it starts.';
  }
  return null;
}

/** "22 Sep – 21 Oct 2026", with the year once when both ends share it. */
export function formatGoalRange(start: string, end: string): string {
  const from = new Date(`${start}T00:00:00`);
  const to = new Date(`${end}T00:00:00`);
  const sameYear = from.getFullYear() === to.getFullYear();
  const day = (d: Date, withYear: boolean) =>
    d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', ...(withYear ? { year: 'numeric' } : {}) });
  return `${day(from, !sameYear)} – ${day(to, true)}`;
}
