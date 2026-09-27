import { Component, Injector, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import {
  BoardService,
  COLUMN_LABELS,
  PRIORITY_LABELS,
  Priority,
  apiError,
  formatWhen,
  goalHue,
} from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { PERIOD_LABELS, childTypeOf, formatGoalRange, goalsOfType } from '../core/goal-calendar';
import { Goal, GoalsService, setsProgressByHand } from '../core/goals.service';
import { Confirm } from '../core/confirm';
import { GoalProgress } from './goal-progress';
import { askGoalProgress } from './goal-progress-dialog';
import { askNewGoal } from './goal-form-dialog';
import { askMoveGoal } from './goal-move-dialog';
import { askDeleteGoal } from './goal-delete-dialog';
import { InlineCreate, NewTask } from '../shared/inline-create';
import { Discussion } from '../shared/discussion';

/**
 * One goal, full screen, at /goals/GOAL-3: its child goals (a year's quarters, a quarter's months)
 * or its tasks (a month's), its progress and its discussion. Goals are their own
 * section, beside the board rather than in it, so the page lives under /goals (it was
 * /board/goals, which still redirects here).
 */
@Component({
  selector: 'app-goal-detail',
  imports: [
    FormsModule,
    RouterLink,
    Discussion,
    GoalProgress,
    InlineCreate,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './goal-detail.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['../board/item-page.scss', './goal-detail.scss'],
})
export class GoalDetail implements OnInit {
  private readonly goalsApi = inject(GoalsService);
  private readonly boardApi = inject(BoardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly branding = inject(BrandingService);
  private readonly confirm = inject(Confirm);
  private readonly dialog = inject(MatDialog);
  private readonly injector = inject(Injector);

  protected readonly goal = signal<Goal | null>(null);
  protected readonly loading = signal(true);
  protected readonly editingTitle = signal(false);
  protected readonly editingDescription = signal(false);
  protected readonly addingTask = signal(false);

  titleDraft = '';
  descriptionDraft = '';

  protected readonly key = signal('');
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly columnLabels = COLUMN_LABELS;
  protected readonly allowedPoints = [1, 2, 3, 5, 8, 13, 21];
  protected readonly priorities: Priority[] = ['highest', 'high', 'medium', 'low', 'lowest'];
  protected readonly periodLabels = PERIOD_LABELS;
  protected readonly setsProgressByHand = setsProgressByHand;

  async ngOnInit(): Promise<void> {
    this.key.set(this.route.snapshot.paramMap.get('key') ?? '');
    await this.reload();
  }

  protected assistantName(): string {
    return this.branding.branding()?.assistantNickname ?? 'Assistant';
  }

  protected async reload(): Promise<void> {
    try {
      this.goal.set(await this.goalsApi.getByKey(this.key()));
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load that goal.');
    } finally {
      this.loading.set(false);
    }
  }

  protected startTitle(): void {
    this.titleDraft = this.goal()?.title ?? '';
    this.editingTitle.set(true);
  }

  protected async saveTitle(): Promise<void> {
    const title = this.titleDraft.trim();
    if (!title) return;
    await this.change(() => this.goalsApi.update(this.goal()!.id, { title }));
    this.editingTitle.set(false);
  }

  protected startDescription(): void {
    this.descriptionDraft = this.goal()?.description ?? '';
    this.editingDescription.set(true);
  }

  protected async saveDescription(): Promise<void> {
    const description = this.descriptionDraft.trim();
    await this.change(() =>
      this.goalsApi.update(this.goal()!.id, { description, clearDescription: description.length === 0 })
    );
    this.editingDescription.set(false);
  }

  protected async setPriority(priority: Priority): Promise<void> {
    await this.change(() => this.goalsApi.update(this.goal()!.id, { priority }));
  }

  protected async setStatus(value: 'active' | 'completed'): Promise<void> {
    await this.change(() => this.goalsApi.updateStatus(this.goal()!.id, value));
  }

  protected async updateProgress(): Promise<void> {
    const goal = this.goal()!;
    const progress = await askGoalProgress(this.dialog, goal);
    if (progress !== null) await this.change(() => this.goalsApi.updateProgress(goal.id, progress));
  }

  /** Creates a task under this goal, in the backlog; see InlineCreate, which stays open for the next. */
  protected readonly createTask = async (task: NewTask): Promise<boolean> => {
    try {
      await this.boardApi.create({ ...task, goalId: this.goal()!.id });
      await this.reload();
      return true;
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not add that task.');
      return false;
    }
  };

  /** A year takes up to four quarters and a quarter three months, while it is open. */
  protected canAddChild(goal: Goal): boolean {
    return goal.status === 'active' && goal.childCount < (goal.periodType === 'year' ? 4 : 3);
  }

  /** "quarterly goals" or "monthly goals": what sits under this goal. */
  protected childWord(goal: Goal, count = 2): string {
    return goalsOfType(goal.periodType === 'year' ? 'quarter' : 'month', count);
  }

  protected async addChild(): Promise<void> {
    const goal = this.goal();
    if (!goal || !childTypeOf(goal.periodType)) return;
    const goals = await this.goalsApi.getAll();
    if (await askNewGoal(this.dialog, this.injector, { goals, parent: goals.find(g => g.id === goal.id) ?? goal })) {
      await this.reload();
    }
  }

  protected async move(): Promise<void> {
    const goal = this.goal();
    if (!goal) return;
    if (await askMoveGoal(this.dialog, this.injector, { goal, goals: await this.goalsApi.getAll() })) await this.reload();
  }

  protected async remove(): Promise<void> {
    const goal = this.goal();
    if (!goal) return;
    if (await askDeleteGoal(this.dialog, this.injector, { goal, goals: await this.goalsApi.getAll() })) {
      await this.router.navigate(['/goals']);
    }
  }

  protected hue(): number {
    return goalHue(this.key());
  }

  protected range(goal: Goal): string {
    return formatGoalRange(goal.periodStart, goal.periodEnd);
  }

  protected when(value: string): string {
    return formatWhen(value);
  }

  private async change<T>(action: () => Promise<T>): Promise<void> {
    try {
      await action();
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not save that change.');
    }
  }
}
