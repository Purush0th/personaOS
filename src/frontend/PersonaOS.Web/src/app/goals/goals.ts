import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { BoardService, COLUMN_LABELS, apiError, withScopeConfirmation } from '../core/board.service';
import { openTaskFromQuery } from '../board/task-dialog';
import { InlineCreate, NewTask } from '../shared/inline-create';
import { GoalProgress } from './goal-progress';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { Goal, GoalTaskSummary, GoalsService } from '../core/goals.service';
import { GoalPeriod, defaultGoalEnd, formatGoalRange, goalDays, goalPeriodProblem } from '../core/goal-period';
import { todayLocal } from '../core/local-date';

@Component({
  selector: 'app-goals',
  imports: [
    FormsModule,
    RouterLink,
    GoalProgress,
    InlineCreate,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './goals.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './goals.scss',
})
export class Goals implements OnInit {
  private readonly goalsApi = inject(GoalsService);
  private readonly boardApi = inject(BoardService);
  private readonly branding = inject(BrandingService);
  private readonly confirm = inject(Confirm);

  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  protected readonly includeDropped = signal(false);
  protected readonly adding = signal(false);

  /** The goal whose Create task is open. */
  protected readonly addingTaskTo = signal<number | null>(null);

  draftTitle = '';
  draftPeriod: GoalPeriod = 'month';
  draftStart = todayLocal();
  draftEnd = defaultGoalEnd('month', todayLocal());

  protected readonly allowedPoints = [1, 2, 3, 5, 8, 13, 21];
  protected readonly columnLabels = COLUMN_LABELS;
  protected readonly periodLabels: Record<string, string> = { year: 'Year', quarter: 'Quarter', month: 'Month' };
  protected readonly statusLabels: Record<string, string> = { active: 'Active', completed: 'Completed', dropped: 'Dropped' };

  constructor() {
    // A task opened from a goal may have moved or been re-estimated; the goals are read again.
    openTaskFromQuery(() => void this.reload());
  }

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected boardEnabled(): boolean {
    return this.branding.isEnabled('board');
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      this.goals.set(await this.goalsApi.getAll(this.includeDropped()));
    } catch {
      this.confirm.error('Could not load your goals.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async toggleDropped(): Promise<void> {
    this.includeDropped.update(v => !v);
    await this.reload();
  }

  /** A new type or start resets the end to that type's default; a year's end is fixed anyway. */
  protected resetEnd(): void {
    if (this.draftStart) this.draftEnd = defaultGoalEnd(this.draftPeriod, this.draftStart);
  }

  protected draftProblem(): string | null {
    return goalPeriodProblem(this.draftPeriod, this.draftStart, this.draftEnd);
  }

  protected range(goal: Goal): string {
    return formatGoalRange(goal.periodStart, goal.periodEnd);
  }

  /** Still active after its last day. */
  protected overdue(goal: Goal): boolean {
    return goal.status === 'active' && goal.periodEnd < todayLocal();
  }

  protected async add(): Promise<void> {
    const title = this.draftTitle.trim();
    if (!title || this.draftProblem()) return;

    try {
      await this.goalsApi.create({
        title,
        periodType: this.draftPeriod,
        periodStart: this.draftStart,
        periodEnd: this.draftEnd,
      });
      this.adding.set(false);
      this.draftTitle = '';
      this.draftStart = todayLocal();
      this.resetEnd();
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not add that goal.');
    }
  }

  /**
   * Creates a task under the goal whose Create task is open; see InlineCreate. It goes to the
   * backlog, and planning pulls it into a sprint.
   */
  protected readonly createTask = async (task: NewTask): Promise<boolean> => {
    const goalId = this.addingTaskTo();
    if (goalId === null) return false;
    try {
      await withScopeConfirmation(ack => this.boardApi.create({ ...task, goalId }, ack));
      await this.reload();
      return true;
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not add that task.');
      return false;
    }
  };

  protected async setStatus(goal: Goal, status: string): Promise<void> {
    try {
      await this.goalsApi.updateStatus(goal.id, status);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not update that goal.');
    }
  }

  protected async setProgress(goal: Goal, progress: number): Promise<void> {
    try {
      await this.goalsApi.updateProgress(goal.id, progress);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not update progress.');
    }
  }

  protected draftDays(): number {
    return goalDays(this.draftStart, this.draftEnd);
  }

  protected async remove(goal: Goal): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${goal.key} “${goal.title}”?`,
      message: goal.taskCount > 0
        ? `Its ${goal.taskCount} tasks stay on the board without a goal. This cannot be undone.`
        : 'This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;

    try {
      await this.goalsApi.delete(goal.id);
      await this.reload();
    } catch {
      this.confirm.error('Could not delete that goal.');
    }
  }

  /**
   * Tasks are also managed here, not only on the board: work finished outside a sprint — a
   * completed sub-goal converted to a task — never appears on the board, so this was the only
   * place it could be seen and the only place it can be reopened or deleted.
   */
  protected async reopenTask(task: GoalTaskSummary): Promise<void> {
    try {
      await this.boardApi.move(task.key, 'backlog', null, null, true);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not reopen that task.');
    }
  }

  protected async removeTask(task: GoalTaskSummary): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${task.key} “${task.title}”?`,
      message: 'This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    try {
      await this.boardApi.delete(task.key);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not delete that task.');
    }
  }
}
