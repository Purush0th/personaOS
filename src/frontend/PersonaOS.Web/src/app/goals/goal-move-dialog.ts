import { ChangeDetectionStrategy, Component, Injector, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';

import { apiError } from '../core/board.service';
import {
  formatGoalRange, monthDates, monthName, monthsOfQuarter, parentTypeOf, quarterDates, resolveGoalDates, slotLabel,
} from '../core/goal-calendar';
import { Goal, GoalsService } from '../core/goals.service';
import { todayLocal } from '../core/local-date';

export interface GoalMoveData {
  goal: Goal;
  goals: Goal[];
}

/**
 * Move: a quarter into a year, or a month into a quarter, in the slot picked there — its dates
 * change to that slot, and its child months move with it, keeping their place. "–" as the parent
 * detaches it: it becomes standalone and keeps its dates.
 */
@Component({
  selector: 'app-goal-move-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title>Move {{ data.goal.key }}</h2>
    <mat-dialog-content>
      <p class="goal">{{ data.goal.title }} · {{ data.goal.slot }}</p>
      <div class="row">
        <mat-form-field>
          <mat-label>Under</mat-label>
          <!-- 0 stands for "none": a select shows null as empty, and "–" is a real choice. -->
          <mat-select [ngModel]="parentId() ?? 0" (ngModelChange)="setParent($event || null)">
            <mat-option [value]="0">–</mat-option>
            @for (p of parents(); track p.id) {
              <mat-option [value]="p.id">{{ p.key }} {{ p.title }} · {{ p.slot }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        @if (parent()) {
          <mat-form-field>
            <mat-label>{{ data.goal.periodType === 'quarter' ? 'Quarter' : 'Month' }}</mat-label>
            <mat-select [ngModel]="slot()" (ngModelChange)="slot.set($event)">
              @for (option of slots(); track option.value) {
                <mat-option [value]="option.value" [disabled]="!!option.blocked">
                  {{ option.label }}@if (option.blocked) {<span class="blocked"> · {{ option.blocked }}</span>}
                </mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
      </div>
      @if (!parent()) {
        <p class="note">{{ data.goal.parentKey ? 'It becomes standalone and keeps its dates.' : 'Pick where it goes.' }}</p>
      } @else if (resolved()?.problem; as problem) {
        <p class="problem" role="alert">{{ problem }}</p>
      } @else if (resolved()?.dates; as dates) {
        <p class="note">
          {{ range(dates.start, dates.end) }}@if (data.goal.childCount > 0) {; its {{ data.goal.childCount }} months move with it}
        </p>
      }
      @if (error()) {
        <p class="problem" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="!canSave()" (click)="save()">Move</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    // Fields keep a line of space between rows, so a label never sits on the field above.
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem 0.75rem;
      margin: 0.5rem 0 0.75rem;

      mat-form-field {
        flex: 1 1 10rem;
      }
    }

    .goal,
    .note,
    .problem {
      margin: 0 0 0.5rem;
      font: var(--mat-sys-body-medium);
    }

    .note {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .problem {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-error);
    }

    .blocked {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class GoalMoveDialog {
  protected readonly data = inject<GoalMoveData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<GoalMoveDialog, Goal>);
  private readonly goalsApi = inject(GoalsService);
  private readonly today = todayLocal();

  protected readonly parentId = signal<number | null>(this.data.goal.parentId);
  protected readonly slot = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);
  private readonly saving = signal(false);

  protected readonly parents = computed(() => {
    const type = parentTypeOf(this.data.goal.periodType);
    return this.data.goals.filter(g => g.periodType === type && g.status === 'active' && g.id !== this.data.goal.id);
  });

  protected readonly parent = computed(() => this.data.goals.find(g => g.id === this.parentId()) ?? null);

  private year(): number {
    return Number(this.parent()!.periodEnd.slice(0, 4));
  }

  protected readonly slots = computed(() => {
    const parent = this.parent();
    if (!parent) return [];
    const type = this.data.goal.periodType;
    const numbers = type === 'quarter' ? [1, 2, 3, 4] : monthsOfQuarter(parent.periodEnd);
    const taken = new Set(parent.children.filter(c => c.id !== this.data.goal.id).map(c => c.slot));
    return numbers.map(value => {
      const calendar = type === 'quarter' ? quarterDates(this.year(), value) : monthDates(this.year(), value);
      const blocked = taken.has(slotLabel(type, calendar.end)) ? 'taken'
        : calendar.end < this.today ? 'over'
        : resolveGoalDates(type, this.year(), value, null, this.today, parent).problem ? 'too short'
        : null;
      return { value, label: type === 'quarter' ? `Q${value}` : monthName(value), blocked };
    });
  });

  protected readonly resolved = computed(() => {
    const parent = this.parent();
    if (!parent || this.slot() === null) return null;
    return resolveGoalDates(this.data.goal.periodType, this.year(), this.slot(), null, this.today, parent);
  });

  protected readonly canSave = computed(() => {
    if (this.saving()) return false;
    if (!this.parent()) return this.data.goal.parentId !== null; // detaching
    return !!this.resolved()?.dates;
  });

  protected setParent(id: number | null): void {
    this.parentId.set(id);
    this.slot.set(null);
    this.error.set(null);
  }

  protected range(start: string, end: string): string {
    return formatGoalRange(start, end);
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;
    const parent = this.parent();
    const type = this.data.goal.periodType;
    this.saving.set(true);
    this.error.set(null);
    try {
      const moved = await this.goalsApi.move(this.data.goal.id, parent
        ? { parentId: parent.id, year: this.year(), quarter: type === 'quarter' ? this.slot() : null, month: type === 'month' ? this.slot() : null }
        : { parentId: null });
      this.ref.close(moved);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not move that goal.');
    } finally {
      this.saving.set(false);
    }
  }
}

/** Opens Move for the goal; resolves to the moved goal, or null. */
export async function askMoveGoal(dialog: MatDialog, injector: Injector, data: GoalMoveData): Promise<Goal | null> {
  const ref = dialog.open<GoalMoveDialog, GoalMoveData, Goal>(GoalMoveDialog, {
    data, injector, width: '30rem', maxWidth: '94vw',
  });
  return (await firstValueFrom(ref.afterClosed())) ?? null;
}
