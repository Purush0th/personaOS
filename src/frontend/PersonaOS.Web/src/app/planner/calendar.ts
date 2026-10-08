/**
 * Calendar arithmetic for the planner views, kept free of Angular so it can be tested on its own.
 * Days are `yyyy-MM-dd` calendar days in the user's local calendar (never instants); weeks start
 * on Monday, as the planner's repeat rules do.
 */
import { shiftLocalDate, toLocalDate } from '../core/local-date';
import { EventChange, PlannerItemDto } from '../core/planner.service';

export type CalendarView = 'day' | 'week' | 'month' | 'year' | 'schedule' | '4day';

export interface ViewOption {
  view: CalendarView;
  label: string;
  /** The keyboard shortcut, as Google Calendar has it. */
  key: string;
}

export const VIEWS: ViewOption[] = [
  { view: 'day', label: 'Day', key: 'D' },
  { view: 'week', label: 'Week', key: 'W' },
  { view: 'month', label: 'Month', key: 'M' },
  { view: 'year', label: 'Year', key: 'Y' },
  { view: 'schedule', label: 'Schedule', key: 'A' },
  { view: '4day', label: '4 days', key: 'X' },
];

/** How many days the Schedule view lists at a time. */
export const SCHEDULE_DAYS = 30;

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// --- Days ------------------------------------------------------------------------------

export function parseDay(day: string): Date {
  return new Date(`${day}T00:00:00`);
}

/** 0 = Sunday … 6 = Saturday. */
export function weekday(day: string): number {
  return parseDay(day).getDay();
}

export function weekdayName(day: string): string {
  return WEEKDAYS[weekday(day)];
}

export function shortWeekday(day: string): string {
  return WEEKDAYS[weekday(day)].slice(0, 3).toUpperCase();
}

export function monthName(month: number): string {
  return MONTHS[month];
}

export function startOfWeek(day: string): string {
  return shiftLocalDate(day, -((weekday(day) + 6) % 7));
}

export function startOfMonth(day: string): string {
  const d = parseDay(day);
  return toLocalDate(new Date(d.getFullYear(), d.getMonth(), 1));
}

export function addMonths(day: string, months: number): string {
  const d = parseDay(day);
  // Keep to the first of the month: the 31st plus a month is not the 1st of the month after.
  return toLocalDate(new Date(d.getFullYear(), d.getMonth() + months, 1));
}

export function addYears(day: string, years: number): string {
  const d = parseDay(day);
  return toLocalDate(new Date(d.getFullYear() + years, 0, 1));
}

/** Every day from `from` to `to`, inclusive. */
export function daysBetween(from: string, to: string): string[] {
  const days: string[] = [];
  for (let day = from; day <= to; day = shiftLocalDate(day, 1)) days.push(day);
  return days;
}

export function isWeekend(day: string): boolean {
  const d = weekday(day);
  return d === 0 || d === 6;
}

// --- Views -----------------------------------------------------------------------------

/** The days a view shows around `anchor`, and the range to load for it. */
export function viewRange(view: CalendarView, anchor: string): { from: string; to: string } {
  switch (view) {
    case 'day':
      return { from: anchor, to: anchor };
    case '4day':
      return { from: anchor, to: shiftLocalDate(anchor, 3) };
    case 'week': {
      const from = startOfWeek(anchor);
      return { from, to: shiftLocalDate(from, 6) };
    }
    case 'month': {
      const grid = monthGrid(anchor);
      return { from: grid[0][0], to: grid[grid.length - 1][6] };
    }
    case 'year': {
      const year = parseDay(anchor).getFullYear();
      return { from: startOfWeek(`${year}-01-01`), to: shiftLocalDate(startOfWeek(`${year}-12-31`), 6) };
    }
    case 'schedule':
      return { from: anchor, to: shiftLocalDate(anchor, SCHEDULE_DAYS - 1) };
  }
}

/** The anchor one step before or after: a day, 4 days, a week, a month, a year, or a schedule page. */
export function stepAnchor(view: CalendarView, anchor: string, direction: 1 | -1): string {
  switch (view) {
    case 'day':
      return shiftLocalDate(anchor, direction);
    case '4day':
      return shiftLocalDate(anchor, 4 * direction);
    case 'week':
      return shiftLocalDate(anchor, 7 * direction);
    case 'month':
      return addMonths(anchor, direction);
    case 'year':
      return addYears(anchor, direction);
    case 'schedule':
      return shiftLocalDate(anchor, SCHEDULE_DAYS * direction);
  }
}

