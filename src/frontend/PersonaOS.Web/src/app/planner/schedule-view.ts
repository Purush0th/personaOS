import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { goalHue } from '../core/board.service';
import { todayLocal } from '../core/local-date';
import { PlannerItemDto } from '../core/planner.service';
import { formatRange, monthName, parseDay, shortWeekday } from './calendar';

/**
 * The Schedule view: the days ahead that have events, as a list, the way Google Calendar's
 * Schedule reads. Empty days are left out.
 */
@Component({
  selector: 'app-schedule-view',
  template: `
    <div class="schedule">
      @for (day of days(); track day.date) {
        <div class="day" [class.today]="day.date === today">
          <button type="button" class="date" (click)="openDay.emit(day.date)" [attr.aria-label]="'Open ' + day.date">
            <span class="number">{{ day.number }}</span>
            <span class="label">{{ day.label }}</span>
          </button>
          <div class="items">
            @for (item of day.items; track item.id + item.occurrenceDate) {
              <button
                type="button"
                class="item"
                data-testid="event"
                [class.goal]="item.goalKey"
                [style.--hue]="hue(item)"
                [class.done]="item.status !== 'planned'"
                (click)="open.emit(item)"
              >
                <span class="dot"></span>
                <span class="time">{{ time(item) }}</span>
                <span class="title">{{ item.title }}</span>
                @if (item.recurrenceText) {
                  <span class="repeat muted" aria-label="Repeats">↻</span>
                }
              </button>
            }
          </div>
        </div>
      } @empty {
        <p class="muted empty">Nothing planned for these {{ span() }} days.</p>
      }
    </div>
  `,
  styles: `
    .schedule {
      display: flex;
      flex-direction: column;
    }
    .day {
      display: grid;
      grid-template-columns: 7.5rem 1fr;
      gap: 1rem;
      padding: 0.6rem 0.25rem;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .date {
      display: flex;
      align-items: baseline;
      gap: 0.6rem;
      align-self: start;
      border: none;
      background: none;
      color: var(--mat-sys-on-surface);
      cursor: pointer;
      text-align: left;

      .number {
        display: grid;
        place-items: center;
        min-width: 2rem;
        height: 2rem;
        border-radius: 50%;
        font: var(--mat-sys-title-medium);
      }
      .label {
        font: var(--mat-sys-label-small);
        letter-spacing: 0.04em;
        color: var(--mat-sys-on-surface-variant);
      }
    }
    .today .number {
      background: var(--mat-sys-primary);
      color: var(--mat-sys-on-primary);
    }
    .items {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .item {
      --event-bg: var(--mat-sys-primary);
      display: grid;
      grid-template-columns: auto 9rem 1fr auto;
      align-items: center;
      gap: 0.75rem;
      padding: 0.35rem 0.5rem;
      border: none;
      border-radius: var(--mat-sys-corner-small);
      background: none;
      color: var(--mat-sys-on-surface);
      font: var(--mat-sys-body-medium);
      text-align: left;
      cursor: pointer;

      &:hover {
        background: var(--mat-sys-surface-container-highest);
      }
      &.goal {
        --event-bg: hsl(var(--hue) 60% 50%);
      }
      &.done .title {
        text-decoration: line-through;
        color: var(--mat-sys-on-surface-variant);
      }
      .dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        background: var(--event-bg);
      }
      .time {
        color: var(--mat-sys-on-surface-variant);
      }
      .title {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
    }
    .empty {
      padding: 2rem 0.5rem;
    }
    @media (max-width: 600px) {
      .day {
        grid-template-columns: 4.5rem 1fr;
      }
      .item {
        grid-template-columns: auto 1fr auto;
        .time {
          display: none;
        }
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ScheduleView {
  readonly dates = input.required<string[]>();
  readonly itemsByDay = input.required<Map<string, PlannerItemDto[]>>();

  readonly open = output<PlannerItemDto>();
  readonly openDay = output<string>();

  protected readonly today = todayLocal();
  protected readonly span = computed(() => this.dates().length);

  protected readonly days = computed(() =>
    this.dates()
      .filter(date => (this.itemsByDay().get(date)?.length ?? 0) > 0)
      .map(date => {
        const d = parseDay(date);
        return {
          date,
          number: d.getDate(),
          label: `${monthName(d.getMonth()).slice(0, 3).toUpperCase()}, ${shortWeekday(date)}`,
          items: this.itemsByDay().get(date)!,
        };
      })
  );

  protected time(item: PlannerItemDto): string {
    return item.scheduledTime && item.endTime ? formatRange(item.scheduledTime, item.endTime) : 'All day';
  }

  protected hue(item: PlannerItemDto): number | null {
    return item.goalKey ? goalHue(item.goalKey) : null;
  }
}
