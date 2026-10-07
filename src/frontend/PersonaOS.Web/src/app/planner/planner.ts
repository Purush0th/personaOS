import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { Confirm } from '../core/confirm';
import { todayLocal } from '../core/local-date';
import { PlannerItemDto, PlannerService } from '../core/planner.service';
import { CalendarView, VIEWS, daysBetween, isWeekend, stepAnchor, viewRange, viewTitle } from './calendar';
import { EventDialog, EventDialogData } from './event-dialog';
import { EventPeek } from './event-peek';
import { MonthView } from './month-view';
import { ScheduleView } from './schedule-view';
import { TimeGrid } from './time-grid';
import { YearView } from './year-view';

/** The view options, as the toggles at the bottom of Google Calendar's view menu. */
interface CalendarOptions {
  showWeekends: boolean;
  showSkipped: boolean;
  showCompleted: boolean;
}

const OPTIONS_KEY = 'planner.options';
const DEFAULT_OPTIONS: CalendarOptions = { showWeekends: true, showSkipped: true, showCompleted: true };

const STEP_LABELS: Record<CalendarView, string> = {
  day: 'day',
  '4day': '4 days',
  week: 'week',
  month: 'month',
  year: 'year',
  schedule: 'period',
};

/**
 * The planner as a calendar, after Google Calendar: Day, Week, Month, Year, Schedule and 4 days,
 * the same keyboard shortcuts (D W M Y A X, T for today, J/N and K/P to step, C to create), and
 * the view and day in the address so a reload or a shared link opens the same place.
 */
