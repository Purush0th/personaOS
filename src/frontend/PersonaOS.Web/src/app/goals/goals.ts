import { Component, Injector, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { BoardService, COLUMN_LABELS, apiError, withScopeConfirmation } from '../core/board.service';
import { openTaskFromQuery } from '../board/task-dialog';
import { InlineCreate, NewTask } from '../shared/inline-create';
import { GoalProgress } from './goal-progress';
import { askGoalProgress } from './goal-progress-dialog';
import { askNewGoal } from './goal-form-dialog';
import { askMoveGoal } from './goal-move-dialog';
import { askDeleteGoal } from './goal-delete-dialog';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { Goal, GoalTaskSummary, GoalsService, TreeRow, byLane, setsProgressByHand, treeRows, unfolded } from '../core/goals.service';
import { PageTabs } from '../shared/page-tabs';
import { GoalPeriod, PERIOD_LABELS, childTypeOf, formatGoalRange, goalsOfType } from '../core/goal-calendar';
import { todayLocal } from '../core/local-date';

/**
 * Goals as a roadmap: years, their quarters and those quarters' months, each child indented under
 * its parent. A top-level goal and everything under it share a swim lane, as on the Timeline tab,
 * and a goal with child goals folds. Monthly goals carry the tasks. Every action is in the goal's
 * menu; the ones that cannot be taken yet say why instead of failing.
 */
@Component({
  selector: 'app-goals',
  imports: [
    RouterLink,
    PageTabs,
    GoalProgress,
    InlineCreate,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatProgressBarModule,
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
  private readonly dialog = inject(MatDialog);
  private readonly injector = inject(Injector);

  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  /** Goals folded shut: their child goals are hidden. */
  protected readonly collapsed = signal<ReadonlySet<number>>(new Set());
  private readonly rows = computed(() => treeRows(this.goals()));
  protected readonly lanes = computed(() => byLane(unfolded(this.rows(), this.collapsed())));
  protected readonly canFold = computed(() => this.rows().some(r => r.hasChildren));
  protected readonly anyCollapsed = computed(() => this.collapsed().size > 0);

  /** The monthly goal whose Create task is open. */
  protected readonly addingTaskTo = signal<number | null>(null);

  protected readonly allowedPoints = [1, 2, 3, 5, 8, 13, 21];
  protected readonly columnLabels = COLUMN_LABELS;
  protected readonly periodLabels = PERIOD_LABELS;
  protected readonly setsProgressByHand = setsProgressByHand;

  constructor() {
    // A task opened from a goal may have moved or been finished; the goals are read again.
    openTaskFromQuery(() => void this.reload());
  }

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected boardEnabled(): boolean {
    return this.branding.isEnabled('board');
  }

  protected async reload(): Promise<void> {
    try {
      this.goals.set(await this.goalsApi.getAll());
    } catch {
      this.confirm.error('Could not load your goals.');
    } finally {
      this.loading.set(false);
    }
  }

  protected toggle(row: TreeRow): void {
    this.collapsed.update(set => {
      const next = new Set(set);
      if (!next.delete(row.goal.id)) next.add(row.goal.id);
      return next;
    });
  }

  protected toggleAll(): void {
    this.collapsed.set(this.anyCollapsed() ? new Set() : new Set(this.rows().filter(r => r.hasChildren).map(r => r.goal.id)));
  }

  protected range(goal: Goal): string {
    return formatGoalRange(goal.periodStart, goal.periodEnd);
  }

  /** Still active after its last day. */
  protected overdue(goal: Goal): boolean {
    return goal.status === 'active' && goal.periodEnd < todayLocal();
  }

  /** "quarterly goal" or "monthly goal": what can be added under this goal, or null. */
  protected childType(goal: Goal): string | null {
    const type = childTypeOf(goal.periodType);
    return type && goal.status === 'active' && goal.childCount < (type === 'quarter' ? 4 : 3) ? goalsOfType(type) : null;
  }

  /** "quarterly goals" or "monthly goals": what sits under this goal. */
  protected childrenWord(goal: Goal): string {
    return goalsOfType((childTypeOf(goal.periodType) ?? 'month') as GoalPeriod, 2);
  }

  protected async newGoal(parent: Goal | null = null): Promise<void> {
    const created = await askNewGoal(this.dialog, this.injector, { goals: this.goals(), parent });
    if (created) await this.reload();
  }

  protected async move(goal: Goal): Promise<void> {
    if (await askMoveGoal(this.dialog, this.injector, { goal, goals: this.goals() })) await this.reload();
  }

  protected async remove(goal: Goal): Promise<void> {
    if (await askDeleteGoal(this.dialog, this.injector, { goal, goals: this.goals() })) await this.reload();
  }

  protected async updateProgress(goal: Goal): Promise<void> {
    const progress = await askGoalProgress(this.dialog, goal);
    if (progress === null) return;
    await this.change(() => this.goalsApi.updateProgress(goal.id, progress), 'Could not update progress.');
  }

  protected async setStatus(goal: Goal, status: 'active' | 'completed'): Promise<void> {
    await this.change(() => this.goalsApi.updateStatus(goal.id, status), 'Could not update that goal.');
  }

  /**
   * Creates a task under the monthly goal whose Create task is open; see InlineCreate. It goes to
   * the backlog, and planning pulls it into a sprint.
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

  /**
   * Tasks are also managed here, not only on the board: work finished outside a sprint never
   * appears on the board, so this is where it can be reopened or deleted.
   */
  protected async reopenTask(task: GoalTaskSummary): Promise<void> {
    await this.change(() => this.boardApi.move(task.key, 'backlog', null, null, true), 'Could not reopen that task.');
  }

  protected async removeTask(task: GoalTaskSummary): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${task.key} “${task.title}”?`,
      message: 'This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await this.change(() => this.boardApi.delete(task.key), 'Could not delete that task.');
  }

  private async change(action: () => Promise<unknown>, failure: string): Promise<void> {
    try {
      await action();
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? failure);
    }
  }
}
