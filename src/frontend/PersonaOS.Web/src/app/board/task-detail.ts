import { Component, OnInit, inject, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
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
import { Confirm } from '../core/confirm';
import { Discussion } from '../shared/discussion';

/**
 * One task: full screen at /board/tasks/TASK-7, the link you can paste anywhere, or embedded in
 * the dialog the board and backlog open over themselves (see task-dialog.ts).
 */
@Component({
  selector: 'app-task-detail',
  imports: [
    FormsModule,
    RouterLink,
    Discussion,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './task-detail.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './item-page.scss',
  host: { '[class.embedded]': 'embedded()' },
})
export class TaskDetail implements OnInit {
  private readonly api = inject(BoardService);
  private readonly goalsApi = inject(GoalsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly branding = inject(BrandingService);
  private readonly confirm = inject(Confirm);

  /** The task to show when embedded; the full page reads it from the route instead. */
  readonly taskKey = input<string>();
  /** Embedded in a dialog: no crumbs, and a delete is reported instead of navigating away. */
  readonly embedded = input(false);
  readonly deleted = output<void>();

  protected readonly task = signal<BoardTask | null>(null);
  protected readonly plan = signal<PlanView | null>(null);
  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  protected readonly editingTitle = signal(false);
  protected readonly editingDescription = signal(false);

  titleDraft = '';
  descriptionDraft = '';

  protected readonly key = signal('');
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly columnLabels = COLUMN_LABELS;

  async ngOnInit(): Promise<void> {
    this.key.set(this.taskKey() ?? this.route.snapshot.paramMap.get('key') ?? '');
    await Promise.all([this.reload(), this.loadContext()]);
  }

  protected assistantName(): string {
    return this.branding.branding()?.assistantNickname ?? 'Assistant';
  }

  protected async reload(): Promise<void> {
    try {
      const detail = await this.api.task(this.key());
      this.task.set(detail.task);
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load that task.');
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

  protected async setPoints(points: number | null): Promise<void> {
    await this.change(() => this.api.update(this.key(), { points, clearPoints: points === null }));
  }

  protected async setPriority(priority: Priority): Promise<void> {
    await this.change(() => this.api.update(this.key(), { priority }));
  }

  protected async setGoal(goalId: number | null): Promise<void> {
    await this.change(() => this.api.update(this.key(), { goalId, clearGoal: goalId === null }));
  }

  /** Moving between sprints, or out to the backlog, from the task itself. */
  protected async setSprint(sprintKey: string | null): Promise<void> {
    const task = this.task();
    if (!task) return;
    const column: BoardColumn = sprintKey === null ? 'backlog' : task.column === 'backlog' ? 'todo' : task.column;
    await this.change(() =>
      withScopeConfirmation(ack => this.api.move(this.key(), column, sprintKey, null, ack))
    );
  }

  protected async setColumn(column: BoardColumn): Promise<void> {
    const task = this.task();
    if (!task) return;
    await this.change(() =>
      withScopeConfirmation(ack =>
        this.api.move(this.key(), column, column === 'backlog' ? null : task.sprintKey, null, ack)
      )
    );
  }

  protected async remove(): Promise<void> {
    const task = this.task();
    if (!task) return;
    const ok = await this.confirm.ask({
      title: `Delete ${task.key} “${task.title}”?`,
      message: 'Its comments and attachments go too, and its number is reused by the next task.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;

    try {
      await this.api.delete(task.key);
      if (this.embedded()) this.deleted.emit();
      else await this.router.navigate(['/board/backlog']);
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not delete that task.');
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
      this.confirm.error(apiError(e).message ?? 'Could not save that change.');
    }
  }
}
