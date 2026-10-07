import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { goalHue } from '../core/board.service';
import { todayLocal } from '../core/local-date';
import { PlannerItemDto } from '../core/planner.service';
import { formatTime, monthGrid, monthName, parseDay, shortWeekday, isWeekend } from './calendar';

/** How many events a day cell lists before "N more". */
const MAX_IN_CELL = 3;

interface Cell {
  date: string;
  label: string;
  inMonth: boolean;
  isToday: boolean;
  shown: PlannerItemDto[];
  more: number;
}

/**
 * The Month view: whole weeks from the week of the 1st, a timed event as a dot and its start, an
 * all-day one as a coloured bar, "N more" when a day is full. A day's number opens that day.
 */
@Component({
  selector: 'app-month-view',
  template: `
    <div class="month" [style.--cols]="weekdays().length">
      <div class="weekdays">
        @for (name of weekdays(); track name) {
          <span>{{ name }}</span>
        }
      </div>
      @for (week of weeks(); track week[0].date) {
        <div class="week">
          @for (cell of week; track cell.date) {
            <div class="cell" [class.out]="!cell.inMonth" (click)="create.emit(cell.date)">
              <button
                type="button"
                class="day-number"
                [class.today]="cell.isToday"
                [attr.aria-label]="'Open ' + cell.date"
                (click)="$event.stopPropagation(); openDay.emit(cell.date)"
              >
                {{ cell.label }}
              </button>
              @for (item of cell.shown; track item.id + item.occurrenceDate) {
                <button
                  type="button"
                  data-testid="event"
                  [class]="item.scheduledTime ? 'timed' : 'bar'"
                  [class.goal]="hue(item) !== null"
                  [style.--hue]="hue(item)"
                  [class.done]="item.status !== 'planned'"
                  [class.past]="cell.date < today"
                  (click)="$event.stopPropagation(); open.emit(item)"
                >
                  @if (item.scheduledTime) {
                    <span class="dot"></span>
                    <span class="time">{{ time(item.scheduledTime) }}</span>
                  }
                  <span class="title">{{ item.title }}</span>
                </button>
              }
              @if (cell.more > 0) {
                <button type="button" class="more" (click)="$event.stopPropagation(); openDay.emit(cell.date)">{{ cell.more }} more</button>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './month-view.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class MonthView {
  readonly anchor = input.required<string>();
  readonly itemsByDay = input.required<Map<string, PlannerItemDto[]>>();
  readonly showWeekends = input(true);

  readonly open = output<PlannerItemDto>();
  readonly openDay = output<string>();
  /** An empty part of a day was clicked: a new all-day event there. */
  readonly create = output<string>();

  protected readonly today = todayLocal();

  protected readonly weeks = computed<Cell[][]>(() => {
    const month = parseDay(this.anchor()).getMonth();
    return monthGrid(this.anchor()).map(week =>
      week
        .filter(date => this.showWeekends() || !isWeekend(date))
        .map(date => {
          const d = parseDay(date);
          const items = this.itemsByDay().get(date) ?? [];
          // All day first, as Google Calendar lists them; timed ones are already in time order.
          const sorted = [...items.filter(i => !i.scheduledTime), ...items.filter(i => i.scheduledTime)];
          const fits = sorted.length > MAX_IN_CELL + 1 ? MAX_IN_CELL : sorted.length;
          return {
            date,
            label: d.getDate() === 1 ? `${d.getDate()} ${monthName(d.getMonth()).slice(0, 3)}` : `${d.getDate()}`,
            inMonth: d.getMonth() === month,
            isToday: date === this.today,
            shown: sorted.slice(0, fits),
            more: sorted.length - fits,
          };
        })
    );
  });

  protected readonly weekdays = computed(() => (this.weeks()[0] ?? []).map(cell => shortWeekday(cell.date)));

  protected time(value: string): string {
    return formatTime(value);
  }

  protected hue(item: PlannerItemDto): number | null {
    return item.goalKey ? goalHue(item.goalKey) : null;
  }
}
