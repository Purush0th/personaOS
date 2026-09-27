import { shiftLocalDate } from './local-date';

/**
 * Where a goal sits on the calendar, mirrored from the server's GoalCalendar so a form can show
 * the dates and say what is wrong before saving. The server stays the authority: it checks again,
 * and it is what the assistant and the phone go through.
 *
 * Every goal follows the calendar and never starts in the past:
 * - a year starts today or on a later day picked, ends 31 December, and needs 90 days;
 * - a quarter is Q1–Q4 of a year, starts on the later of today and its first day, and needs 45;
 * - a month is a calendar month, the same way, and needs 15.
 * A nested goal's slot lies in its parent's year or quarter, and it starts no earlier than the
 * parent. Dates are local `yyyy-MM-dd`; both ends count.
 */
export type GoalPeriod = 'year' | 'quarter' | 'month';

export const MIN_DAYS: Record<GoalPeriod, number> = { year: 90, quarter: 45, month: 15 };

/** The goal types as the user reads them, in the order they are offered (owner, 2026-09-27). */
export const PERIOD_LABELS: Record<GoalPeriod, string> = { month: 'Monthly', quarter: 'Quarterly', year: 'Yearly' };

/** "monthly goal", "quarterly goals": goals of a type, counted. */
export function goalsOfType(type: GoalPeriod, count = 1): string {
  return `${PERIOD_LABELS[type].toLowerCase()} ${count === 1 ? 'goal' : 'goals'}`;
}

/** The type a goal's parent must have; null for a year, which never nests. */
export function parentTypeOf(type: GoalPeriod): GoalPeriod | null {
  return type === 'quarter' ? 'year' : type === 'month' ? 'quarter' : null;
}

/** The type of goal that nests under this one; null for a month. */
export function childTypeOf(type: GoalPeriod): GoalPeriod | null {
  return type === 'year' ? 'quarter' : type === 'quarter' ? 'month' : null;
}

export interface GoalDates {
  start: string;
  end: string;
}

/** What a nested goal needs of its parent. */
export interface ParentSlot {
  periodType: GoalPeriod;
  periodStart: string;
  periodEnd: string;
}

const pad = (n: number) => `${n}`.padStart(2, '0');
const date = (year: number, month: number, day: number) => `${year}-${pad(month)}-${pad(day)}`;
const lastDay = (year: number, month: number) => new Date(year, month, 0).getDate();

export function quarterOf(day: string): number {
  return Math.floor((Number(day.slice(5, 7)) - 1) / 3) + 1;
}

export function quarterDates(year: number, quarter: number): GoalDates {
  const first = (quarter - 1) * 3 + 1;
  return { start: date(year, first, 1), end: date(year, first + 2, lastDay(year, first + 2)) };
}

export function monthDates(year: number, month: number): GoalDates {
  return { start: date(year, month, 1), end: date(year, month, lastDay(year, month)) };
}

/** Days from start to end, counting both. */
export function goalDays(start: string, end: string): number {
  const ms = new Date(`${end}T00:00:00`).getTime() - new Date(`${start}T00:00:00`).getTime();
  return Math.round(ms / 86_400_000) + 1;
}

const MONTH_NAMES = Array.from({ length: 12 }, (_, i) =>
  new Date(2000, i, 1).toLocaleDateString('en', { month: 'short' }));

export function monthName(month: number): string {
  return MONTH_NAMES[month - 1];
}

/** "2026", "Q4 2026" or "Oct 2026" — the same as the server's label. */
export function slotLabel(type: GoalPeriod, end: string): string {
  const year = end.slice(0, 4);
  if (type === 'quarter') return `Q${quarterOf(end)} ${year}`;
  if (type === 'month') return `${monthName(Number(end.slice(5, 7)))} ${year}`;
  return year;
}

/** The months of a quarter (by its end), e.g. [10, 11, 12]. */
export function monthsOfQuarter(quarterEnd: string): number[] {
  const first = (quarterOf(quarterEnd) - 1) * 3 + 1;
  return [first, first + 1, first + 2];
}

/**
 * The dates of a goal of `type` in the slot given, or why there are none.
 * @param slot the quarter (1–4) or month (1–12); unused for a year.
 * @param start a year goal's chosen start (today when empty).
 */
export function resolveGoalDates(
  type: GoalPeriod, year: number, slot: number | null, start: string | null, today: string, parent: ParentSlot | null = null,
): { dates: GoalDates | null; problem: string | null } {
  const fail = (problem: string) => ({ dates: null, problem });

  if (type === 'year') {
    const first = start || (year > Number(today.slice(0, 4)) ? date(year, 1, 1) : today);
    if (Number(first.slice(0, 4)) !== year) return fail(`A yearly goal for ${year} must start in ${year}.`);
    if (first < today) return fail('A goal cannot start in the past.');
    const end = date(year, 12, 31);
    const days = goalDays(first, end);
    if (days < MIN_DAYS.year) {
      return fail(`A yearly goal needs at least ${MIN_DAYS.year} days; this has ${days}. Start it by ${shiftLocalDate(end, -(MIN_DAYS.year - 1))}, or pick next year.`);
    }
    return { dates: { start: first, end }, problem: null };
  }

  if (slot === null) return fail(type === 'quarter' ? 'Pick a quarter.' : 'Pick a month.');
  if (year < Number(today.slice(0, 4))) return fail(`${year} has already passed.`);
  const calendar = type === 'quarter' ? quarterDates(year, slot) : monthDates(year, slot);
  const label = slotLabel(type, calendar.end);
  if (calendar.end < today) return fail(`${label} has already ended.`);

  let first = calendar.start < today ? today : calendar.start;
  if (parent) {
    const within = parent.periodType === 'year'
      ? { start: date(Number(parent.periodEnd.slice(0, 4)), 1, 1), end: parent.periodEnd }
      : { start: quarterDates(Number(parent.periodEnd.slice(0, 4)), quarterOf(parent.periodEnd)).start, end: parent.periodEnd };
    if (calendar.start < within.start || calendar.end > within.end) {
      return fail(`${label} is not in ${slotLabel(parent.periodType, parent.periodEnd)}.`);
    }
    if (parent.periodStart > first) first = parent.periodStart;
  }

  const days = goalDays(first, calendar.end);
  const min = MIN_DAYS[type];
  if (days < min) {
    return fail(`${label} has only ${days} days left; a ${type === 'quarter' ? 'quarterly' : 'monthly'} goal needs at least ${min}.`);
  }
  return { dates: { start: first, end: calendar.end }, problem: null };
}

/** "22 Sep – 31 Dec 2026", with the year once when both ends share it. */
export function formatGoalRange(start: string, end: string): string {
  const from = new Date(`${start}T00:00:00`);
  const to = new Date(`${end}T00:00:00`);
  const sameYear = from.getFullYear() === to.getFullYear();
  const day = (d: Date, withYear: boolean) =>
    d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', ...(withYear ? { year: 'numeric' } : {}) });
  return `${day(from, !sameYear)} – ${day(to, true)}`;
}
