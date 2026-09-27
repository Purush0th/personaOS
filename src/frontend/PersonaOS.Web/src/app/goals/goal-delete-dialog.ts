import { ChangeDetectionStrategy, Component, Injector, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';

import { apiError } from '../core/board.service';
import { Goal, GoalTaskAction, GoalsService } from '../core/goals.service';

export interface GoalDeleteData {
  goal: Goal;
  goals: Goal[];
}

/**
 * Delete a goal (one with no child goals; the goals page says to delete those first). When it
 * has tasks the user picks what happens to them: keep them without a goal, delete them too, or
 * reassign them — each task to its own monthly goal, or to none.
 */
@Component({
  selector: 'app-goal-delete-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatRadioModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title>Delete {{ data.goal.key }}?</h2>
    <mat-dialog-content>
      <p class="goal">{{ data.goal.title }} · {{ data.goal.slot }}</p>
      @if (data.goal.tasks.length > 0) {
        <p class="question">Its {{ data.goal.tasks.length }} {{ data.goal.tasks.length === 1 ? 'task' : 'tasks' }}:</p>
        <mat-radio-group class="actions" [ngModel]="action()" (ngModelChange)="action.set($event)" aria-label="What happens to its tasks">
          <mat-radio-button value="keep">Keep without a goal</mat-radio-button>
          <mat-radio-button value="reassign" [disabled]="targets().length === 0">Move to other goals</mat-radio-button>
          <mat-radio-button value="delete">Delete them too</mat-radio-button>
        </mat-radio-group>

        @if (action() === 'reassign') {
          <div class="mapping">
            @for (task of data.goal.tasks; track task.id) {
              <div class="task">
                <span class="key">{{ task.key }}</span>
                <span class="title">{{ task.title }}</span>
                <mat-form-field subscriptSizing="dynamic">
                  <!-- "none", not null: a select shows null as empty, and "No goal" is a real choice. -->
                  <mat-select [ngModel]="mapping()[task.key] ?? 'none'" (ngModelChange)="map(task.key, $event)"
                              [attr.aria-label]="'Goal for ' + task.key">
                    <mat-option value="none">No goal</mat-option>
                    @for (g of targets(); track g.id) {
                      <mat-option [value]="g.key">{{ g.key }} {{ g.title }} · {{ g.slot }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
              </div>
            }
          </div>
        }
      }
      <p class="warning">This cannot be undone.</p>
      @if (error()) {
        <p class="problem" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button class="destructive" [disabled]="saving()" (click)="save()">Delete</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .goal,
    .question,
    .warning,
    .problem {
      margin: 0 0 0.5rem;
      font: var(--mat-sys-body-medium);
    }

    .warning {
      margin-top: 0.75rem;
      color: var(--mat-sys-on-surface-variant);
    }

    .problem {
      color: var(--mat-sys-error);
    }

    .actions {
      display: flex;
      flex-direction: column;
    }

    .mapping {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin-top: 0.5rem;
    }

    .task {
      display: grid;
      grid-template-columns: auto 1fr minmax(10rem, 14rem);
      align-items: center;
      gap: 0.5rem;

      .key {
        font: var(--mat-sys-label-medium);
        color: var(--mat-sys-on-surface-variant);
      }

      .title {
        overflow-wrap: anywhere;
      }
    }

    @media (max-width: 480px) {
      .task {
        grid-template-columns: auto 1fr;

        mat-form-field {
          grid-column: 1 / -1;
        }
      }
    }

    .destructive {
      background: var(--mat-sys-error);
      color: var(--mat-sys-on-error);
    }
  `,
})
export class GoalDeleteDialog {
  protected readonly data = inject<GoalDeleteData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<GoalDeleteDialog, boolean>);
  private readonly goalsApi = inject(GoalsService);

  protected readonly action = signal<GoalTaskAction>('keep');
  protected readonly mapping = signal<Record<string, string | null>>({});
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);

  /** Open monthly goals other than this one: where a task can go. */
  protected readonly targets = computed(() =>
    this.data.goals.filter(g => g.periodType === 'month' && g.status === 'active' && g.id !== this.data.goal.id));

  protected map(taskKey: string, goalKey: string | null): void {
    this.mapping.update(m => ({ ...m, [taskKey]: goalKey === 'none' ? null : goalKey }));
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      const reassign = this.action() === 'reassign'
        ? Object.fromEntries(this.data.goal.tasks.map(t => [t.key, this.mapping()[t.key] ?? null]))
        : null;
      await this.goalsApi.delete(this.data.goal.id, this.action(), reassign);
      this.ref.close(true);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not delete that goal.');
    } finally {
      this.saving.set(false);
    }
  }
}

/** Opens Delete for the goal; resolves true once it is deleted. */
export async function askDeleteGoal(dialog: MatDialog, injector: Injector, data: GoalDeleteData): Promise<boolean> {
  const ref = dialog.open<GoalDeleteDialog, GoalDeleteData, boolean>(GoalDeleteDialog, {
    data, injector, width: '34rem', maxWidth: '94vw', autoFocus: 'dialog',
  });
  return (await firstValueFrom(ref.afterClosed())) === true;
}