/** The heading for a view: "3 October 2026", "Sept – Oct 2026", "October 2026", "2026". */
export function viewTitle(view: CalendarView, anchor: string): string {
  const { from, to } = view === 'month' || view === 'year' ? { from: anchor, to: anchor } : viewRange(view, anchor);
  const a = parseDay(from);
  const b = parseDay(to);
  if (view === 'year') return `${a.getFullYear()}`;
  if (view === 'month') return `${MONTHS[a.getMonth()]} ${a.getFullYear()}`;
  if (view === 'day') return `${a.getDate()} ${MONTHS[a.getMonth()]} ${a.getFullYear()}`;
  if (a.getMonth() === b.getMonth()) return `${MONTHS[a.getMonth()]} ${a.getFullYear()}`;
  const short = (d: Date) => (d.getMonth() === 8 ? 'Sept' : MONTHS[d.getMonth()].slice(0, 3));
  return a.getFullYear() === b.getFullYear()
    ? `${short(a)} – ${short(b)} ${b.getFullYear()}`
    : `${short(a)} ${a.getFullYear()} – ${short(b)} ${b.getFullYear()}`;
}

/** The weeks shown for the month of `anchor`: Monday to Sunday, from the week of the 1st to the week of the last day. */
export function monthGrid(anchor: string): string[][] {
  const first = startOfMonth(anchor);
  const d = parseDay(first);
  const last = toLocalDate(new Date(d.getFullYear(), d.getMonth() + 1, 0));
  const weeks: string[][] = [];
  for (let start = startOfWeek(first); start <= last; start = shiftLocalDate(start, 7)) {
    weeks.push(daysBetween(start, shiftLocalDate(start, 6)));
  }
  return weeks;
}

// --- Times -----------------------------------------------------------------------------

/** Minutes after midnight of an `HH:mm` or `HH:mm:ss` time. */
export function minutesOf(time: string): number {
  const [h, m] = time.split(':').map(Number);
  return h * 60 + m;
}

export function timeOf(minutes: number): string {
  const clamped = Math.max(0, Math.min(minutes, 23 * 60 + 59));
  return `${String(Math.floor(clamped / 60)).padStart(2, '0')}:${String(clamped % 60).padStart(2, '0')}`;
}

/** "8:30am", "12pm", "4pm", as Google Calendar writes times. */
export function formatTime(time: string): string {
  const minutes = minutesOf(time);
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  const suffix = h < 12 ? 'am' : 'pm';
  const hour = h % 12 === 0 ? 12 : h % 12;
  return m === 0 ? `${hour}${suffix}` : `${hour}:${String(m).padStart(2, '0')}${suffix}`;
}

/** "8:30 – 9:30am", "12 – 3:30pm", "11am – 1pm": the suffix once when both halves share it. */
export function formatRange(start: string, end: string): string {
  const a = formatTime(start);
  const b = formatTime(end);
  const sameHalf = (minutesOf(start) < 720) === (minutesOf(end) < 720);
  return sameHalf ? `${a.replace(/am|pm/, '')} – ${b}` : `${a} – ${b}`;
}

/** The label of an hour row: "7 AM", "12 PM". */
export function hourLabel(hour: number): string {
  const h = hour % 12 === 0 ? 12 : hour % 12;
  return `${h} ${hour < 12 ? 'AM' : 'PM'}`;
}

export function isAllDay(item: PlannerItemDto): boolean {
  return item.scheduledTime === null;
}

// --- Several days ----------------------------------------------------------------------

/** The last day of an event: its own day unless it lasts several. */
export function lastDayOf(item: PlannerItemDto): string {
  return item.endDate && item.endDate > item.date ? item.endDate : item.date;
}

export function isMultiDay(item: PlannerItemDto): boolean {
  return lastDayOf(item) > item.date;
}

/** Drawn as a bar across the top of Day and Week: all day, or over several days (timed or not). */
export function isBar(item: PlannerItemDto): boolean {
  return item.scheduledTime === null || isMultiDay(item);
}

/** Days from `a` to `b` (negative when `b` is earlier). */
export function dayDiff(a: string, b: string): number {
  return Math.round((parseDay(b).getTime() - parseDay(a).getTime()) / 86_400_000);
}

/** Which of its days `day` is, and how many it has: "(Day 2/3)" as Google Calendar titles it. */
export function dayOfSpan(item: PlannerItemDto, day: string): { n: number; of: number } {
  return { n: dayDiff(item.date, day) + 1, of: dayDiff(item.date, lastDayOf(item)) + 1 };
}

