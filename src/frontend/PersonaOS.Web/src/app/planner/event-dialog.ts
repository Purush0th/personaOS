import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';

import { BoardService, BoardTask, apiError } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Goal, GoalsService } from '../core/goals.service';
import { EventChange, EventFields, PlannerItemDto, PlannerService } from '../core/planner.service';
import { shiftLocalDate } from '../core/local-date';
import {
  CustomRepeat, DEFAULT_NOTIFY_MINUTES, MAX_NOTIFY_MINUTES, NOTIFY_PRESETS, NOTIFY_UNIT_MINUTES, NotifyUnit, RepeatOption,
  WEEK_ORDER, customRule, dayDiff, defaultCustomRepeat, formatMoment, largestUnit, lastDayOf, leadText, minutesOf, notifyAt,
  parseCustom, repeatOptions, timeOf,
} from './calendar';
import { ScopeDialog } from './scope-dialog';

export interface EventDialogData {
  /** The event to edit; absent for a new one. */
  item?: PlannerItemDto;
  /** For a new event: the day, and the start when it was made from a time slot. */
  date?: string;
  start?: string | null;
}

/** What a notification select holds: a preset's minutes, 'none' or 'custom'. */
type NotifyChoice = number | 'none' | 'custom';

/**
 * Creates or edits a planner event, Google Calendar style: title, first and last day, all day or a
 * start and end,
 * repeat (presets and a custom editor), notification (presets and a custom number with its unit),
 * notes and a goal. Saving a repeating event asks which events the change is for.
 */
