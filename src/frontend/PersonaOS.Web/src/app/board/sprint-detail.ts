import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Confirm } from '../core/confirm';
import { BurndownChart } from './burndown-chart';
import { SprintForm } from './sprint-form';

import {
  BoardColumn,
  BoardService,
  BoardTask,
  COLUMN_LABELS,
  Sprint,
  SprintDetail as SprintDetailView,
  apiError,
  formatWhen,
  goalHue,
  sprintDayHasCome,
} from '../core/board.service';

/** One sprint, full screen, at /board/sprints/SPRINT-2: how it went and what was in it. */
@Component({
  selector: 'app-sprint-detail',
  imports: [
    BurndownChart,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatListModule,
    MatProgressBarModule,
    SprintForm,
  ],
  templateUrl: './sprint-detail.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./item-page.scss', './sprint-detail.scss'],
})
export class SprintDetail implements OnInit {
  private readonly api = inject(BoardService);
  private readonly route = inject(ActivatedRoute);
  private readonly confirm = inject(Confirm);

  protected readonly detail = signal<SprintDetailView | null>(null);
  protected readonly loading = signal(true);
  protected readonly editing = signal(false);

  protected readonly key = signal('');
  protected readonly columnLabels = COLUMN_LABELS;
  protected readonly columnOrder: BoardColumn[] = ['done', 'in_progress', 'todo'];

  async ngOnInit(): Promise<void> {
    this.key.set(this.route.snapshot.paramMap.get('key') ?? '');
    await this.reload();
  }

  protected async reload(): Promise<void> {
    try {
      this.detail.set(await this.api.sprint(this.key()));
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load that sprint.');
    } finally {
      this.loading.set(false);
    }
  }

  protected startEdit(): void {
    this.editing.set(true);
  }

  protected async sprintSaved(): Promise<void> {
    this.editing.set(false);
    await this.reload();
  }

  protected dayHasCome(sprint: Sprint): boolean {
    return sprintDayHasCome(sprint);
  }

  /** Why Start is greyed out before a sprint's first day; nothing once it can start. */
  protected startHint(sprint: Sprint): string {
    return this.dayHasCome(sprint)
      ? ''
      : `Starts ${formatWhen(sprint.startsAtUtc)}`;
  }

  protected async start_(): Promise<void> {
    const sprint = this.detail()?.sprint;
    if (!sprint) return;
    const ok = await this.confirm.ask({
      title: `Start ${sprint.key}?`,
      message: `${sprint.totalPoints} points are committed when it starts.`,
      confirmLabel: 'Start sprint',
    });
    if (!ok) return;
    await this.change(() => this.api.startSprint(sprint.key));
  }

  protected async complete(): Promise<void> {
    const sprint = this.detail()?.sprint;
    if (!sprint) return;
    const open = sprint.taskCount - sprint.doneTaskCount;
    const ok = await this.confirm.ask({
      title: `Complete ${sprint.key}?`,
      message: open === 0
        ? 'Everything in it is done.'
        : `${open} unfinished ${open === 1 ? 'task moves' : 'tasks move'} on to the next sprint.`,
      confirmLabel: 'Complete sprint',
    });
    if (!ok) return;
    await this.change(() => this.api.completeSprint(sprint.key, {}));
  }

  protected tasksIn(column: string): BoardTask[] {
    return (this.detail()?.tasks ?? []).filter(t => t.column === column);
  }

  protected points(tasks: BoardTask[]): number {
    return tasks.reduce((sum, t) => sum + (t.points ?? 0), 0);
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
