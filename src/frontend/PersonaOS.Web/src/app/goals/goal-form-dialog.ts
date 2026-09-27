import { ChangeDetectionStrategy, Component, Injector, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';

import { apiError } from '../core/board.service';
import {
  GoalPeriod, PERIOD_LABELS, formatGoalRange, goalDays, monthDates, monthName, monthsOfQuarter, parentTypeOf, quarterDates,
  resolveGoalDates, slotLabel,
} from '../core/goal-calendar';
import { Goal, GoalsService } from '../core/goals.service';
import { todayLocal } from '../core/local-date';

export interface GoalFormData {
  /** Every goal, for the "Under" choices and the slots already taken. */
  goals: Goal[];
  /** Adding under this goal: the type and parent are fixed. */
  parent?: Goal | null;
}

/** One option of a slot select; a slot that cannot be used says why instead of being picked. */
interface SlotOption {
  value: number;
  label: string;
  blocked: string | null;
}

/**
 * New goal: pick the type and its calendar slot and the dates follow (see goal-calendar.ts). A
 * year picks its start day; a quarter or month picks Q1–Q4 or the month. "Under" nests it in a
 * year or quarter; opened from a parent's "Add quarter / Add month", that part is fixed.
 * Slots that are over, too short or already taken are shown but cannot be picked.
 */
@Component({
  selector: 'app-goal-form-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title>{{ data.parent ? 'Add ' + type().toLowerCase() + ' goal' : 'New goal' }}</h2>
    <mat-dialog-content>
      @if (data.parent; as parent) {
        <p class="under">Under <b>{{ parent.key }}</b> {{ parent.title }} · {{ parent.slot }}</p>
      }
      <form id="goal-form" (ngSubmit)="save()">
        <mat-form-field class="full">
          <mat-label>Title</mat-label>
          <input matInput name="title" [ngModel]="title()" (ngModelChange)="title.set($event)" required cdkFocusInitial />
        </mat-form-field>

        <div class="row">
          @if (!data.parent) {
            <mat-form-field>
              <mat-label>Type</mat-label>
              <mat-select name="type" [ngModel]="typeValue()" (ngModelChange)="setType($event)">
                @for (t of types; track t) {
                  <mat-option [value]="t">{{ periodLabels[t] }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }

          @if (!data.parent) {
            <mat-form-field>
              <mat-label>Year</mat-label>
              <mat-select name="year" [ngModel]="year()" (ngModelChange)="setYear($event)">
                @for (y of years; track y) {
                  <mat-option [value]="y">{{ y }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }

          @if (typeValue() === 'year') {
            <mat-form-field>
              <mat-label>Starts</mat-label>
              <input matInput type="date" name="start" [min]="today" [ngModel]="start()" (ngModelChange)="start.set($event)" />
            </mat-form-field>
          } @else {
            <mat-form-field>
              <mat-label>{{ typeValue() === 'quarter' ? 'Quarter' : 'Month' }}</mat-label>
              <mat-select name="slot" [ngModel]="slot()" (ngModelChange)="slot.set($event)">
                @for (option of slots(); track option.value) {
                  <mat-option [value]="option.value" [disabled]="!!option.blocked">
                    {{ option.label }}@if (option.blocked) {<span class="blocked"> · {{ option.blocked }}</span>}
                  </mat-option>
                }
              </mat-select>
            </mat-form-field>
          }

          @if (!data.parent && parentType()) {
            <mat-form-field>
              <mat-label>Under</mat-label>
              <!-- 0 stands for "none": a select shows null as empty, and "–" is a real choice. -->
              <mat-select name="parent" [ngModel]="parentId() ?? 0" (ngModelChange)="parentId.set($event || null)">
                <mat-option [value]="0">–</mat-option>
                @for (p of parents(); track p.id) {
                  <mat-option [value]="p.id">{{ p.key }} {{ p.title }} · {{ p.slot }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
        </div>
      </form>

      <!-- Nothing picked yet is not a problem to show in red; the empty field says it. -->
      @if (shownProblem(); as problem) {
        <p class="problem" role="alert">{{ problem }}</p>
      } @else if (resolved().dates; as dates) {
        <p class="dates">{{ range(dates.start, dates.end) }} · {{ days(dates.start, dates.end) }} days</p>
      }
      @if (error()) {
        <p class="problem" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button type="submit" form="goal-form" [disabled]="!canSave()">Add goal</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    // Room above the first outlined field, whose label sits on its top border.
    form {
      padding-top: 0.5rem;
    }

    // Fields keep a line of space between rows, so a label never sits on the field above.
    .full {
      width: 100%;
      margin-bottom: 1rem;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem 0.75rem;
      margin-bottom: 0.75rem;

      mat-form-field {
        flex: 1 1 9rem;
      }
    }

    .under {
      margin: 0 0 0.75rem;
      font: var(--mat-sys-body-medium);
    }

    .dates,
    .problem {
      margin: 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .problem {
      color: var(--mat-sys-error);
    }

    .blocked {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class GoalFormDialog {
  protected readonly data = inject<GoalFormData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<GoalFormDialog, Goal>);
  private readonly goalsApi = inject(GoalsService);

  protected readonly today = todayLocal();
  private readonly thisYear = Number(this.today.slice(0, 4));
  protected readonly years = Array.from({ length: 6 }, (_, i) => this.thisYear + i);
  protected readonly types: GoalPeriod[] = ['month', 'quarter', 'year'];
  protected readonly periodLabels = PERIOD_LABELS;

  protected readonly title = signal('');
  private readonly typeChoice = signal<GoalPeriod>('month');
  protected readonly year = signal(this.data.parent ? Number(this.data.parent.periodEnd.slice(0, 4)) : this.thisYear);
  protected readonly start = signal(this.today);
  protected readonly slot = signal<number | null>(null);
  protected readonly parentId = signal<number | null>(this.data.parent?.id ?? null);
  protected readonly error = signal<string | null>(null);
  private readonly saving = signal(false);

  /** Under a parent the type is the one that nests there: a quarter in a year, a month in a quarter. */
  protected readonly typeValue = computed<GoalPeriod>(() =>
    this.data.parent ? (this.data.parent.periodType === 'year' ? 'quarter' : 'month') : this.typeChoice());

  protected readonly type = computed(() => PERIOD_LABELS[this.typeValue()]);
  protected readonly parentType = computed(() => parentTypeOf(this.typeValue()));

  private readonly parent = computed(() =>
    this.data.parent ?? this.data.goals.find(g => g.id === this.parentId()) ?? null);

  /** Open goals of the right type in the chosen year. */
  protected readonly parents = computed(() =>
    this.data.goals.filter(g => g.periodType === this.parentType() && g.status === 'active'
      && Number(g.periodEnd.slice(0, 4)) === this.year()));

  protected readonly slots = computed<SlotOption[]>(() => {
    const type = this.typeValue();
    const parent = this.parent();
    const numbers = type === 'quarter'
      ? [1, 2, 3, 4]
      : parent ? monthsOfQuarter(parent.periodEnd) : Array.from({ length: 12 }, (_, i) => i + 1);
    const taken = new Set(parent?.children.map(c => c.slot) ?? []);
    return numbers.map(value => {
      const calendar = type === 'quarter' ? quarterDates(this.year(), value) : monthDates(this.year(), value);
      const blocked = taken.has(slotLabel(type, calendar.end)) ? 'taken'
        : calendar.end < this.today ? 'over'
        : resolveGoalDates(type, this.year(), value, null, this.today, parent).problem ? 'too short'
        : null;
      return { value, label: type === 'quarter' ? `Q${value}` : monthName(value), blocked };
    });
  });

  protected readonly resolved = computed(() =>
    resolveGoalDates(this.typeValue(), this.year(), this.typeValue() === 'year' ? null : this.slot(),
      this.typeValue() === 'year' ? this.start() : null, this.today, this.parent()));

  protected readonly shownProblem = computed(() => {
    const problem = this.resolved().problem;
    return problem?.startsWith('Pick a') ? null : problem;
  });

  protected readonly canSave = computed(() =>
    !!this.title().trim() && !!this.resolved().dates && !this.saving());

  protected setType(type: GoalPeriod): void {
    this.typeChoice.set(type);
    this.slot.set(null);
    this.parentId.set(null);
  }

  protected setYear(year: number): void {
    this.year.set(year);
    this.slot.set(null);
    this.parentId.set(null);
    if (this.start().slice(0, 4) !== `${year}`) this.start.set(year === this.thisYear ? this.today : `${year}-01-01`);
  }

  protected range(start: string, end: string): string {
    return formatGoalRange(start, end);
  }

  protected days(start: string, end: string): number {
    return goalDays(start, end);
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;
    const type = this.typeValue();
    this.saving.set(true);
    this.error.set(null);
    try {
      const goal = await this.goalsApi.create({
        title: this.title().trim(),
        periodType: type,
        year: this.year(),
        quarter: type === 'quarter' ? this.slot() : null,
        month: type === 'month' ? this.slot() : null,
        periodStart: type === 'year' ? this.start() : null,
        parentId: this.parent()?.id ?? null,
      });
      this.ref.close(goal);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not add that goal.');
    } finally {
      this.saving.set(false);
    }
  }
}

/** Opens New goal (or Add quarter / Add month under `parent`); resolves to the created goal, or null. */
export async function askNewGoal(dialog: MatDialog, injector: Injector, data: GoalFormData): Promise<Goal | null> {
  const ref = dialog.open<GoalFormDialog, GoalFormData, Goal>(GoalFormDialog, {
    data, injector, width: '34rem', maxWidth: '94vw', autoFocus: 'first-tabbable',
  });
  return (await firstValueFrom(ref.afterClosed())) ?? null;
}
