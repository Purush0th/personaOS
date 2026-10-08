import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, inject, input, output, signal, viewChild,
} from '@angular/core';

import { goalHue } from '../core/board.service';
import { shiftLocalDate, todayLocal } from '../core/local-date';
import { EventChange, PlannerItemDto } from '../core/planner.service';
import {
  PlacedBar, PlacedEvent, SNAP_MINUTES, dayDiff, formatRange, formatTime, hourLabel, isBar, lastDayOf, layoutBars, layoutDay,
  minutesOf, movedTo, parseDay, shortWeekday, snap, timeOf,
} from './calendar';
import { DRAG_THRESHOLD_PX, swallowNextClick } from './drag';

/** One column of the grid: a day and its timed events placed. */
interface DayColumn {
  date: string;
  dayOfMonth: number;
  weekday: string;
  isToday: boolean;
  placed: PlacedEvent[];
}

/** A drag in progress: what is dragged, how, and where it would land now. */
interface Drag {
  kind: 'move' | 'resize' | 'bar';
  item: PlannerItemDto;
  pointerId: number;
  x: number;
  y: number;
  /** The day column the pointer started in. */
  fromDate: string;
  startMinutes: number;
  endMinutes: number;
  /** Past the few pixels that tell a drag from a click. */
  started: boolean;
  /** Where it would land: the day, and for a timed event the start and end. */
  date: string;
  start: number;
  end: number;
}

/** The height of one hour, in pixels. */
const HOUR_PX = 48;

/**
 * The Day, 4 days and Week views: a column per day with a strip on top for all-day events and
 * events over several days (as bars across their days) and the hours below, events placed by
 * their times and side by side where they overlap, and a red line at the current time. Opens on
 * the working morning rather than midnight.
 *
 * Events can be dragged with a mouse, as in Google Calendar: a timed one to another time or day,
 * its bottom edge to change the end, a bar along the top to other days.
 */
