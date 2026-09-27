import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { Goal } from '../core/goals.service';

/**
 * A goal's progress as one bar that only reads out. A goal with child goals averages them ("1 of
 * 3 months done"); a month with tasks counts them ("1 of 2 tasks done"; value points never
 * count); anything else is set by hand with Update progress, which opens GoalProgressDialog.
 *
 * Shown on the goals page and on a goal's own page.
 */
@Component({
  selector: 'app-goal-progress',
  template: `
    <span class="bar" role="progressbar" [attr.aria-label]="'Progress of ' + goal().key"
          [attr.aria-valuenow]="goal().effectiveProgress" aria-valuemin="0" aria-valuemax="100">
      <span class="fill" [style.width.%]="goal().effectiveProgress"></span>
    </span>
    <span class="value">{{ goal().effectiveProgress }}%</span>
    @if (goal().childCount > 0) {
      <span class="detail">{{ goal().completedChildCount }} of {{ goal().childCount }} {{ childWord() }} done</span>
    } @else if (goal().taskCount > 0) {
      <span class="detail">{{ goal().doneTaskCount }} of {{ goal().taskCount }} {{ goal().taskCount === 1 ? 'task' : 'tasks' }} done</span>
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

    .bar {
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

    .value {
      min-width: 2.5rem;
      font-weight: 700;
      color: var(--mat-sys-on-surface);
    }
  `,
})
export class GoalProgress {
  readonly goal = input.required<Goal>();

  protected childWord(): string {
    const plural = this.goal().childCount !== 1;
    return this.goal().periodType === 'year' ? (plural ? 'quarters' : 'quarter') : (plural ? 'months' : 'month');
  }
}
