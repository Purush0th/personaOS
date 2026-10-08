import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, inject, input, output, signal } from '@angular/core';

import { goalHue } from '../core/board.service';
import { shiftLocalDate, todayLocal } from '../core/local-date';
import { EventChange, PlannerItemDto } from '../core/planner.service';
import { PlacedBar, dayDiff, formatTime, isBar, isWeekend, lastDayOf, layoutBars, monthGrid, monthName, parseDay, shortWeekday } from './calendar';
import { DRAG_THRESHOLD_PX, swallowNextClick } from './drag';

/** How many lanes of events a week shows before "N more" (one more when that fits them all). */
const MAX_LANES = 3;

interface Cell {
  date: string;
  label: string;
  inMonth: boolean;
  isToday: boolean;
  /** Events on this day in lanes that are not shown. */
  more: number;
}

interface Week {
  days: Cell[];
  /** The events shown, in lanes; one over several days spans their columns. */
  bars: PlacedBar[];
  lanes: number;
}

interface Drag {
  item: PlannerItemDto;
  pointerId: number;
  x: number;
  y: number;
  /** The day under the pointer when it was pressed, and the day under it now. */
  fromDate: string;
  date: string;
  started: boolean;
}

/**
 * The Month view: whole weeks from the week of the 1st. An all-day event, or one over several
 * days, is a coloured bar across its days; a timed one is a dot and its start; "N more" when a
 * day is full. A day's number opens that day. With a mouse, an event can be dragged to another day.
 */
