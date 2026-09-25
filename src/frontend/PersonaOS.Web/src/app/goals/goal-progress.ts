import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';

import { Goal } from '../core/goals.service';

/**
 * A goal's progress as one bar. With tasks, the bar is theirs and only reads out ("2 of 5 points
 * · 1 of 2 tasks"). Without, the bar itself is the control: drag it, or use the arrow keys, in 5%
 * steps, and the new value is saved when it is let go. It replaces a number field with a "%"
 * suffix, which took typing and a blur to change, and sat beside a second copy of the same number.
 *
 * Shown on the goals page and on a goal's own page.
 */
@Component({
  selector: 'app-goal-progress',
  template: `
    @if (goal().taskCount === 0) {
      <input
        type="range"
        min="0"
        max="100"
        step="5"
        [value]="shown()"
        [style.--fill.%]="shown()"
        [attr.aria-label]="'Progress of ' + goal().key"
        [attr.aria-valuetext]="shown() + '%'"
        (input)="shown.set(+$any($event.target).value)"
        (change)="save(+$any($event.target).value)"
      />
    } @else {
      <span class="bar" role="progressbar" [attr.aria-valuenow]="shown()" aria-valuemin="0" aria-valuemax="100">
        <span class="fill" [style.width.%]="shown()"></span>
      </span>
    }
    <span class="value">{{ shown() }}%</span>
    @if (goal().taskCount > 0) {
      <span class="detail">
        {{ goal().donePoints }} of {{ goal().totalPoints }} points · {{ goal().doneTaskCount }} of {{ goal().taskCount }} tasks
      </span>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 0.25rem 0.75rem;
      font: var(--mat-sys-label-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .bar,
    input {
      flex: 1 1 12rem;
      height: 6px;
      border-radius: var(--mat-sys-corner-full);
      background: var(--mat-sys-surface-container-highest);
      overflow: hidden;
    }

    .fill {
      display: block;
      height: 100%;
      background: var(--mat-sys-primary);
    }

    // The slider draws as the same bar, filled to its value, with a round handle on it.
    input {
      appearance: none;
      margin: 0;
      overflow: visible;
      cursor: pointer;
      background: linear-gradient(
        to right,
        var(--mat-sys-primary) var(--fill),
        var(--mat-sys-surface-container-highest) var(--fill)
      );

      &:focus-visible {
        outline: 2px solid var(--mat-sys-primary);
        outline-offset: 4px;
      }

      &::-webkit-slider-thumb {
        appearance: none;
        width: 14px;
        height: 14px;
        border-radius: 50%;
        background: var(--mat-sys-primary);
        border: 2px solid var(--mat-sys-surface);
      }

      &::-moz-range-thumb {
        width: 14px;
        height: 14px;
        border-radius: 50%;
        background: var(--mat-sys-primary);
        border: 2px solid var(--mat-sys-surface);
      }
    }

    .value {
      min-width: 2.5rem;
      font-weight: 700;
      color: var(--mat-sys-on-surface);
    }
  `,
})
export class GoalProgress {
  readonly goal = input.required<Goal>();
  /** A new hand-set progress, 0 to 100, once the slider is let go. */
  readonly changed = output<number>();

  /**
   * The value shown: the thumb's while it is dragged, then held until the saved goal comes back
   * (so the bar does not jump back to the old value in between), then the goal's own.
   */
  protected readonly shown = linkedSignal(() => this.goal().effectiveProgress);

  protected save(value: number): void {
    if (value !== this.goal().progress) this.changed.emit(value);
  }
}
