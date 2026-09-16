import { Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
import { Goal, GoalsService } from '../core/goals.service';
import { Discussion } from '../shared/discussion';

/** One goal, full screen, at /board/goals/GOAL-3: its tasks, progress and discussion. */
@Component({
  selector: 'app-goal-detail',
  imports: [FormsModule, RouterLink, Discussion],
  templateUrl: './goal-detail.html',
  styleUrl: './goal-detail.scss',
})
export class GoalDetail implements OnInit {
  private readonly goalsApi = inject(GoalsService);
  private readonly boardApi = inject(BoardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly branding = inject(BrandingService);

  @ViewChild(Discussion) private discussion?: Discussion;

  protected readonly goal = signal<Goal | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly editingTitle = signal(false);
  protected readonly editingDescription = signal(false);
  protected readonly addingTask = signal(false);

  titleDraft = '';
  descriptionDraft = '';
  taskTitle = '';
  taskPoints: number | null = null;

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
      this.error.set(null);
      await this.discussion?.load();
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not load that goal.');
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

  protected async setPriority(value: string): Promise<void> {
    await this.change(() => this.goalsApi.update(this.goal()!.id, { priority: value as Priority }));
  }

  protected async setStatus(value: string): Promise<void> {
    await this.change(() => this.goalsApi.updateStatus(this.goal()!.id, value));
  }

  protected async setProgress(value: string): Promise<void> {
    const progress = Number(value);
    if (Number.isNaN(progress)) return;
    await this.change(() => this.goalsApi.updateProgress(this.goal()!.id, progress));
  }

  protected async addTask(): Promise<void> {
    const title = this.taskTitle.trim();
    if (!title) return;
    await this.change(() =>
      this.boardApi.create({ title, points: this.taskPoints, goalId: this.goal()!.id })
    );
    this.taskTitle = '';
    this.taskPoints = null;
    this.addingTask.set(false);
  }

  protected async remove(): Promise<void> {
    const goal = this.goal();
    if (!goal) return;
    const extra = goal.taskCount > 0 ? ` Its ${goal.taskCount} tasks stay on the board without a goal.` : '';
    if (!confirm(`Delete ${goal.key} “${goal.title}”?${extra}`)) return;
    try {
      await this.goalsApi.delete(goal.id);
      await this.router.navigate(['/goals']);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not delete that goal.');
    }
  }

  protected hue(): number {
    return goalHue(this.key());
  }

  protected when(value: string): string {
    return formatWhen(value);
  }

  private async change<T>(action: () => Promise<T>): Promise<void> {
    try {
      await action();
      await this.reload();
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not save that change.');
    }
  }
}
