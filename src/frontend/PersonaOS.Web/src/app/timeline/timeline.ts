import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { Confirm } from '../core/confirm';
import { PageTabs } from '../shared/page-tabs';
import { PERIOD_LABELS, formatGoalRange, monthName } from '../core/goal-calendar';
import { Goal, GoalsService, TreeRow, treeRows, unfolded } from '../core/goals.service';
import { todayLocal } from '../core/local-date';

/** A goal's row: where its bar sits on the year, as percentages of the year's width. */
interface Row extends TreeRow {
  /** 0 for a year, 1 for a quarter, 2 for a month: the level it is drawn at, nested or not. */
  level: number;
  left: number;
  width: number;
}

/** A row as drawn: whether it opens or closes its lane, for the lane's gap and corners. */
interface VisibleRow extends Row {
  laneFirst: boolean;
  laneLast: boolean;
}

const LEVEL: Record<Goal['periodType'], number> = { year: 0, quarter: 1, month: 2 };

/** Days since 1 January of `year`, and the year's length, for placing a day on the axis. */
function dayOfYear(day: string, year: number): number {
  return (Date.UTC(Number(day.slice(0, 4)), Number(day.slice(5, 7)) - 1, Number(day.slice(8, 10))) - Date.UTC(year, 0, 1)) / 86_400_000;
}

function daysIn(year: number): number {
  return dayOfYear(`${year + 1}-01-01`, year);
}

/**
 * The goals as a roadmap for one year: a bar per goal on a month axis, years above their quarters
 * above their months, each labelled with its key and title and filled to its progress. Standalone
 * quarters and months sit at their own level. Rows with child goals fold. A bar opens the goal.
 * The same goals as the Goals page, only drawn on time (PRD 3.2).
 */
@Component({
  selector: 'app-timeline',
  imports: [RouterLink, PageTabs, MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule],
  templateUrl: './timeline.html',
  styleUrl: './timeline.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Timeline implements OnInit {
  private readonly goalsApi = inject(GoalsService);
  private readonly confirm = inject(Confirm);

  protected readonly today = todayLocal();
  private readonly thisYear = Number(this.today.slice(0, 4));

  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  protected readonly year = signal(this.thisYear);
  /** Goals folded shut: their child goals are hidden. */
  protected readonly collapsed = signal<ReadonlySet<number>>(new Set());

  protected readonly periodLabels = PERIOD_LABELS;
  protected readonly quarters = [1, 2, 3, 4];

  /** The current year, and every year that has a goal. */
  protected readonly years = computed(() =>
    [...new Set([this.thisYear, ...this.goals().map(g => Number(g.periodEnd.slice(0, 4)))])].sort((a, b) => a - b));

  /** Where each month starts, as a percentage of the year: the axis and the grid lines. */
  protected readonly months = computed(() => {
    const year = this.year();
    const days = daysIn(year);
    return Array.from({ length: 12 }, (_, i) => {
      const start = dayOfYear(`${year}-${`${i + 1}`.padStart(2, '0')}-01`, year);
      const end = i === 11 ? days : dayOfYear(`${year}-${`${i + 2}`.padStart(2, '0')}-01`, year);
      return { name: monthName(i + 1), left: (start / days) * 100, width: ((end - start) / days) * 100 };
    });
  });

  protected readonly rows = computed<Row[]>(() => {
    const year = this.year();
    const days = daysIn(year);
    return treeRows(this.goals().filter(g => Number(g.periodEnd.slice(0, 4)) === year)).map(row => {
      const start = dayOfYear(row.goal.periodStart, year);
      const end = dayOfYear(row.goal.periodEnd, year) + 1;
      return { ...row, level: LEVEL[row.goal.periodType], left: (start / days) * 100, width: ((end - start) / days) * 100 };
    });
  });

  protected readonly visibleRows = computed<VisibleRow[]>(() => {
    const rows = unfolded(this.rows(), this.collapsed());
    return rows.map((r, i) => ({
      ...r,
      laneFirst: i === 0 || rows[i - 1].lane !== r.lane,
      laneLast: i === rows.length - 1 || rows[i + 1].lane !== r.lane,
    }));
  });

  /** Today's place on the axis, only in the current year. */
  protected readonly todayAt = computed(() =>
    this.year() === this.thisYear ? ((dayOfYear(this.today, this.year()) + 0.5) / daysIn(this.year())) * 100 : null);

  protected readonly anyCollapsed = computed(() => this.collapsed().size > 0);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  async ngOnInit(): Promise<void> {
    try {
      this.goals.set(await this.goalsApi.getAll());
    } catch {
      this.confirm.error('Could not load your goals.');
    } finally {
      this.loading.set(false);
    }
    setTimeout(() => this.scrollToToday());
  }

  /** When the year is wider than the screen, starts the view a month before today. */
  private scrollToToday(): void {
    const el = this.scroller()?.nativeElement;
    const at = this.todayAt();
    if (!el || at === null || el.scrollWidth <= el.clientWidth) return;
    const label = el.querySelector<HTMLElement>('.label')?.offsetWidth ?? 0;
    const track = el.scrollWidth - label;
    el.scrollLeft = Math.max(0, (track * (at - 100 / 12)) / 100);
  }

  protected setYear(year: number): void {
    this.year.set(year);
    this.collapsed.set(new Set());
  }

  protected toggle(row: Row): void {
    this.collapsed.update(set => {
      const next = new Set(set);
      if (!next.delete(row.goal.id)) next.add(row.goal.id);
      return next;
    });
  }

  protected toggleAll(): void {
    this.collapsed.set(this.anyCollapsed() ? new Set() : new Set(this.rows().filter(r => r.hasChildren).map(r => r.goal.id)));
  }

  protected overdue(goal: Goal): boolean {
    return goal.status === 'active' && goal.periodEnd < this.today;
  }

  protected describe(goal: Goal): string {
    const state = goal.status === 'completed' ? 'completed' : this.overdue(goal) ? 'overdue' : 'active';
    return `${goal.key} ${goal.title}, ${PERIOD_LABELS[goal.periodType].toLowerCase()} ${goal.slot}, `
      + `${formatGoalRange(goal.periodStart, goal.periodEnd)}, ${goal.effectiveProgress}% done, ${state}`;
  }
}