/**
 * Each loaded event under every day it covers, so a day's cell, count or list includes an event
 * that began earlier. Days outside `from`…`to` are left out.
 */
export function eventsByDay(items: PlannerItemDto[], from: string, to: string): Map<string, PlannerItemDto[]> {
  const byDay = new Map<string, PlannerItemDto[]>();
  for (const item of items) {
    const first = item.date < from ? from : item.date;
    const last = lastDayOf(item) > to ? to : lastDayOf(item);
    for (let day = first; day <= last; day = shiftLocalDate(day, 1)) {
      byDay.set(day, [...(byDay.get(day) ?? []), item]);
    }
  }
  return byDay;
}

/** An event drawn as a bar over a row of days: its first column, how many it spans, and its lane. */
export interface PlacedBar {
  item: PlannerItemDto;
  start: number;
  span: number;
  lane: number;
  /** It began before the first day shown, or goes on after the last: the bar's end is cut square. */
  startsBefore: boolean;
  endsAfter: boolean;
}

/**
 * Places events on a row of days (a week, or the days of the Day and Week views) in lanes, as
 * Google Calendar stacks them: the earliest first, the longest first among those, all-day before
 * timed, each in the first lane free over all its days. `days` may skip some (weekends hidden).
 */
export function layoutBars(days: string[], items: PlannerItemDto[]): PlacedBar[] {
  const placed: PlacedBar[] = [];
  for (const item of items) {
    const last = lastDayOf(item);
    const start = days.findIndex(d => d >= item.date && d <= last);
    if (start < 0) continue;
    let end = start;
    while (end + 1 < days.length && days[end + 1] <= last) end++;
    placed.push({ item, start, span: end - start + 1, lane: 0, startsBefore: item.date < days[start], endsAfter: last > days[end] });
  }

  placed.sort((a, b) =>
    a.start - b.start
    || b.span - a.span
    || Number(a.item.scheduledTime !== null) - Number(b.item.scheduledTime !== null)
    || (a.item.scheduledTime ?? '').localeCompare(b.item.scheduledTime ?? ''));

  const taken: boolean[][] = [];
  for (const bar of placed) {
    let lane = 0;
    while (taken[lane]?.slice(bar.start, bar.start + bar.span).some(Boolean)) lane++;
    taken[lane] ??= [];
    for (let i = bar.start; i < bar.start + bar.span; i++) taken[lane][i] = true;
    bar.lane = lane;
  }
  return placed;
}

// --- Dragging --------------------------------------------------------------------------

/** Dragged events snap to quarter hours, as in Google Calendar. */
export const SNAP_MINUTES = 15;

export function snap(minutes: number): number {
  return Math.round(minutes / SNAP_MINUTES) * SNAP_MINUTES;
}

/** The change that moves a timed event to `date` at `start`, keeping its length (cut at midnight). */
export function movedTo(item: PlannerItemDto, date: string, start: number): EventChange {
  const length = minutesOf(item.endTime ?? item.scheduledTime!) - minutesOf(item.scheduledTime!);
  const change: EventChange = { scheduledTime: timeOf(start), endTime: timeOf(start + (length > 0 ? length : 60)) };
  if (date !== item.date) change.date = date;
  return change;
}

// --- Laying out a day ------------------------------------------------------------------

/** A timed event placed in a day column: its top and height in minutes, and its share of the width. */
export interface PlacedEvent {
  item: PlannerItemDto;
  startMinutes: number;
  endMinutes: number;
  column: number;
  columns: number;
}

/** The shortest an event is drawn, so a 5-minute event can still be read and clicked. */
export const MIN_EVENT_MINUTES = 20;

/**
 * Places a day's timed events side by side where they overlap, as Google Calendar does: each run
 * of overlapping events shares the width, and an event takes the first column free at its start.
 * One over several days is not here: it is a bar across the top.
 */
