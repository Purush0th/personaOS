import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';

import { Goal } from '../core/goals.service';

/**
 * Update progress, for a goal kept by hand (one without tasks): a slider in 5% steps with the
 * number above it, then Save. The phone has the same dialog. Save is off until the value moves,
 * so an unchanged goal is never written.
 */
@Component({
  selector: 'app-goal-progress-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Update progress</h2>
    <mat-dialog-content>
      <p class="goal"><span class="key">{{ goal.key }}</span> {{ goal.title }}</p>
      <p class="value" aria-live="polite">{{ value() }}%</p>
      <input
        type="range"
        min="0"
        max="100"
        step="5"
        cdkFocusInitial
        [value]="value()"
        [style.--fill.%]="value()"
        [attr.aria-label]="'Progress of ' + goal.key"
        [attr.aria-valuetext]="value() + '%'"
        (input)="value.set(+$any($event.target).value)"
      />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [mat-dialog-close]="value()" [disabled]="value() === start">Save</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .goal {
      margin: 0;
      font: var(--mat-sys-body-medium);
      overflow-wrap: anywhere;

      .key {
        color: var(--mat-sys-on-surface-variant);
      }
    }

    .value {
      margin: 1rem 0 0.5rem;
      font: var(--mat-sys-headline-small);
      color: var(--mat-sys-on-surface);
      text-align: center;
    }

    // One 6px bar filled to the value, with a round handle on it. The input is as tall as the
    // handle and draws the bar across its middle: a 6px input let the handle stick out, which
    // made the dialog content scroll.
    input {
      display: block;
      width: 100%;
      height: 18px;
      margin: 0.5rem 0;
      appearance: none;
      cursor: pointer;
      background:
        linear-gradient(
          to right,
          var(--mat-sys-primary) var(--fill),
          var(--mat-sys-surface-container-highest) var(--fill)
        )
        center / 100% 6px no-repeat;

      &:focus-visible {
        outline: 2px solid var(--mat-sys-primary);
        outline-offset: 2px;
      }

      &::-webkit-slider-thumb {
        appearance: none;
        width: 18px;
        height: 18px;
        border-radius: 50%;
        background: var(--mat-sys-primary);
        border: 2px solid var(--mat-sys-surface);
      }

      &::-moz-range-thumb {
        width: 18px;
        height: 18px;
        border-radius: 50%;
        background: var(--mat-sys-primary);
        border: 2px solid var(--mat-sys-surface);
      }
    }
  `,
})
export class GoalProgressDialog {
  protected readonly goal = inject<Goal>(MAT_DIALOG_DATA);
  /** Where the slider starts: the saved value, on the 5% grid the slider moves in. */
  protected readonly start = Math.round(Math.min(100, Math.max(0, this.goal.progress)) / 5) * 5;
  protected readonly value = signal(this.start);
}

/** Opens Update progress for the goal; resolves to the new value, or null when nothing changed. */
export async function askGoalProgress(dialog: MatDialog, goal: Goal): Promise<number | null> {
  const ref = dialog.open<GoalProgressDialog, Goal, number>(GoalProgressDialog, {
    data: goal,
    width: '26rem',
    maxWidth: '92vw',
  });
  // Cancel, Escape and a click outside close with '' or undefined; only Save gives a number.
  const value: unknown = await firstValueFrom(ref.afterClosed());
  return typeof value !== 'number' || value === goal.progress ? null : value;
}
