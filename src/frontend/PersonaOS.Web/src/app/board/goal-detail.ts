import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
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
import { formatGoalRange } from '../core/goal-period';
import { Goal, GoalsService } from '../core/goals.service';
import { Confirm } from '../core/confirm';
import { GoalProgress } from '../goals/goal-progress';
import { InlineCreate, NewTask } from '../shared/inline-create';
import { Discussion } from '../shared/discussion';

/** One goal, full screen, at /board/goals/GOAL-3: its tasks, progress and discussion. */
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
  styleUrls: ['./item-page.scss', './goal-detail.scss'],
})
export class GoalDetail implements OnInit {
  private readonly goalsApi = inject(GoalsService);
  private readonly boardApi = inject(BoardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly branding = inject(BrandingService);
  private readonly confirm = inject(Confirm);

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

  protected async setStatus(value: string): Promise<void> {
    await this.change(() => this.goalsApi.updateStatus(this.goal()!.id, value));
  }

  protected async setProgress(progress: number): Promise<void> {
    await this.change(() => this.goalsApi.updateProgress(this.goal()!.id, progress));
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

  protected async remove(): Promise<void> {
    const goal = this.goal();
    if (!goal) return;
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
      await this.router.navigate(['/goals']);
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not delete that goal.');
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
