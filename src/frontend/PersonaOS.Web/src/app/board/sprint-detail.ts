import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { Confirm } from '../core/confirm';

import {
  BoardColumn,
  BoardService,
  BoardTask,
  COLUMN_LABELS,
  SprintDetail as SprintDetailView,
  apiError,
  formatWhen,
  goalHue,
  toLocalInput,
} from '../core/board.service';

/** One point on the burndown, already placed in the SVG's coordinates. */
interface Plot {
  x: number;
  y: number;
  label: string;
  remaining: number;
}

/** One sprint, full screen, at /board/sprints/SPRINT-2: how it went and what was in it. */
@Component({
  selector: 'app-sprint-detail',
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
  ],
  templateUrl: './sprint-detail.html',
  styleUrls: ['./item-page.scss', './sprint-detail.scss'],
})
export class SprintDetail implements OnInit {
  private readonly api = inject(BoardService);
  private readonly route = inject(ActivatedRoute);
  private readonly confirm = inject(Confirm);

  protected readonly detail = signal<SprintDetailView | null>(null);
  protected readonly loading = signal(true);
  protected readonly editing = signal(false);

  name = '';
  start = '';
  end = '';

  protected readonly key = signal('');
  protected readonly columnLabels = COLUMN_LABELS;
  protected readonly columnOrder: BoardColumn[] = ['done', 'in_progress', 'todo'];

  /** The chart's drawing area, in the SVG's own units. */
  private readonly width = 520;
  private readonly height = 180;
  private readonly pad = 28;

  protected readonly plots = computed<Plot[]>(() => {
    const burndown = this.detail()?.burndown ?? [];
    if (burndown.length === 0) return [];

    const max = Math.max(...burndown.map(p => p.remainingPoints + p.completedPoints), 1);
    const step = burndown.length === 1 ? 0 : (this.width - this.pad * 2) / (burndown.length - 1);
    return burndown.map((point, i) => ({
      x: this.pad + step * i,
      y: this.height - this.pad - ((this.height - this.pad * 2) * point.remainingPoints) / max,
      label: new Date(`${point.date}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' }),
      remaining: point.remainingPoints,
    }));
  });

  protected readonly line = computed(() =>
    this.plots().map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ')
  );

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
    const sprint = this.detail()?.sprint;
    if (!sprint) return;
    this.name = sprint.name ?? '';
    this.start = toLocalInput(sprint.startsAtUtc);
    this.end = toLocalInput(sprint.endsAtUtc);
    this.editing.set(true);
  }

  protected async save(): Promise<void> {
    await this.change(() =>
      this.api.updateSprint(this.key(), {
        name: this.name.trim() || null,
        clearName: !this.name.trim(),
        startsAtLocal: this.start || undefined,
        endsAtLocal: this.end || undefined,
      })
    );
    this.editing.set(false);
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

  protected chartSize(): { width: number; height: number; pad: number } {
    return { width: this.width, height: this.height, pad: this.pad };
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