@Component({
  selector: 'app-planner',
  imports: [
    MatButtonModule, MatDividerModule, MatIconModule, MatMenuModule, MatProgressBarModule, MatTooltipModule,
    MonthView, ScheduleView, TimeGrid, YearView,
  ],
  templateUrl: './planner.html',
  styleUrl: './planner.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class Planner implements OnInit {
  private readonly planner = inject(PlannerService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(Confirm);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly views = VIEWS;

  protected readonly view = signal<CalendarView>('week');
  protected readonly anchor = signal(todayLocal());
  protected readonly options = signal<CalendarOptions>(loadOptions());
  protected readonly loading = signal(true);
  private readonly items = signal<PlannerItemDto[]>([]);

  protected readonly title = computed(() => viewTitle(this.view(), this.anchor()));
  protected readonly viewLabel = computed(() => VIEWS.find(v => v.view === this.view())!.label);
  protected readonly stepLabel = computed(() => STEP_LABELS[this.view()]);

  /** The days the grid views show, without weekends when they are hidden (never for Day). */
  protected readonly gridDays = computed(() => {
    const { from, to } = viewRange(this.view(), this.anchor());
    const days = daysBetween(from, to);
    return this.view() === 'day' || this.options().showWeekends ? days : days.filter(d => !isWeekend(d));
  });

  protected readonly scheduleDays = computed(() => {
    const { from, to } = viewRange('schedule', this.anchor());
    return daysBetween(from, to);
  });

  /** The loaded events by day, without the ones the options hide. */
  protected readonly itemsByDay = computed(() => {
    const { showSkipped, showCompleted } = this.options();
    const byDay = new Map<string, PlannerItemDto[]>();
    for (const item of this.items()) {
      if (!showSkipped && item.status === 'skipped') continue;
      if (!showCompleted && item.status === 'done') continue;
      byDay.set(item.date, [...(byDay.get(item.date) ?? []), item]);
    }
    return byDay;
  });

  /**
   * The address decides the view and the day, so a reload, a shared link and the browser's back
   * and forward all land where they should. The page is reused between them, so it follows the
   * address rather than reading it once.
   */
  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const view = params.get('view') as CalendarView | null;
      this.view.set(view && VIEWS.some(v => v.view === view) ? view : 'week');
      const date = params.get('date');
      this.anchor.set(date && /^\d{4}-\d{2}-\d{2}$/.test(date) ? date : todayLocal());
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const { from, to } = viewRange(this.view(), this.anchor());
      const days = await this.planner.getRange(from, to);
      this.items.set(days.flatMap(d => d.items));
    } catch {
      this.confirm.error('Could not load the planner.');
    } finally {
      this.loading.set(false);
    }
  }

  // --- Navigation ----------------------------------------------------------------------

  protected async setView(view: CalendarView): Promise<void> {
    if (view === this.view()) return;
    this.view.set(view);
    await this.navigated();
  }

  protected async step(direction: 1 | -1): Promise<void> {
    this.anchor.set(stepAnchor(this.view(), this.anchor(), direction));
    await this.navigated();
  }

  protected async goToday(): Promise<void> {
    this.anchor.set(todayLocal());
    await this.navigated();
  }

  protected async openDay(date: string): Promise<void> {
    this.anchor.set(date);
    this.view.set('day');
    await this.navigated();
  }

  /** Puts the view and day in the address; following it (above) loads them. */
  private async navigated(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    if (params.get('view') === this.view() && params.get('date') === this.anchor()) return;
    await this.router.navigate([], { relativeTo: this.route, queryParams: { view: this.view(), date: this.anchor() } });
  }

  protected setOption(key: keyof CalendarOptions): void {
    const next = { ...this.options(), [key]: !this.options()[key] };
    this.options.set(next);
    try {
      localStorage.setItem(OPTIONS_KEY, JSON.stringify(next));
    } catch {
      // Storage may be blocked: the choice simply lasts for this visit.
    }
  }

  // --- Events --------------------------------------------------------------------------

  protected async openEvent(item: PlannerItemDto): Promise<void> {
    const changed = await firstValueFrom(
      this.dialog.open<EventPeek, PlannerItemDto, boolean>(EventPeek, { data: item, width: '28rem', maxWidth: '95vw', autoFocus: 'dialog' })
        .afterClosed()
    );
    if (changed) await this.reload();
  }

  protected async createEvent(at?: { date: string; start: string | null }): Promise<void> {
    const data: EventDialogData = at ?? { date: this.view() === 'year' || this.view() === 'month' ? todayLocal() : this.anchor(), start: '09:00' };
    const saved = await firstValueFrom(
      this.dialog.open<EventDialog, EventDialogData, boolean>(EventDialog, { data, width: '36rem', maxWidth: '95vw' }).afterClosed()
    );
    if (saved) await this.reload();
  }

  // --- Keyboard ------------------------------------------------------------------------

  @HostListener('document:keydown', ['$event'])
  protected async onKey(event: KeyboardEvent): Promise<void> {
    if (event.ctrlKey || event.metaKey || event.altKey || this.dialog.openDialogs.length > 0) return;
    // A key typed into a field, or in an open menu, is not a shortcut. The target can be the
    // document itself, which has no closest().
    const target = event.target;
    if (target instanceof Element && target.closest('input, textarea, select, [contenteditable="true"], .mat-mdc-menu-panel')) return;

    const key = event.key.toLowerCase();
    const view = VIEWS.find(v => v.key.toLowerCase() === key);
    if (view) {
      event.preventDefault();
      await this.setView(view.view);
    } else if (key === 't') {
      event.preventDefault();
      await this.goToday();
    } else if (key === 'j' || key === 'n') {
      event.preventDefault();
      await this.step(1);
    } else if (key === 'k' || key === 'p') {
      event.preventDefault();
      await this.step(-1);
    } else if (key === 'c') {
      event.preventDefault();
      await this.createEvent();
    }
  }
}

function loadOptions(): CalendarOptions {
  try {
    const saved = JSON.parse(localStorage.getItem(OPTIONS_KEY) ?? 'null') as Partial<CalendarOptions> | null;
    return { ...DEFAULT_OPTIONS, ...(saved ?? {}) };
  } catch {
    return { ...DEFAULT_OPTIONS };
  }
}
