import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { BoardColumn, BoardService, apiError } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { Goal, GoalTaskSummary, GoalsService } from '../core/goals.service';
import { GoalPeriod, defaultGoalEnd, formatGoalRange, goalDays, goalPeriodProblem } from '../core/goal-period';
import { todayLocal } from '../core/local-date';

const COLUMN_LABELS: Record<BoardColumn, string> = {
  backlog: 'Backlog',
  todo: 'This week',
  in_progress: 'In progress',
  done: 'Done',
};

@Component({
  selector: 'app-goals',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
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

  /** Goal whose inline "add task" form is open. */
  protected readonly addingTaskTo = signal<number | null>(null);

  draftTitle = '';
  draftPeriod: GoalPeriod = 'month';
  draftStart = todayLocal();
  draftEnd = defaultGoalEnd('month', todayLocal());
  taskTitle = '';
  taskPoints: number | null = null;

  protected readonly allowedPoints = [1, 2, 3, 5, 8, 13, 21];

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

  protected startAddTask(goal: Goal): void {
    this.addingTaskTo.set(goal.id);
    this.taskTitle = '';
    this.taskPoints = null;
  }

  /** Tasks added from a goal go to the Backlog; planning pulls them into a sprint. */
  protected async addTask(goal: Goal): Promise<void> {
    const title = this.taskTitle.trim();
    if (!title) return;
    try {
      await this.boardApi.create({ title, points: this.taskPoints, goalId: goal.id });
      this.addingTaskTo.set(null);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not add that task.');
    }
  }

  protected async setStatus(goal: Goal, status: string): Promise<void> {
    try {
      await this.goalsApi.updateStatus(goal.id, status);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not update that goal.');
    }
  }

  protected async setProgress(goal: Goal, value: string): Promise<void> {
    const progress = Number(value);
    if (Number.isNaN(progress)) return;
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

  protected columnLabel(column: BoardColumn): string {
    return COLUMN_LABELS[column];
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
      message: 'Its number will be reused by the next task.',
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