@Component({
  selector: 'app-month-view',
  template: `
    <div class="month" [style.--cols]="weekdays().length" [class.dragging]="drag()?.started">
      <div class="weekdays">
        @for (name of weekdays(); track name) {
          <span>{{ name }}</span>
        }
      </div>
      @for (week of weeks(); track week.days[0].date) {
        <div class="week" [style.--lanes]="week.lanes">
          @for (cell of week.days; track cell.date; let i = $index) {
            <div
              class="cell"
              [attr.data-date]="cell.date"
              [class.out]="!cell.inMonth"
              [class.drop]="isDropTarget(cell.date)"
              [style.grid-column]="i + 1"
              (click)="create.emit(cell.date)"
            >
              <button
                type="button"
                class="day-number"
                [class.today]="cell.isToday"
                [attr.aria-label]="'Open ' + cell.date"
                (click)="$event.stopPropagation(); openDay.emit(cell.date)"
              >
                {{ cell.label }}
              </button>
            </div>
          }
          @for (bar of week.bars; track bar.item.id + bar.item.occurrenceDate) {
            <button
              type="button"
              data-testid="event"
              [class]="isBar(bar.item) ? 'bar' : 'timed'"
              [class.goal]="hue(bar.item) !== null"
              [style.--hue]="hue(bar.item)"
              [class.done]="bar.item.status !== 'planned'"
              [class.past]="lastDayOf(bar.item) < today"
              [class.cut-start]="bar.startsBefore"
              [class.cut-end]="bar.endsAfter"
              [class.lifted]="drag()?.started && drag()!.item === bar.item"
              [style.grid-column]="bar.start + 1 + ' / span ' + bar.span"
              [style.grid-row]="bar.lane + 2"
              (pointerdown)="press($event, bar.item)"
              (click)="$event.stopPropagation(); open.emit(bar.item)"
            >
              @if (!isBar(bar.item)) {
                <span class="dot"></span>
                <span class="time">{{ time(bar.item.scheduledTime!) }}</span>
              } @else if (bar.item.scheduledTime && !bar.startsBefore) {
                <span class="time">{{ time(bar.item.scheduledTime) }}</span>
              }
              <span class="title">{{ bar.item.title }}</span>
            </button>
          }
          @for (cell of week.days; track cell.date; let i = $index) {
            @if (cell.more > 0) {
              <button
                type="button"
                class="more"
                [style.grid-column]="i + 1"
                [style.grid-row]="week.lanes + 2"
                (click)="$event.stopPropagation(); openDay.emit(cell.date)"
              >
                {{ cell.more }} more
              </button>
            }
          }
        </div>
      }
    </div>
  `,
  styleUrls: ['./month-view.scss', './drag.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class MonthView implements OnDestroy {
  readonly anchor = input.required<string>();
  /** The visible events of the loaded range, once each. */
  readonly items = input.required<PlannerItemDto[]>();
  readonly showWeekends = input(true);

  readonly open = output<PlannerItemDto>();
  readonly openDay = output<string>();
  /** An empty part of a day was clicked: a new all-day event there. */
  readonly create = output<string>();
  /** An event was dragged to another day. */
  readonly move = output<{ item: PlannerItemDto; change: EventChange }>();

  protected readonly today = todayLocal();
  protected readonly isBar = isBar;
  protected readonly lastDayOf = lastDayOf;

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly drag = signal<Drag | null>(null);

  protected readonly weeks = computed<Week[]>(() => {
    const month = parseDay(this.anchor()).getMonth();
    return monthGrid(this.anchor()).map(week => {
      const dates = week.filter(date => this.showWeekends() || !isWeekend(date));
      const placed = layoutBars(dates, this.items());
      const used = Math.max(0, ...placed.map(b => b.lane + 1));
      // As many lanes as there is room for; when one more would show everything, show it.
      const lanes = used <= MAX_LANES + 1 ? used : MAX_LANES;
      const days = dates.map((date, i) => {
        const d = parseDay(date);
        return {
          date,
          label: d.getDate() === 1 ? `${d.getDate()} ${monthName(d.getMonth()).slice(0, 3)}` : `${d.getDate()}`,
          inMonth: d.getMonth() === month,
          isToday: date === this.today,
          more: placed.filter(b => b.lane >= lanes && b.start <= i && i < b.start + b.span).length,
        };
      });
      return { days, bars: placed.filter(b => b.lane < lanes), lanes };
    });
  });

  protected readonly weekdays = computed(() => (this.weeks()[0]?.days ?? []).map(cell => shortWeekday(cell.date)));

  ngOnDestroy(): void {
    this.stopListening();
  }

  protected time(value: string): string {
    return formatTime(value);
  }

  protected hue(item: PlannerItemDto): number | null {
    return item.goalKey ? goalHue(item.goalKey) : null;
  }


  // --- Dragging ------------------------------------------------------------------------

  protected press(event: PointerEvent, item: PlannerItemDto): void {
    // A finger scrolls the page; dragging is for a mouse or a pen.
    if (event.pointerType === 'touch' || event.button !== 0) return;
    const date = this.dayAt(event.clientX, event.clientY);
    if (!date) return;
    this.drag.set({ item, pointerId: event.pointerId, x: event.clientX, y: event.clientY, fromDate: date, date, started: false });
    document.addEventListener('pointermove', this.onMove);
    document.addEventListener('pointerup', this.onUp);
    document.addEventListener('pointercancel', this.onCancel);
  }

  private readonly onMove = (event: PointerEvent): void => {
    const drag = this.drag();
    if (!drag || event.pointerId !== drag.pointerId) return;
    if (!drag.started && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < DRAG_THRESHOLD_PX) return;
    event.preventDefault();
    this.drag.set({ ...drag, started: true, date: this.dayAt(event.clientX, event.clientY) ?? drag.date });
  };

  private readonly onUp = (event: PointerEvent): void => {
    const drag = this.drag();
    if (!drag || event.pointerId !== drag.pointerId) return;
    this.stopListening();
    this.drag.set(null);
    if (!drag.started) return;
    swallowNextClick();

    const shift = dayDiff(drag.fromDate, drag.date);
    if (shift !== 0) this.move.emit({ item: drag.item, change: { date: shiftLocalDate(drag.item.date, shift) } });
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

  /** The day under a point on the screen, if any. */
  private dayAt(clientX: number, clientY: number): string | null {
    for (const cell of this.host.nativeElement.querySelectorAll<HTMLElement>('.cell[data-date]')) {
      const rect = cell.getBoundingClientRect();
      if (clientX >= rect.left && clientX < rect.right && clientY >= rect.top && clientY < rect.bottom) return cell.dataset['date']!;
    }
    return null;
  }

  /** The days a dragged event would cover where it is now. */
  protected isDropTarget(date: string): boolean {
    const drag = this.drag();
    if (!drag?.started) return false;
    const shift = dayDiff(drag.fromDate, drag.date);
    return date >= shiftLocalDate(drag.item.date, shift) && date <= shiftLocalDate(lastDayOf(drag.item), shift);
  }
}