export function layoutDay(items: PlannerItemDto[]): PlacedEvent[] {
  const timed = items
    .filter(i => !isBar(i))
    .map(item => {
      const start = minutesOf(item.scheduledTime!);
      const end = Math.max(item.endTime ? minutesOf(item.endTime) : start + 60, start + MIN_EVENT_MINUTES);
      return { item, startMinutes: start, endMinutes: Math.min(end, 24 * 60), column: 0, columns: 1 };
    })
    .sort((a, b) => a.startMinutes - b.startMinutes || b.endMinutes - a.endMinutes);

  const placed: PlacedEvent[] = [];
  let cluster: PlacedEvent[] = [];
  let clusterEnd = -1;
  const close = () => {
    const columns = Math.max(1, ...cluster.map(e => e.column + 1));
    for (const e of cluster) e.columns = columns;
    placed.push(...cluster);
    cluster = [];
  };

  for (const event of timed) {
    if (event.startMinutes >= clusterEnd) close();
    const taken = new Set(cluster.filter(e => e.endMinutes > event.startMinutes).map(e => e.column));
    let column = 0;
    while (taken.has(column)) column++;
    event.column = column;
    cluster.push(event);
    clusterEnd = Math.max(clusterEnd, event.endMinutes);
  }

  close();
  return placed;
}

// --- Repeat ----------------------------------------------------------------------------

export interface RepeatOption {
  label: string;
  /** The rule text, or null for "Does not repeat"; 'custom' opens the custom editor. */
  rule: string | null | 'custom';
}

const ORDINALS = ['first', 'second', 'third', 'fourth'];
const CODES = ['SU', 'MO', 'TU', 'WE', 'TH', 'FR', 'SA'];

/** Which weekday of its month a day is (1 to 5), and whether it is also the last one. */
export function nthWeekday(day: string): { n: number; last: boolean } {
  const d = parseDay(day);
  const n = Math.floor((d.getDate() - 1) / 7) + 1;
  const later = new Date(d);
  later.setDate(d.getDate() + 7);
  return { n, last: later.getMonth() !== d.getMonth() };
}

/** The repeat menu for an event on `day`, as Google Calendar offers it. */
export function repeatOptions(day: string): RepeatOption[] {
  const d = parseDay(day);
  const name = weekdayName(day);
  const code = CODES[d.getDay()];
  const { n, last } = nthWeekday(day);
  const options: RepeatOption[] = [
    { label: 'Does not repeat', rule: null },
    { label: 'Daily', rule: 'FREQ=DAILY' },
    { label: `Weekly on ${name}`, rule: 'FREQ=WEEKLY' },
  ];
  // A fifth weekday is not in every month, so it is only offered as "the last".
  if (n <= 4) options.push({ label: `Monthly on the ${ORDINALS[n - 1]} ${name}`, rule: `FREQ=MONTHLY;BYDAY=${n}${code}` });
  if (last) options.push({ label: `Monthly on the last ${name}`, rule: `FREQ=MONTHLY;BYDAY=-1${code}` });
  options.push(
    { label: `Annually on ${MONTHS[d.getMonth()]} ${d.getDate()}`, rule: 'FREQ=YEARLY' },
    { label: 'Every weekday (Monday to Friday)', rule: 'FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR' },
    { label: 'Custom…', rule: 'custom' },
  );
  return options;
}

export type RepeatUnit = 'DAILY' | 'WEEKLY' | 'MONTHLY' | 'YEARLY';
export type RepeatEnd = 'never' | 'on' | 'after';

/** The custom repeat editor's state. */
export interface CustomRepeat {
  interval: number;
  unit: RepeatUnit;
  /** Weekly: day codes, MO…SU. */
  days: string[];
  /** Monthly: on the same day of the month, or on the same weekday of it. */
  monthlyBy: 'day' | 'weekday';
  end: RepeatEnd;
  until: string;
  count: number;
}

export function defaultCustomRepeat(day: string): CustomRepeat {
  return {
    interval: 1,
    unit: 'WEEKLY',
    days: [CODES[weekday(day)]],
    monthlyBy: 'day',
    end: 'never',
    until: shiftLocalDate(day, 90),
    count: 10,
  };
}

/** The rule text of a custom repeat for an event first on `day`. */
export function customRule(repeat: CustomRepeat, day: string): string {
  const parts = [`FREQ=${repeat.unit}`];
  if (repeat.interval > 1) parts.push(`INTERVAL=${repeat.interval}`);
  if (repeat.unit === 'WEEKLY' && repeat.days.length > 0) {
    parts.push(`BYDAY=${WEEK_ORDER.filter(c => repeat.days.includes(c)).join(',')}`);
  }
  if (repeat.unit === 'MONTHLY') {
    if (repeat.monthlyBy === 'weekday') {
      const { n } = nthWeekday(day);
      parts.push(`BYDAY=${n > 4 ? -1 : n}${CODES[weekday(day)]}`);
    } else {
      parts.push(`BYMONTHDAY=${parseDay(day).getDate()}`);
    }
  }
  if (repeat.end === 'on') parts.push(`UNTIL=${repeat.until.replaceAll('-', '')}`);
  if (repeat.end === 'after') parts.push(`COUNT=${repeat.count}`);
  return parts.join(';');
}