@Component({
  selector: 'app-time-grid',
  templateUrl: './time-grid.html',
  styleUrls: ['./time-grid.scss', './drag.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TimeGrid implements AfterViewInit, OnDestroy {
  readonly days = input.required<string[]>();
  /** The visible events of the loaded range, once each. */
  readonly items = input.required<PlannerItemDto[]>();
  readonly itemsByDay = input.required<Map<string, PlannerItemDto[]>>();

  /** An event was clicked. */
  readonly open = output<PlannerItemDto>();
  /** An empty slot was clicked: a new event at that day and time (null for all day). */
  readonly create = output<{ date: string; start: string | null }>();
  /** A day's heading was clicked. */
  readonly openDay = output<string>();
  /** An event was dragged somewhere else, or made longer or shorter. */
  readonly move = output<{ item: PlannerItemDto; change: EventChange }>();

  protected readonly hours = Array.from({ length: 24 }, (_, h) => h);
  protected readonly hourPx = HOUR_PX;
  protected readonly hourLabel = hourLabel;
  protected readonly formatRange = formatRange;
  protected readonly formatTime = formatTime;
  /** "GMT+05:30": the zone the times are in, as Google Calendar labels the hours. */
  protected readonly zone = zoneLabel(new Date());

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly now = signal(new Date());
  private timer?: ReturnType<typeof setInterval>;

  protected readonly drag = signal<Drag | null>(null);

  protected readonly columns = computed<DayColumn[]>(() => {
    const today = todayLocal();
    return this.days().map(date => ({
      date,
      dayOfMonth: parseDay(date).getDate(),
      weekday: shortWeekday(date),
      isToday: date === today,
      placed: layoutDay(this.itemsByDay().get(date) ?? []),
    }));
  });

  /** All-day events and events over several days, as bars across the days they cover. */
  protected readonly bars = computed<PlacedBar[]>(() => layoutBars(this.days(), this.items().filter(isBar)));
  protected readonly barLanes = computed(() => Math.max(0, ...this.bars().map(b => b.lane + 1)));

  /** Minutes past midnight now, for the red line. */
  protected readonly nowMinutes = computed(() => this.now().getHours() * 60 + this.now().getMinutes());

  /** Where a dragged bar would land, as a bar over the days shown. */
  protected readonly barGhost = computed<PlacedBar | null>(() => {
    const drag = this.drag();
    if (!drag?.started || drag.kind !== 'bar') return null;
    const shift = dayDiff(drag.fromDate, drag.date);
    const moved = { ...drag.item, date: shiftLocalDate(drag.item.date, shift), endDate: shiftLocalDate(lastDayOf(drag.item), shift) };
    const placed = layoutBars(this.days(), [moved])[0];
    return placed ? { ...placed, lane: this.bars().find(b => b.item === drag.item)?.lane ?? 0 } : null;
  });

  ngAfterViewInit(): void {
    // Start at 7 am, or an hour before now when today is shown and later than that.
    const showsToday = this.days().includes(todayLocal());
    const hour = showsToday ? Math.max(0, Math.min(7, this.now().getHours() - 1)) : 7;
    const el = this.scroller()?.nativeElement;
    // A little above the hour, so its label is not cut off at the top.
    if (el) el.scrollTop = Math.max(0, hour * HOUR_PX - 12);
    // The hours scroll and the headings and the top strip do not: they leave room for the
    // scrollbar, so their columns line up with the days below.
    if (el) this.host.nativeElement.style.setProperty('--scrollbar', `${el.offsetWidth - el.clientWidth}px`);
    this.timer = setInterval(() => this.now.set(new Date()), 60_000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
    this.stopListening();
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
    if (lastDayOf(item) > item.date) return `${item.title}, ${item.date} to ${lastDayOf(item)}`;
    return item.scheduledTime && item.endTime
      ? `${item.title}, ${formatRange(item.scheduledTime, item.endTime)}`
      : `${item.title}, all day`;
  }

  protected barTime(bar: PlacedBar): string | null {
    const item = bar.item;
    return item.scheduledTime && !bar.startsBefore ? formatTime(item.scheduledTime) : null;
  }


  // --- Dragging ------------------------------------------------------------------------

  /** A press on an event (or its bottom edge) with a mouse or pen: it may become a drag. */
  protected press(event: PointerEvent, item: PlannerItemDto, kind: Drag['kind'], date: string): void {
    // A finger scrolls the page; dragging is for a mouse or a pen. Only the main button drags.
    if (event.pointerType === 'touch' || event.button !== 0) return;
    if (kind === 'resize') event.stopPropagation();
    // A bar spans several columns: what counts is the day under the pointer, not its first.
    if (kind === 'bar') date = this.dayAt(event.clientX) ?? date;
    const start = item.scheduledTime ? minutesOf(item.scheduledTime) : 0;
    const end = item.scheduledTime && item.endTime ? minutesOf(item.endTime) : start + 60;
    this.drag.set({
      kind, item, pointerId: event.pointerId, x: event.clientX, y: event.clientY, fromDate: date,
      startMinutes: start, endMinutes: end > start ? end : start + 60, started: false, date, start, end,
    });
    document.addEventListener('pointermove', this.onMove);
    document.addEventListener('pointerup', this.onUp);
    document.addEventListener('pointercancel', this.onCancel);
  }

  private readonly onMove = (event: PointerEvent): void => {
    const drag = this.drag();
    if (!drag || event.pointerId !== drag.pointerId) return;
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (!drag.started && Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) return;
    event.preventDefault();

    const date = this.dayAt(event.clientX) ?? drag.date;
    const shift = snap((dy / HOUR_PX) * 60);
    const length = drag.endMinutes - drag.startMinutes;
    let { start, end } = drag;
    if (drag.kind === 'move') {
      start = Math.max(0, Math.min(drag.startMinutes + shift, 24 * 60 - SNAP_MINUTES));
      end = Math.min(start + length, 24 * 60 - 1);
    } else if (drag.kind === 'resize') {
      end = Math.max(drag.startMinutes + SNAP_MINUTES, Math.min(drag.endMinutes + shift, 24 * 60 - 1));
    }
    this.drag.set({ ...drag, started: true, date: drag.kind === 'resize' ? drag.date : date, start, end });
  };

  private readonly onUp = (event: PointerEvent): void => {
    const drag = this.drag();
    if (!drag || event.pointerId !== drag.pointerId) return;
    this.stopListening();
    this.drag.set(null);
    if (!drag.started) return;
    swallowNextClick();

    const change = this.changeFor(drag);
    if (change) this.move.emit({ item: drag.item, change });
  };

  private readonly onCancel = (): void => {
    this.stopListening();
    this.drag.set(null);
  };

  private stopListening(): void {
    document.removeEventListener('pointermove', this.onMove);
    document.removeEventListener('pointerup', this.onUp);
    document.removeEventListener('pointercancel', this.onCancel);
  }

  /** What the drop changes, or null when it lands where it was. */
  private changeFor(drag: Drag): EventChange | null {
    const item = drag.item;
    if (drag.kind === 'bar') {
      const shift = dayDiff(drag.fromDate, drag.date);
      return shift === 0 ? null : { date: shiftLocalDate(item.date, shift) };
    }
    if (drag.kind === 'resize') {
      return drag.end === drag.endMinutes ? null : { endTime: timeOf(drag.end) };
    }
    if (drag.date === item.date && drag.start === drag.startMinutes) return null;
    return movedTo(item, drag.date, drag.start);
  }

  /** The day column under a point on the screen, if any. */
  private dayAt(clientX: number): string | null {
    const heads = this.host.nativeElement.querySelectorAll<HTMLElement>('.day-head');
    const days = this.days();
    for (let i = 0; i < heads.length && i < days.length; i++) {
      const rect = heads[i].getBoundingClientRect();
      if (clientX >= rect.left && clientX < rect.right) return days[i];
    }
    return null;
  }

  protected isDragged(item: PlannerItemDto): boolean {
    const drag = this.drag();
    return !!drag?.started && drag.item === item;
  }

  /** Where a dragged timed event would land in this column, or null. */
  protected ghostIn(date: string): { top: number; height: number; label: string } | null {
    const drag = this.drag();
    if (!drag?.started || drag.kind === 'bar' || drag.date !== date) return null;
    return {
      top: this.top(drag.start),
      height: Math.max(((drag.end - drag.start) / 60) * HOUR_PX - 2, 16),
      label: formatRange(timeOf(drag.start), timeOf(drag.end)),
    };
  }
}

export function zoneLabel(at: Date): string {
  const offset = -at.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  const abs = Math.abs(offset);
  return `GMT${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}
