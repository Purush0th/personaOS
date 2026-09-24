import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { BurndownPoint } from '../core/board.service';

/** One day on the chart, as percentages of the plot area from its top-left corner. */
interface Plot {
  x: number;
  y: number;
  label: string;
  remaining: number;
}

/**
 * A sprint's burndown: the points left at the end of each day, as one line. Shown on the sprint
 * page and in the board's Reports under the heading "Burndown"; the caption says what the line
 * is, which holds for a running sprint and a finished one alike. It needs two days of data to
 * draw a line, and says so until then.
 *
 * The plot stretches to any width at a fixed height. Only the line is SVG, drawn in percentages
 * with strokes that keep their thickness; the dots and labels are HTML placed at the same
 * percentages, so text stays at reading size on a phone and a wide screen alike.
 */
@Component({
  selector: 'app-burndown-chart',
  template: `
    @if (plots().length > 1) {
      <p class="caption">Points left each day</p>
      <div class="chart" role="img" [attr.aria-label]="summary()">
        <span class="y top">{{ max() }}</span>
        <span class="y bottom">0</span>
        <div class="plot">
          <svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
            <path class="burn" [attr.d]="line()" />
          </svg>
          @for (p of plots(); track p.x) {
            <span class="dot" [style.left.%]="p.x" [style.top.%]="p.y" [title]="p.label + ': ' + p.remaining + ' points left'"></span>
          }
        </div>
        <span class="x first">{{ plots()[0].label }}</span>
        <span class="x last">{{ plots()[plots().length - 1].label }}</span>
      </div>
    } @else {
      <p class="waiting">Shows after the first day.</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }

    .caption,
    .waiting {
      margin: 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .caption {
      margin-bottom: 0.75rem;
    }

    .chart {
      display: grid;
      grid-template-columns: auto 1fr;
      grid-template-rows: 10rem auto;
      column-gap: 0.5rem;
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .y {
      grid-column: 1;
      grid-row: 1;
      justify-self: end;
    }

    .y.top {
      align-self: start;
      margin-top: -0.5em;
    }

    .y.bottom {
      align-self: end;
      margin-bottom: -0.5em;
    }

    .plot {
      grid-column: 2;
      grid-row: 1;
      position: relative;
      border-left: 1px solid var(--mat-sys-outline-variant);
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }

    svg {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      overflow: visible;
    }

    .burn {
      fill: none;
      stroke: var(--mat-sys-primary);
      stroke-width: 2;
      vector-effect: non-scaling-stroke;
    }

    .dot {
      position: absolute;
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: var(--mat-sys-primary);
      transform: translate(-50%, -50%);
    }

    .x {
      grid-row: 2;
      grid-column: 2;
      padding-top: 0.4rem;
    }

    .x.last {
      justify-self: end;
    }
  `,
})
export class BurndownChart {
  readonly points = input.required<readonly BurndownPoint[]>();

  /** The top of the scale: the most the sprint ever held, done or not. */
  protected readonly max = computed(() =>
    Math.max(1, ...this.points().map(p => p.remainingPoints + p.completedPoints))
  );

  protected readonly plots = computed<Plot[]>(() => {
    const points = this.points();
    const step = points.length > 1 ? 100 / (points.length - 1) : 0;
    return points.map((point, i) => ({
      x: step * i,
      y: 100 - (100 * point.remainingPoints) / this.max(),
      label: new Date(`${point.date}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' }),
      remaining: point.remainingPoints,
    }));
  });

  protected readonly line = computed(() =>
    this.plots().map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ')
  );

  protected readonly summary = computed(() => {
    const plots = this.plots();
    const first = plots[0];
    const last = plots[plots.length - 1];
    return `Burndown: ${first.remaining} points left on ${first.label}, ${last.remaining} on ${last.label}`;
  });
}