@Component({
  selector: 'app-event-dialog',
  imports: [
    FormsModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatDialogModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatRadioModule, MatSelectModule,
  ],
  templateUrl: './event-dialog.html',
  styleUrl: './event-dialog.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class EventDialog implements OnInit {
  protected readonly data = inject<EventDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<EventDialog, boolean>);
  private readonly planner = inject(PlannerService);
  private readonly goalsApi = inject(GoalsService);
  private readonly dialog = inject(MatDialog);
  private readonly board = inject(BoardService);
  private readonly branding = inject(BrandingService);

  protected readonly editing = !!this.data.item;
  protected readonly weekOrder = WEEK_ORDER;
  protected readonly dayNames: Record<string, string> = { MO: 'M', TU: 'T', WE: 'W', TH: 'T', FR: 'F', SA: 'S', SU: 'S' };
  protected readonly notifyPresets = NOTIFY_PRESETS;
  protected readonly maxNotify = MAX_NOTIFY_MINUTES;

  protected readonly title = signal(this.data.item?.title ?? '');
  protected readonly date = signal(this.data.item?.date ?? this.data.date ?? '');
  /** The last day: the first, unless the event lasts several days. */
  protected readonly endDate = signal(this.data.item ? lastDayOf(this.data.item) : (this.data.date ?? ''));
  protected readonly allDay = signal(this.data.item ? this.data.item.scheduledTime === null : !this.data.start);
  protected readonly start = signal(this.initialStart());
  protected readonly end = signal(this.initialEnd());
  protected readonly notes = signal(this.data.item?.notes ?? '');
  protected readonly goalId = signal<number | null>(this.data.item?.goalId ?? null);
  protected readonly goals = signal<Goal[]>([]);
  /** The running sprint's unfinished tasks, to plan a day's work on (new events only). */
  protected readonly sprintTasks = signal<BoardTask[]>([]);
  protected readonly taskId = signal<number | null>(null);

  // Repeat: a preset rule, null (does not repeat) or 'custom'.
  protected readonly repeatChoice = signal<string | null>(null);
  protected readonly custom = signal<CustomRepeat>(defaultCustomRepeat(this.date() || '2026-01-01'));
  protected readonly repeatChoices = computed<RepeatOption[]>(() => repeatOptions(this.date() || '2026-01-01'));

  // Notification.
  protected readonly notifyChoice = signal<NotifyChoice>('none');
  protected readonly customAmount = signal(1);
  protected readonly customUnit = signal<NotifyUnit>('hours');

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The notification lead in minutes, or null for none. */
  protected readonly notifyMinutes = computed<number | null>(() => {
    const choice = this.notifyChoice();
    if (choice === 'none') return null;
    if (choice === 'custom') return Math.max(0, Math.round(this.customAmount() || 0)) * NOTIFY_UNIT_MINUTES[this.customUnit()];
    return choice;
  });

  /** Too far ahead for the server: more than four weeks. */
  protected readonly notifyTooFar = computed(() => (this.notifyMinutes() ?? 0) > MAX_NOTIFY_MINUTES);

  /** "You will be notified Fri 2 Oct, 9:30 am, and when it starts", or why that cannot be. */
  protected readonly notifyHint = computed(() => {
    const minutes = this.notifyMinutes();
    if (!this.date()) return null;
    // Every event is notified when it starts; the choice here is a heads-up before that.
    if (minutes === null || minutes === 0) return 'You will be notified when it starts.';
    const at = formatMoment(notifyAt(this.date(), this.allDay() ? null : this.start(), minutes));
    if (this.notifyTooFar()) {
      return `Too far ahead: ${leadText(minutes)} before would be ${at}. The most is 4 weeks (${MAX_NOTIFY_MINUTES.toLocaleString('en-GB')} minutes) before.`;
    }
    const prefix = this.allDay() ? 'All-day events start at 12:00 am. ' : '';
    return `${prefix}You will be notified ${at}, and when it starts.`;
  });

  protected readonly multiDay = computed(() => !!this.endDate() && this.endDate() > this.date());

  protected readonly timeError = computed(() => {
    if (this.endDate() && this.date() && this.endDate() < this.date()) return 'The end day must be on or after the start day.';
    return !this.multiDay() && !this.allDay() && this.start() && this.end() && minutesOf(this.end()) <= minutesOf(this.start())
      ? 'The end must be after the start.'
      : null;
  });

  /** The rule the form describes now, or null for no repeat. */
  protected readonly rule = computed<string | null>(() => {
    const choice = this.repeatChoice();
    if (choice === 'custom') return customRule(this.custom(), this.date());
    return choice;
  });

  protected readonly canSave = computed(
    () => !!this.title().trim() && !!this.date() && !!this.endDate() && !this.timeError() && !this.notifyTooFar()
      && !this.saving() && (this.allDay() || (!!this.start() && !!this.end()))
  );

  async ngOnInit(): Promise<void> {
    this.initRepeat();
    this.initNotify();
    try {
      this.goals.set((await this.goalsApi.getAll()).filter(g => g.status === 'active' || g.id === this.goalId()));
    } catch {
      this.goals.set([]);
    }
    if (!this.editing && this.branding.isEnabled('board')) {
      try {
        const board = await this.board.board();
        this.sprintTasks.set([...board.inProgress, ...board.todo]);
      } catch {
        this.sprintTasks.set([]);
      }
    }
  }

  /** Planning a board task: the event takes its title (unless one was typed) and its goal. */
  protected pickTask(id: number | null): void {
    this.taskId.set(id);
    const task = this.sprintTasks().find(t => t.id === id);
    if (!task) return;
    if (!this.title().trim()) this.title.set(task.title);
    if (task.goalId != null) this.goalId.set(task.goalId);
  }

  // --- Times ---------------------------------------------------------------------------

  private initialStart(): string {
    const item = this.data.item;
    if (item?.scheduledTime) return item.scheduledTime.slice(0, 5);
    return this.data.start?.slice(0, 5) ?? '09:00';
  }

  private initialEnd(): string {
    const item = this.data.item;
    if (item?.endTime) return item.endTime.slice(0, 5);
    return timeOf(minutesOf(this.initialStart()) + 60);
  }

  /** Moving the start keeps the length, as Google Calendar does (over several days the end stays). */
  protected setStart(value: string): void {
    if (!value) return;
    const length = minutesOf(this.end()) - minutesOf(this.start());
    this.start.set(value);
    if (!this.multiDay()) this.end.set(timeOf(minutesOf(value) + (length > 0 ? length : 60)));
  }

  protected setEndDate(value: string): void {
    if (value) this.endDate.set(value);
  }

  protected setAllDay(value: boolean): void {
    this.allDay.set(value);
    // A new timed event gets the usual heads-up; a new all-day one starts without.
    if (!this.editing) this.notifyChoice.set(value ? 'none' : DEFAULT_NOTIFY_MINUTES);
  }

  // --- Repeat --------------------------------------------------------------------------

  private initRepeat(): void {
    const rule = this.data.item?.recurrence ?? null;
    if (!rule) {
      this.repeatChoice.set(null);
      return;
    }
    // A rule a preset makes shows as that preset; anything else opens in the custom editor.
    const preset = this.repeatChoices().find(o => o.rule === rule);
    if (preset) {
      this.repeatChoice.set(rule);
    } else {
      this.custom.set(parseCustom(rule, this.date()));
      this.repeatChoice.set('custom');
    }
  }

  /** A new first day moves the last day by as much, so the event keeps its length. */
  protected setDate(value: string): void {
    if (!value) return;
    const choice = this.repeatChoice();
    const index = this.repeatChoices().findIndex(o => o.rule === choice);
    const days = this.date() && this.endDate() ? Math.max(0, dayDiff(this.date(), this.endDate())) : 0;
    this.date.set(value);
    this.endDate.set(shiftLocalDate(value, days));
    // "Weekly on Saturday" follows the day: keep the same kind of choice for the new day.
    if (choice !== null && choice !== 'custom' && index >= 0) {
      const options = this.repeatChoices();
      this.repeatChoice.set(options[Math.min(index, options.length - 2)].rule as string | null);
    }
  }

  protected updateCustom(change: Partial<CustomRepeat>): void {
    this.custom.set({ ...this.custom(), ...change });
  }

  protected toggleDay(code: string): void {
    const days = this.custom().days;
    const next = days.includes(code) ? days.filter(d => d !== code) : [...days, code];
    if (next.length > 0) this.updateCustom({ days: next });
  }

  // --- Notification --------------------------------------------------------------------

  private initNotify(): void {
    const item = this.data.item;
    const minutes = item ? item.notifyBeforeMinutes : this.allDay() ? null : DEFAULT_NOTIFY_MINUTES;
    if (minutes === null) {
      this.notifyChoice.set('none');
    } else if (NOTIFY_PRESETS.some(p => p.minutes === minutes)) {
      this.notifyChoice.set(minutes);
    } else {
      const unit = largestUnit(minutes);
      this.customUnit.set(unit);
      this.customAmount.set(minutes / NOTIFY_UNIT_MINUTES[unit]);
      this.notifyChoice.set('custom');
    }
  }

  protected step(delta: number): void {
    this.customAmount.set(Math.max(0, Math.round(this.customAmount() || 0) + delta));
  }

  protected setAmount(value: number | string): void {
    const n = Number(value);
    this.customAmount.set(Number.isFinite(n) ? Math.max(0, Math.round(n)) : 0);
  }

  // --- Saving --------------------------------------------------------------------------

  protected async save(): Promise<void> {
    if (!this.canSave()) return;
    this.error.set(null);
    this.saving.set(true);
    try {
      if (this.data.item) {
        const done = await this.saveChange(this.data.item);
        if (done) this.ref.close(true);
      } else {
        await this.planner.create(this.fields());
        this.ref.close(true);
      }
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not save the event.');
    } finally {
      this.saving.set(false);
    }
  }

  private fields(): EventFields {
    return {
      title: this.title().trim(),
      date: this.date(),
      endDate: this.multiDay() ? this.endDate() : null,
      notes: this.notes().trim() || null,
      scheduledTime: this.allDay() ? null : this.start(),
      endTime: this.allDay() ? null : this.end(),
      notifyBeforeMinutes: this.notifyMinutes(),
      recurrence: this.rule(),
      goalId: this.goalId(),
      taskId: this.taskId(),
    };
  }

  /** Sends only what changed, asking which events it is for when the event repeats. False when cancelled. */
  private async saveChange(item: PlannerItemDto): Promise<boolean> {
    const change: EventChange = {};
    const f = this.fields();
    if (f.title !== item.title) change.title = f.title;
    if ((f.notes ?? '') !== (item.notes ?? '')) change.notes = f.notes ?? '';
    if (f.date !== item.date) change.date = f.date;
    // The server keeps the length when only the first day moves; a different length is sent.
    if (dayDiff(f.date, f.endDate ?? f.date) !== dayDiff(item.date, lastDayOf(item))) change.endDate = f.endDate ?? f.date;

    const oldStart = item.scheduledTime?.slice(0, 5) ?? null;
    const oldEnd = item.endTime?.slice(0, 5) ?? null;
    if (f.scheduledTime === null && oldStart !== null) change.clearScheduledTime = true;
    if (f.scheduledTime !== null && f.scheduledTime !== oldStart) change.scheduledTime = f.scheduledTime;
    if (f.endTime !== null && f.endTime !== oldEnd) change.endTime = f.endTime;

    const seriesChanged =
      f.notifyBeforeMinutes !== item.notifyBeforeMinutes || f.recurrence !== item.recurrence || f.goalId !== item.goalId;
    if (f.notifyBeforeMinutes !== item.notifyBeforeMinutes) {
      if (f.notifyBeforeMinutes === null) change.clearNotify = true;
      else change.notifyBeforeMinutes = f.notifyBeforeMinutes;
    }
    if (f.recurrence !== item.recurrence) {
      if (f.recurrence === null) change.clearRecurrence = true;
      else change.recurrence = f.recurrence;
    }
    if (f.goalId !== item.goalId) {
      if (f.goalId === null) change.clearGoal = true;
      else change.goalId = f.goalId;
    }

    if (Object.keys(change).length === 0) return true;

    if (!item.recurrence) {
      await this.planner.update(item.id, change);
      return true;
    }

    const scope = await ScopeDialog.ask(this.dialog, { action: 'edit', seriesOnly: seriesChanged });
    if (!scope) return false;
    await this.planner.update(item.id, change, { scope, occurrence: item.occurrenceDate });
    return true;
  }
}
