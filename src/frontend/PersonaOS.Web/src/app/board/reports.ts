import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { BoardService, Sprint, SprintReport, apiError, formatWhen } from '../core/board.service';
import { Confirm } from '../core/confirm';
import { BoardTabs } from './board-tabs';

/**
 * The board's Reports view, after Jira's: a velocity chart of what each sprint committed to
 * against what it completed, and every started sprint in a table that leads to its burndown.
 */
@Component({
  selector: 'app-reports',
  imports: [BoardTabs, MatProgressBarModule, RouterLink],
  templateUrl: './reports.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './reports.scss',
})
export class Reports implements OnInit {
  private readonly api = inject(BoardService);
  private readonly confirm = inject(Confirm);

  protected readonly report = signal<SprintReport | null>(null);
  protected readonly loading = signal(true);

  /** Oldest first, as a chart reads left to right; the API lists the newest first. */
  protected readonly chronological = computed(() => [...(this.report()?.sprints ?? [])].reverse());

  /** The tallest bar, so every bar is drawn as a share of it. */
  private readonly scale = computed(() =>
    Math.max(1, ...this.chronological().flatMap(s => [this.committed(s), s.completedPoints]))
  );

  async ngOnInit(): Promise<void> {
    try {
      this.report.set(await this.api.report());
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load the reports.');
    } finally {
      this.loading.set(false);
    }
  }

  /** What the sprint set out to do: frozen when it started, or its current total before that. */
  protected committed(sprint: Sprint): number {
    return sprint.committedPoints ?? sprint.totalPoints;
  }

  protected height(points: number): number {
    return (points / this.scale()) * 100;
  }

  protected title(sprint: Sprint): string {
    return sprint.name ? `${sprint.key} · ${sprint.name}` : sprint.key;
  }

  protected when(value: string): string {
    return formatWhen(value);
  }
}
