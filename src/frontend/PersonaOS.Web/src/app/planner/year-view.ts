import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { todayLocal } from '../core/local-date';
import { PlannerItemDto } from '../core/planner.service';
import { monthGrid, monthName, parseDay } from './calendar';

interface MiniMonth {
  name: string;
  weeks: { date: string; day: number; inMonth: boolean; busy: number }[][];
}

/**
 * The Year view: twelve small months, as Google Calendar shows them. A day with events is marked
 * (stronger the more it holds); clicking a day opens it.
 */
@Component({
  selector: 'app-year-view',
  template: `
    <div class="year">
      @for (month of months(); track month.name) {
        <section class="mini">
          <h3>{{ month.name }}</h3>
          <div class="row head">
            @for (d of letters; track $index) {
              <span>{{ d }}</span>
            }
          </div>
          @for (week of month.weeks; track week[0].date) {
            <div class="row">
              @for (cell of week; track cell.date) {
                <button
                  type="button"
                  class="day"
                  [class.out]="!cell.inMonth"
                  [class.today]="cell.date === today && cell.inMonth"
                  [class.busy]="cell.busy > 0 && cell.inMonth"
                  [attr.aria-label]="cell.date + (cell.busy ? ', ' + cell.busy + ' events' : '')"
                  (click)="openDay.emit(cell.date)"
                >
                  {{ cell.day }}
                </button>
              }
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .year {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr));
      gap: 1.5rem 2rem;
      padding: 0.5rem 0.25rem 1rem;
    }
    h3 {
      font: var(--mat-sys-title-small);
      margin: 0 0 0.4rem 0.4rem;
    }
    .row {
      display: grid;
      grid-template-columns: repeat(7, 1fr);
    }
    .head span {
      text-align: center;
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-on-surface-variant);
      padding-bottom: 0.25rem;
    }
    .day {
      justify-self: center;
      width: 1.9rem;
      height: 1.9rem;
      border: none;
      border-radius: 50%;
      background: none;
      color: var(--mat-sys-on-surface);
      font: var(--mat-sys-label-small);
      cursor: pointer;
      position: relative;

      &:hover {
        background: var(--mat-sys-surface-container-highest);
      }
      &.out {
        color: var(--mat-sys-outline);
      }

      /* A day with events gets a small dot, not a fill: with a daily repeat a fill would colour
         almost every day of the year. */
      &.busy::after {
        content: '';
        position: absolute;
        left: 50%;
        bottom: 3px;
        width: 4px;
        height: 4px;
        margin-left: -2px;
        border-radius: 50%;
        background: var(--mat-sys-primary);
      }
      &.today {
        background: var(--mat-sys-primary);
        color: var(--mat-sys-on-primary);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class YearView {
  readonly anchor = input.required<string>();
  readonly itemsByDay = input.required<Map<string, PlannerItemDto[]>>();

  readonly openDay = output<string>();

  protected readonly today = todayLocal();
  protected readonly letters = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];

  protected readonly months = computed<MiniMonth[]>(() => {
    const year = parseDay(this.anchor()).getFullYear();
    return Array.from({ length: 12 }, (_, month) => {
      const first = `${year}-${String(month + 1).padStart(2, '0')}-01`;
      return {
        name: monthName(month),
        weeks: monthGrid(first).map(week =>
          week.map(date => {
            const d = parseDay(date);
            return { date, day: d.getDate(), inMonth: d.getMonth() === month, busy: this.itemsByDay().get(date)?.length ?? 0 };
          })
        ),
      };
    });
  });
}
