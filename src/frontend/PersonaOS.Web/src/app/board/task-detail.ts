import { Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import {
  BoardColumn,
  BoardService,
  BoardTask,
  COLUMN_LABELS,
  PRIORITY_LABELS,
  PlanView,
  Priority,
  apiError,
  formatWhen,
  goalHue,
  withScopeConfirmation,
} from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Goal, GoalsService } from '../core/goals.service';
import { Discussion } from '../shared/discussion';

/** One task, full screen, at /board/tasks/TASK-7 — the link you can paste anywhere. */
@Component({
  selector: 'app-task-detail',
  imports: [FormsModule, RouterLink, Discussion],
  templateUrl: './task-detail.html',
  styleUrl: './task-detail.scss',
})
export class TaskDetail implements OnInit {
  private readonly api = inject(BoardService);
  private readonly goalsApi = inject(GoalsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly branding = inject(BrandingService);

  @ViewChild(Discussion) private discussion?: Discussion;

  protected readonly task = signal<BoardTask | null>(null);
  protected readonly plan = signal<PlanView | null>(null);
  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly editingTitle = signal(false);
  protected readonly editingDescription = signal(false);

  titleDraft = '';
  descriptionDraft = '';

  protected readonly key = signal('');
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly columnLabels = COLUMN_LABELS;

  async ngOnInit(): Promise<void> {
    this.key.set(this.route.snapshot.paramMap.get('key') ?? '');
    await Promise.all([this.reload(), this.loadContext()]);
  }

  protected assistantName(): string {
    return this.branding.branding()?.assistantNickname ?? 'Assistant';
  }

  protected async reload(): Promise<void> {
    try {
      const detail = await this.api.task(this.key());
      this.task.set(detail.task);
      this.error.set(null);
      await this.discussion?.load();
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not load that task.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadContext(): Promise<void> {
    try {
      this.plan.set(await this.api.plan());
    } catch {
      this.plan.set(null);
    }
    try {
      this.goals.set((await this.goalsApi.getAll()).filter(g => g.status === 'active'));
    } catch {
      this.goals.set([]);
    }
  }

  protected startTitle(): void {
    this.titleDraft = this.task()?.title ?? '';
    this.editingTitle.set(true);
  }

  protected async saveTitle(): Promise<void> {
    const title = this.titleDraft.trim();
    if (!title) return;
    await this.change(() => this.api.update(this.key(), { title }));
    this.editingTitle.set(false);
  }

  protected startDescription(): void {
    this.descriptionDraft = this.task()?.description ?? '';
    this.editingDescription.set(true);
  }

  protected async saveDescription(): Promise<void> {
    const description = this.descriptionDraft.trim();
    await this.change(() =>
      this.api.update(this.key(), { description, clearDescription: description.length === 0 })
    );
    this.editingDescription.set(false);
  }

  protected async setPoints(value: string): Promise<void> {
    const points = value === '' ? null : Number(value);
    await this.change(() => this.api.update(this.key(), { points, clearPoints: points === null }));
  }

  protected async setPriority(value: string): Promise<void> {
    await this.change(() => this.api.update(this.key(), { priority: value as Priority }));
  }

  protected async setGoal(value: string): Promise<void> {
    const goalId = value === '' ? null : Number(value);
    await this.change(() => this.api.update(this.key(), { goalId, clearGoal: goalId === null }));
  }

  /** Moving between sprints, or out to the backlog, from the task itself. */
  protected async setSprint(value: string): Promise<void> {
    const task = this.task();
    if (!task) return;
    const column: BoardColumn = value === 'backlog' ? 'backlog' : task.column === 'backlog' ? 'todo' : task.column;
    await this.change(() =>
      withScopeConfirmation(ack =>
        this.api.move(this.key(), column, value === 'backlog' ? null : value, null, ack)
      )
    );
  }

  protected async setColumn(value: string): Promise<void> {
    const task = this.task();
    if (!task) return;
    await this.change(() =>
      withScopeConfirmation(ack =>
        this.api.move(this.key(), value as BoardColumn, value === 'backlog' ? null : task.sprintKey, null, ack)
      )
    );
  }

  protected async remove(): Promise<void> {
    const task = this.task();
    if (!task || !confirm(`Delete ${task.key} “${task.title}”? Its number will be reused.`)) return;
    try {
      await this.api.delete(task.key);
      await this.router.navigate(['/backlog']);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not delete that task.');
    }
  }

  protected sprintOptions(): { key: string; label: string }[] {
    return (this.plan()?.sprints ?? []).map(s => ({
      key: s.sprint.key,
      label: s.sprint.name ? `${s.sprint.key} · ${s.sprint.name}` : s.sprint.key,
    }));
  }

  protected columnOptions(): BoardColumn[] {
    const task = this.task();
    return task?.sprintKey ? ['todo', 'in_progress', 'done'] : ['backlog'];
  }

  protected hue(goalKey: string | null): number {
    return goalHue(goalKey);
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
