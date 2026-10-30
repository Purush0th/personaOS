import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { apiError } from '../core/board.service';
import { Confirm } from '../core/confirm';
import { PlannerItemDto, PlannerService, PlannerStatus } from '../core/planner.service';
import { formatRange, formatTime, isMultiDay, lastDayOf, notifyLabel, parseDay, weekdayName, monthName } from './calendar';
import { EventDialog, EventDialogData } from './event-dialog';
import { ScopeDialog } from './scope-dialog';

/**
 * The quick look at an event, like Google Calendar's: when it is, how it repeats, the
 * notification, the notes and links, with edit, delete and done at the top.
 * Closes with true when something changed.
 */
@Component({
  selector: 'app-event-peek',
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatTooltipModule, RouterLink],
  template: `
    <div class="actions">
      <button mat-icon-button matTooltip="Edit event" aria-label="Edit event" data-testid="peek-edit" (click)="edit()">
        <mat-icon>pencil-simple</mat-icon>
      </button>
      <button mat-icon-button matTooltip="Delete event" aria-label="Delete event" data-testid="peek-delete" (click)="remove()">
        <mat-icon>trash</mat-icon>
      </button>
      <button mat-icon-button matTooltip="Close" aria-label="Close" [mat-dialog-close]="changed()">
        <mat-icon fontIcon="ph-x"></mat-icon>
      </button>
    </div>

    <mat-dialog-content>
      <div class="head">
        <span class="swatch" [class.done]="item.status === 'done'"></span>
        <div>
          <h2 class="title" [class.struck]="item.status !== 'planned'" data-testid="peek-title">{{ item.title }}</h2>
          <p class="when">{{ when }}</p>
          @if (item.recurrenceText) {
            <p class="muted">{{ item.recurrenceText }}</p>
          }
        </div>
      </div>

      <p class="line"><mat-icon>bell</mat-icon>{{ notify }}</p>

      @if (item.notes) {
        <p class="line notes"><mat-icon>note</mat-icon><span>{{ item.notes }}</span></p>
      }
      @if (item.goalTitle || item.taskKey) {
        <p class="line">
          <mat-icon>flag</mat-icon>
          @if (item.taskKey) {
            <a [routerLink]="['/board/tasks', item.taskKey]" [mat-dialog-close]="changed()">{{ item.taskKey }}</a>&nbsp;
          }
          @if (item.goalTitle) {
            <span>{{ item.goalKey }} {{ item.goalTitle }}</span>
          }
        </p>
      }
      @if (item.status !== 'planned') {
        <p class="line muted"><mat-icon>{{ item.status === 'done' ? 'check-circle' : 'minus-circle' }}</mat-icon>{{ item.status === 'done' ? 'Done' : 'Skipped' }}</p>
      }
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      @if (item.status === 'planned') {
        <button mat-button data-testid="peek-skip" (click)="setStatus('skipped')">Skip</button>
        <button mat-flat-button data-testid="peek-done" (click)="setStatus('done')">Mark done</button>
      } @else {
        <button mat-button data-testid="peek-undo" (click)="setStatus('planned')">Mark not done</button>
      }
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .actions {
      display: flex;
      justify-content: flex-end;
      padding: 0.5rem 0.5rem 0;
    }
    .head {
      display: flex;
      gap: 0.9rem;
      align-items: flex-start;
    }
    .swatch {
      flex: none;
      width: 1rem;
      height: 1rem;
      margin-top: 0.4rem;
      border-radius: 4px;
      background: var(--event-bg, var(--mat-sys-primary));
    }
    .title {
      font: var(--mat-sys-title-large);
      margin: 0;
      overflow-wrap: anywhere;
    }
    .struck {
      text-decoration: line-through;
      color: var(--mat-sys-on-surface-variant);
    }
    .when {
      margin: 0.25rem 0 0;
    }
    .head p {
      margin: 0.15rem 0 0;
    }
    .line {
      display: flex;
      align-items: flex-start;
      gap: 0.9rem;
      margin: 0.9rem 0 0;

      mat-icon {
        flex: none;
        color: var(--mat-sys-on-surface-variant);
      }
    }
    .notes span {
      white-space: pre-wrap;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class EventPeek {
  protected readonly item = inject<PlannerItemDto>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<EventPeek, boolean>);
  private readonly dialog = inject(MatDialog);
  private readonly planner = inject(PlannerService);
  private readonly confirm = inject(Confirm);

  protected readonly changed = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly when = describeWhen(this.item);
  protected readonly notify = notifyLabel(this.item.notifyBeforeMinutes);

  protected async edit(): Promise<void> {
    const saved = await firstValueFrom(
      this.dialog
        .open<EventDialog, EventDialogData, boolean>(EventDialog, { data: { item: this.item }, width: '36rem', maxWidth: '95vw' })
        .afterClosed()
    );
    if (saved) this.ref.close(true);
  }

  protected async remove(): Promise<void> {
    try {
      if (this.item.recurrence) {
        const scope = await ScopeDialog.ask(this.dialog, { action: 'delete' });
        if (!scope) return;
        await this.planner.delete(this.item.id, { scope, occurrence: this.item.occurrenceDate });
      } else {
        const ok = await this.confirm.ask({ title: 'Delete event?', message: `“${this.item.title}” will be deleted.`, confirmLabel: 'Delete', destructive: true });
        if (!ok) return;
        await this.planner.delete(this.item.id);
      }
      this.ref.close(true);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not delete the event.');
    }
  }

  protected async setStatus(status: PlannerStatus): Promise<void> {
    try {
      await this.planner.updateStatus(this.item.id, status, this.item.recurrence ? this.item.occurrenceDate : undefined);
      this.ref.close(true);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not update the event.');
    }
  }
}

/**
 * "Saturday, 3 October · 8:30 – 9:30am", "Saturday, 3 October · All day"; over several days
 * "Friday, 9 October – Sunday, 11 October", or with times "Friday, 9 October, 6pm – Saturday, 10 October, 9am".
 */
export function describeWhen(item: PlannerItemDto): string {
  const dayName = (day: string) => {
    const d = parseDay(day);
    return `${weekdayName(day)}, ${d.getDate()} ${monthName(d.getMonth())}`;
  };
  if (isMultiDay(item)) {
    const last = lastDayOf(item);
    return item.scheduledTime && item.endTime
      ? `${dayName(item.date)}, ${formatTime(item.scheduledTime)} – ${dayName(last)}, ${formatTime(item.endTime)}`
      : `${dayName(item.date)} – ${dayName(last)}`;
  }
  const time = item.scheduledTime && item.endTime ? formatRange(item.scheduledTime, item.endTime) : 'All day';
  return `${dayName(item.date)} · ${time}`;
}