/** Reads a rule back into the custom editor, for editing an event that already repeats. */
export function parseCustom(rule: string, day: string): CustomRepeat {
  const repeat = defaultCustomRepeat(day);
  for (const part of rule.replace(/^RRULE:/i, '').split(';')) {
    const [key, value] = part.split('=');
    switch (key?.toUpperCase()) {
      case 'FREQ':
        repeat.unit = value.toUpperCase() as RepeatUnit;
        break;
      case 'INTERVAL':
        repeat.interval = Number(value) || 1;
        break;
      case 'BYDAY':
        if (/^-?\d/.test(value)) repeat.monthlyBy = 'weekday';
        else repeat.days = value.split(',');
        break;
      case 'UNTIL':
        repeat.end = 'on';
        repeat.until = `${value.slice(0, 4)}-${value.slice(4, 6)}-${value.slice(6, 8)}`;
        break;
      case 'COUNT':
        repeat.end = 'after';
        repeat.count = Number(value) || 1;
        break;
    }
  }
  return repeat;
}

export const WEEK_ORDER = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'];

// --- Notification ----------------------------------------------------------------------

export type NotifyUnit = 'minutes' | 'hours' | 'days' | 'weeks';

export const NOTIFY_UNIT_MINUTES: Record<NotifyUnit, number> = { minutes: 1, hours: 60, days: 1440, weeks: 10080 };

/** The longest lead the server accepts: four weeks, as in Google Calendar. */
export const MAX_NOTIFY_MINUTES = 4 * 10080;

/** The lead a new timed event starts with. */
export const DEFAULT_NOTIFY_MINUTES = 30;

/** The time of day an all-day event's notification counts from. */
export const ALL_DAY_ALERT_REFERENCE = '09:00';

/** The notification choices: the owner's list, then "Custom…". Null is no notification. */
export const NOTIFY_PRESETS: { minutes: number | null; label: string }[] = [
  { minutes: null, label: 'No notification' },
  { minutes: 5, label: '5 minutes before' },
  { minutes: 10, label: '10 minutes before' },
  { minutes: 15, label: '15 minutes before' },
  { minutes: 25, label: '25 minutes before' },
  { minutes: 30, label: '30 minutes before' },
  { minutes: 60, label: '1 hour before' },
  { minutes: 120, label: '2 hours before' },
  { minutes: 240, label: '4 hours before' },
  { minutes: 480, label: '8 hours before' },
  { minutes: 1440, label: '1 day before' },
];

/** "30 minutes", "1 hour", "2 days", "1 week": the largest unit that divides it evenly. */
export function leadText(minutes: number): string {
  const unit = largestUnit(minutes);
  const count = minutes / NOTIFY_UNIT_MINUTES[unit];
  const name = unit.slice(0, -1);
  return `${count} ${count === 1 ? name : unit}`;
}

export function largestUnit(minutes: number): NotifyUnit {
  if (minutes > 0 && minutes % 10080 === 0) return 'weeks';
  if (minutes > 0 && minutes % 1440 === 0) return 'days';
  if (minutes > 0 && minutes % 60 === 0) return 'hours';
  return 'minutes';
}

export function notifyLabel(minutes: number | null): string {
  if (minutes === null) return 'No notification';
  if (minutes === 0) return 'At the start';
  return `${leadText(minutes)} before`;
}

/**
 * When the phone would be notified for an event on `day` at `start` (all day counts from 9 am),
 * `minutes` before. Local time.
 */
export function notifyAt(day: string, start: string | null, minutes: number): Date {
  const at = new Date(`${day}T${(start ?? ALL_DAY_ALERT_REFERENCE).slice(0, 5)}:00`);
  at.setMinutes(at.getMinutes() - minutes);
  return at;
}

/** "Fri 2 Oct, 10:00 am". */
export function formatMoment(at: Date): string {
  const time = at.toLocaleTimeString('en-GB', { hour: 'numeric', minute: '2-digit', hour12: true });
  return `${WEEKDAYS[at.getDay()].slice(0, 3)} ${at.getDate()} ${MONTHS[at.getMonth()].slice(0, 3)}, ${time}`;
}
