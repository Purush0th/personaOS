import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import {
  BoardService,
  Sprint,
  apiError,
  localDay,
  sprintEndFor,
  toDateOnly,
} from '../core/board.service';
import { Confirm } from '../core/confirm';

/**
 * Creating or editing a sprint: a name and a start day. The end is not asked for: a sprint closes
 * on the first Sunday after it starts, at 18:00, and the form shows that date as the start
 * changes. Left without a start, a new sprint takes the next free Sunday-to-Sunday week.
 *
 * The start is the browser's own date field, not Material's datepicker: its value is already the
 * API's 2026-09-27, a phone shows its native calendar, and the datepicker's code would sit in the
 * first download of every page (it shares modules with the app shell). Material hides the
 * browser's calendar icon inside its fields, so the form adds its own and opens the calendar on a
 * click anywhere in the field.
 *
 * Used by the backlog (new and edit) and the sprint page (edit). It saves through the API itself
 * and reports the saved sprint, so each page only has to reload.
 */
@Component({
  selector: 'app-sprint-form',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  templateUrl: './sprint-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './sprint-form.scss',
})
export class SprintForm implements OnInit {
  private readonly api = inject(BoardService);
  private readonly confirm = inject(Confirm);

  /** The sprint to edit; none for a new one. */
  readonly sprint = input<Sprint | null>(null);
  readonly saved = output<Sprint>();
  readonly cancelled = output<void>();

  protected readonly name = signal('');
  /** The start day as the date field holds it, 2026-09-27; empty for "the next free week". */
  protected readonly startsOn = signal('');
  protected readonly saving = signal(false);

  protected readonly endsAt = computed(() => {
    const day = this.startsOn();
    if (!day) return null;
    const [year, month, date] = day.split('-').map(Number);
    return sprintEndFor(new Date(year, month - 1, date));
  });

  ngOnInit(): void {
    const sprint = this.sprint();
    if (!sprint) return;
    this.name.set(sprint.name ?? '');
    this.startsOn.set(toDateOnly(localDay(sprint.startsAtUtc)));
  }

  /** Opens the browser's calendar; where it cannot, the field still takes typing. */
  protected openCalendar(field: HTMLInputElement): void {
    try {
      field.showPicker();
    } catch {
      field.focus();
    }
  }

  protected async save(): Promise<void> {
    if (this.saving()) return;
    const sprint = this.sprint();
    const name = this.name().trim();
    const startsOn = this.startsOn();

    this.saving.set(true);
    try {
      const saved = sprint
        ? await this.api.updateSprint(sprint.key, {
            name: name || null,
            clearName: !name,
            // Only a start that moved is sent: sending it moves the end, which could undo an end
            // the assistant set by hand.
            ...(startsOn && startsOn !== toDateOnly(localDay(sprint.startsAtUtc)) ? { startsOn } : {}),
          })
        : await this.api.createSprint({ name: name || null, startsOn: startsOn || null });
      this.saved.emit(saved);
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not save that sprint.');
    } finally {
      this.saving.set(false);
    }
  }

  protected formatEnd(end: Date): string {
    return end.toLocaleString(undefined, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: 'numeric',
      minute: '2-digit',
    });
  }
}
