import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** One part of a whole: a label, how many, and the class that colours it. */
export interface BreakdownPart {
  label: string;
  count: number;
  /** Its colour: a status (`s-todo`, `s-in_progress`, `s-done`) or a priority (`p-high`, …). */
  tone: string;
}

/**
 * How a whole splits into parts, as one stacked bar with a legend underneath: the plain stand-in
 * for the doughnut charts on Jira's reports ("Work items by status", "by type").
 */
@Component({
  selector: 'app-breakdown-bar',
  template: `
    <div class="bar" role="img" [attr.aria-label]="summary()">
      @for (part of shown(); track part.label) {
        <span class="part {{ part.tone }}" [style.flex-grow]="part.count"></span>
      }
    </div>
    <ul class="legend">
      @for (part of parts(); track part.label) {
        <li class="{{ part.tone }}">
          <i></i>{{ part.label }} <b>{{ part.count }}</b>
        </li>
      }
    </ul>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    /* The same colours as the status lozenges and the priority icons (styles.scss). */
    .s-todo {
      --part-color: var(--mat-sys-outline);
    }

    .s-in_progress {
      --part-color: var(--status-progress-fg);
    }

    .s-done {
      --part-color: var(--status-done-fg);
    }

    .p-highest,
    .p-high,
    .p-medium,
    .p-low,
    .p-lowest {
      --part-color: var(--priority-color);
    }

    .bar {
      display: flex;
      gap: 2px;
      height: 0.75rem;
      border-radius: var(--mat-sys-corner-full);
      overflow: hidden;
      background: var(--mat-sys-surface-container-highest);
    }

    .part {
      flex-basis: 0;
      background: var(--part-color);
    }

    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem 1rem;
      margin: 0.6rem 0 0;
      padding: 0;
      list-style: none;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);

      i {
        display: inline-block;
        width: 0.6rem;
        height: 0.6rem;
        margin-right: 0.35rem;
        border-radius: 50%;
        background: var(--part-color);
      }

      b {
        color: var(--mat-sys-on-surface);
      }
    }
  `,
})
export class BreakdownBar {
  readonly parts = input.required<readonly BreakdownPart[]>();

  /** Empty parts take no room in the bar; the legend still lists them, with their zero. */
  protected readonly shown = computed(() => this.parts().filter(p => p.count > 0));

  protected readonly summary = computed(() =>
    this.parts().map(p => `${p.label} ${p.count}`).join(', ')
  );
}
