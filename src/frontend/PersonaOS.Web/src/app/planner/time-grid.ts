import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, input, output, signal, viewChild,
} from '@angular/core';

import { goalHue } from '../core/board.service';
import { todayLocal } from '../core/local-date';
import { PlannerItemDto } from '../core/planner.service';
import { PlacedEvent, formatRange, formatTime, hourLabel, layoutDay, parseDay, shortWeekday, timeOf } from './calendar';

/** One column of the grid: a day, its all-day events and its timed events placed. */
interface DayColumn {
  date: string;
  dayOfMonth: number;
  weekday: string;
  isToday: boolean;
  allDay: PlannerItemDto[];
  placed: PlacedEvent[];
}

/** The height of one hour, in pixels. */
const HOUR_PX = 48;

/**
 * The Day, 4 days and Week views: a column per day with an all-day strip on top and the hours
 * below, events placed by their times and side by side where they overlap, and a red line at
 * the current time. Opens on the working morning rather than midnight.
 */
@Component({
  selector: 'app-time-grid',
  templateUrl: './time-grid.html',
  styleUrl: './time-grid.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TimeGrid implements AfterViewInit, OnDestroy {
  readonly days = input.required<string[]>();
  readonly itemsByDay = input.required<Map<string, PlannerItemDto[]>>();

  /** An event was clicked. */
  readonly open = output<PlannerItemDto>();
  /** An empty slot was clicked: a new event at that day and time (null for all day). */
  readonly create = output<{ date: string; start: string | null }>();
  /** A day's heading was clicked. */
  readonly openDay = output<string>();

  protected readonly hours = Array.from({ length: 24 }, (_, h) => h);
  protected readonly hourPx = HOUR_PX;
  protected readonly hourLabel = hourLabel;
  protected readonly formatRange = formatRange;
  protected readonly formatTime = formatTime;
  /** "GMT+05:30": the zone the times are in, as Google Calendar labels the hours. */
  protected readonly zone = zoneLabel(new Date());

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly now = signal(new Date());
  private timer?: ReturnType<typeof setInterval>;

  protected readonly columns = computed<DayColumn[]>(() => {
    const today = todayLocal();
    return this.days().map(date => {
      const items = this.itemsByDay().get(date) ?? [];
      return {
        date,
        dayOfMonth: parseDay(date).getDate(),
        weekday: shortWeekday(date),
        isToday: date === today,
        allDay: items.filter(i => i.scheduledTime === null),
        placed: layoutDay(items),
      };
    });
  });

  protected readonly hasAllDay = computed(() => this.columns().some(c => c.allDay.length > 0));

  /** Minutes past midnight now, for the red line. */
  protected readonly nowMinutes = computed(() => this.now().getHours() * 60 + this.now().getMinutes());

  ngAfterViewInit(): void {
    // Start at 7 am, or an hour before now when today is shown and later than that.
    const showsToday = this.days().includes(todayLocal());
    const hour = showsToday ? Math.max(0, Math.min(7, this.now().getHours() - 1)) : 7;
    const el = this.scroller()?.nativeElement;
    // A little above the hour, so its label is not cut off at the top.
    if (el) el.scrollTop = Math.max(0, hour * HOUR_PX - 12);
    this.timer = setInterval(() => this.now.set(new Date()), 60_000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected top(minutes: number): number {
    return (minutes / 60) * HOUR_PX;
  }

  protected height(event: PlacedEvent): number {
    return Math.max(((event.endMinutes - event.startMinutes) / 60) * HOUR_PX - 2, 16);
  }

  /** Short events have room for one line only: title and time side by side. */
  protected compact(event: PlacedEvent): boolean {
    return event.endMinutes - event.startMinutes < 45;
  }

  protected hue(item: PlannerItemDto): number | null {
    return item.goalKey ? goalHue(item.goalKey) : null;
  }

  protected isPast(date: string, event: PlacedEvent | null): boolean {
    const today = todayLocal();
    if (date < today) return true;
    if (date > today || !event) return false;
    return event.endMinutes <= this.nowMinutes();
  }

  /** A click on an empty part of a day: a new event at that half hour. */
  protected slotClick(event: MouseEvent, date: string): void {
    const column = event.currentTarget as HTMLElement;
    const y = event.clientY - column.getBoundingClientRect().top;
    const minutes = Math.floor((y / HOUR_PX) * 2) * 30;
    this.create.emit({ date, start: timeOf(Math.min(minutes, 23 * 60)) });
  }

  protected label(item: PlannerItemDto): string {
    return item.scheduledTime && item.endTime
      ? `${item.title}, ${formatRange(item.scheduledTime, item.endTime)}`
      : `${item.title}, all day`;
  }

}

export function zoneLabel(at: Date): string {
  const offset = -at.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  const abs = Math.abs(offset);
  return `GMT${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}
