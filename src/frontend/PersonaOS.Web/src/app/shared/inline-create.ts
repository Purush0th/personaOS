import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';

/** What the field collects: the rest of a task is set later, on the task itself. */
export interface NewTask {
  title: string;
  points: number | null;
}

/**
 * Jira's inline create, shared by the board's columns and the backlog's sections: type a title,
 * Enter creates it, and the field stays open, empty, for the next one; Esc gives up.
 *
 * `save` does the work and resolves to whether the task was created, so a failure (or a declined
 * scope change) keeps what was typed. The title is read from the field rather than bound with
 * ngModel, which cannot tell that '' after a create is a change and would leave the old text.
 */
@Component({
  selector: 'app-inline-create',
  imports: [MatButtonModule, MatMenuModule],
  template: `
    <textarea
      #field
      [rows]="layout() === 'card' ? 2 : 1"
      placeholder="What needs to be done?"
      aria-label="New task title"
      (input)="title.set(field.value)"
      (keydown)="onKey($event)"
    ></textarea>
    <div class="actions">
      <button
        type="button"
        class="points-pill"
        [class.none]="points() === null"
        [matMenuTriggerFor]="pointsMenu"
        [attr.aria-label]="'Points: ' + (points() ?? 'not estimated')"
      >{{ points() ?? '?' }}</button>
      <span class="spacer"></span>
      <button mat-button type="button" (click)="cancelled.emit()">Cancel</button>
      <button mat-flat-button type="button" [disabled]="!title().trim() || saving()" (click)="submit()">
        Create
      </button>
    </div>

    <mat-menu #pointsMenu="matMenu">
      <button mat-menu-item (click)="points.set(null)">Not estimated</button>
      @for (p of allowedPoints(); track p) {
        <button mat-menu-item (click)="points.set(p)">{{ p }} {{ p === 1 ? 'point' : 'points' }}</button>
      }
    </mat-menu>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'layout()' },
  styles: `
    :host {
      display: flex;
      gap: 0.25rem;
      padding: 0.35rem 0.35rem 0.35rem 0.75rem;
      border-radius: var(--mat-sys-corner-small);
      background: var(--work-item);
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: -2px;
    }

    :host(.card) {
      flex-direction: column;
      padding-top: 0.6rem;
    }

    :host(.row) {
      align-items: center;
    }

    textarea {
      flex: 1;
      min-width: 0;
      resize: none;
      border: none;
      outline: none;
      background: none;
      color: inherit;
      font: var(--mat-sys-body-medium);
    }

    .actions {
      display: flex;
      align-items: center;
      gap: 0.25rem;
    }

    .spacer {
      flex: 1;
    }

    :host(.row) .spacer {
      display: none;
    }
  `,
})
export class InlineCreate {
  readonly allowedPoints = input.required<readonly number[]>();
  /** Stacked in a board column, or one line in a backlog section. */
  readonly layout = input<'card' | 'row'>('card');
  readonly save = input.required<(task: NewTask) => Promise<boolean>>();
  readonly cancelled = output<void>();

  protected readonly title = signal('');
  protected readonly points = signal<number | null>(null);
  protected readonly saving = signal(false);
  private readonly field = viewChild.required<ElementRef<HTMLTextAreaElement>>('field');

  constructor() {
    afterNextRender(() => this.field().nativeElement.focus());
  }

  protected onKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.cancelled.emit();
    } else if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.submit();
    }
  }

  protected async submit(): Promise<void> {
    const title = this.title().trim();
    if (!title || this.saving()) return;

    this.saving.set(true);
    try {
      if (!(await this.save()({ title, points: this.points() }))) return;
      const field = this.field().nativeElement;
      field.value = '';
      this.title.set('');
      this.points.set(null);
      field.focus();
    } finally {
      this.saving.set(false);
    }
  }
}
