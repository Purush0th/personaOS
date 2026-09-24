import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import {
  BoardColumn,
  BoardService,
  COLUMN_LABELS,
  PRIORITY_LABELS,
  Priority,
  Sprint,
  SprintDetail,
  SprintReport,
  apiError,
  formatWhen,
} from '../core/board.service';
import { BoardTabs } from './board-tabs';
import { BreakdownBar, BreakdownPart } from './breakdown-bar';
import { BurndownChart } from './burndown-chart';
import { openTaskFromQuery } from './task-dialog';
import { WorkItemTable } from './work-item-table';

/** Every started sprint the server will list (two years of weekly ones), so old ones can be found. */
const ALL_SPRINTS = 104;

const STATUSES: BoardColumn[] = ['todo', 'in_progress', 'done'];
const PRIORITIES: Priority[] = ['highest', 'high', 'medium', 'low', 'lowest'];
const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The board's Reports view, after Jira's: pick any started sprint (the running one by default,
 * finished ones by searching), see it summed up in a few numbers, two bars and its burndown, and
 * every task in it in a searchable table. The chosen sprint is in the URL (?sprint=), so a report
 * can be linked and Back returns to the previous one. Velocity across sprints closes the page.
 */
@Component({
  selector: 'app-reports',
  imports: [
    BoardTabs,
    BreakdownBar,
    BurndownChart,
    MatAutocompleteModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    RouterLink,
    WorkItemTable,
  ],
  templateUrl: './reports.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './reports.scss',
})
export class Reports implements OnInit {
  private readonly api = inject(BoardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  // Plain signals rather than resource(): resource's code is shared with the app shell's copy of
  // @angular/core, so using it here would add it to the first download of every page.
  protected readonly report = signal<SprintReport | null>(null);
  protected readonly reportError = signal<string | null>(null);
  private readonly loadedDetail = signal<SprintDetail | null>(null);
  protected readonly detailError = signal<string | null>(null);

  private readonly requestedKey = toSignal(
    this.route.queryParamMap.pipe(map(params => params.get('sprint'))),
    { initialValue: null }
  );

  /** The sprint on show: the one in the URL, else the running one, else the latest. */
  protected readonly selectedKey = computed(() => {
    const sprints = this.report()?.sprints ?? [];
    const requested = sprints.find(s => s.key === this.requestedKey());
    return (requested ?? sprints.find(s => s.status === 'active') ?? sprints[0])?.key;
  });

  /** The selected sprint's detail, once it has arrived; the previous one is never shown under it. */
  protected readonly detail = computed(() => {
    const detail = this.loadedDetail();
    return detail?.sprint.key === this.selectedKey() ? detail : null;
  });

  /** What is typed in the sprint search, and the sprints it matches. */
  protected readonly sprintQuery = signal('');
  protected readonly matchingSprints = computed(() => {
    const words = this.sprintQuery().trim().toLowerCase().split(/\s+/).filter(Boolean);
    return (this.report()?.sprints ?? []).filter(s => {
      const text = `${this.title(s)} ${this.dates(s)}`.toLowerCase();
      return words.every(w => text.includes(w));
    });
  });

  /** Oldest first, as a chart reads left to right; the API lists the newest first. */
  protected readonly chronological = computed(() => [...(this.report()?.sprints ?? [])].reverse());
  private readonly scale = computed(() =>
    Math.max(1, ...this.chronological().flatMap(s => [this.committed(s), s.completedPoints]))
  );

  /** Average points over the last three finished sprints; null until one has finished. */
  protected readonly velocity = computed(() => this.report()?.velocity ?? null);

  protected readonly doneCount = computed(
    () => (this.detail()?.tasks ?? []).filter(t => t.column === 'done').length
  );

  protected readonly byStatus = computed<BreakdownPart[]>(() => {
    const tasks = this.detail()?.tasks ?? [];
    return STATUSES.map(column => ({
      label: COLUMN_LABELS[column],
      count: tasks.filter(t => t.column === column).length,
      tone: `s-${column}`,
    }));
  });

  protected readonly byPriority = computed<BreakdownPart[]>(() => {
    const tasks = this.detail()?.tasks ?? [];
    return PRIORITIES.map(priority => ({
      label: PRIORITY_LABELS[priority],
      count: tasks.filter(t => t.priority === priority).length,
      tone: `p-${priority}`,
    }));
  });

  constructor() {
    // A task opened from the table may have changed; the sprint's numbers are fetched again.
    openTaskFromQuery(() => void this.loadDetail(this.selectedKey()));
    effect(() => {
      const key = this.selectedKey();
      untracked(() => void this.loadDetail(key));
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.report.set(await this.api.report(ALL_SPRINTS));
    } catch (e: unknown) {
      this.reportError.set(apiError(e).message ?? 'Could not load the reports.');
    }
  }

  /** Fetches a sprint's detail; an answer for a sprint no longer selected is dropped by `detail`. */
  private async loadDetail(key: string | undefined): Promise<void> {
    if (!key) return;
    this.detailError.set(null);
    try {
      this.loadedDetail.set(await this.api.sprint(key));
    } catch (e: unknown) {
      if (key === this.selectedKey()) this.detailError.set(apiError(e).message ?? 'Could not load that sprint.');
    }
  }

  protected choose(key: string): void {
    this.sprintQuery.set('');
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sprint: key },
      queryParamsHandling: 'merge',
    });
  }

  /** What the sprint set out to do: frozen when it started, or its current total before that. */
  protected committed(sprint: Sprint): number {
    return sprint.committedPoints ?? sprint.totalPoints;
  }

  protected height(points: number): number {
    return (points / this.scale()) * 100;
  }

  /** The last tile: time left in a running sprint, or when a finished one closed. */
  protected timing(sprint: Sprint, now = new Date()): { value: string; caption: string } {
    if (sprint.status === 'closed') {
      return { value: 'Finished', caption: formatWhen(sprint.closedAtUtc ?? sprint.endsAtUtc) };
    }
    const days = Math.ceil((new Date(sprint.endsAtUtc).getTime() - now.getTime()) / DAY_MS);
    return days > 0
      ? { value: `${days} ${days === 1 ? 'day' : 'days'}`, caption: 'left in the sprint' }
      : { value: 'Overdue', caption: `ended ${formatWhen(sprint.endsAtUtc)}` };
  }

  protected title(sprint: Sprint): string {
    return sprint.name ? `${sprint.key} · ${sprint.name}` : sprint.key;
  }

  protected dates(sprint: Sprint): string {
    return `${formatWhen(sprint.startsAtUtc)} – ${formatWhen(sprint.endsAtUtc)}`;
  }
}
