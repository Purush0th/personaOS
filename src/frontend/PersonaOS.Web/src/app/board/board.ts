import { CdkDragDrop, CdkDragMove, DragDropModule } from '@angular/cdk/drag-drop';
import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';

import { Confirm } from '../core/confirm';
import { DRAG_DEFAULTS } from '../core/drag-defaults';

import {
  BoardColumn,
  BoardService,
  BoardTask,
  BoardView,
  COLUMN_LABELS,
  Sprint,
  apiError,
  formatWhen,
  goalHue,
  withScopeConfirmation,
} from '../core/board.service';
import { openTaskFromQuery } from './task-dialog';

interface ColumnDef {
  id: BoardColumn;
  title: string;
  tasks: BoardTask[];
}

/**
 * The board is the running sprint and nothing else: This week, In progress, Done. The backlog and
 * the sprints to come live in the board's other view, so this one stays what you look at daily.
 *
 * It behaves like the owner's Jira board. A card drags from anywhere on it; within its column it
 * reorders, and over another column that whole column lights up as the drop zone while the card
 * stays, faded, where it was. A click opens the task in a dialog over the board, and every column
 * has its own Create.
 */
@Component({
  selector: 'app-board',
  imports: [
    RouterLink,
    DragDropModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressBarModule,
    MatTabsModule,
  ],
  providers: [DRAG_DEFAULTS],
  templateUrl: './board.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './board.scss',
})
export class Board implements OnInit {
  private readonly api = inject(BoardService);
  private readonly confirm = inject(Confirm);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  protected readonly board = signal<BoardView | null>(null);
  protected readonly loading = signal(true);

  /**
   * Column being added to, with the draft's title and points. The title is a signal written from
   * the field, not an ngModel: clearing it after a create is '' to '' as far as ngModel can tell,
   * so the field would keep the old text.
   */
  protected readonly addingTo = signal<BoardColumn | null>(null);
  protected readonly draftTitle = signal('');
  draftPoints: number | null = null;
  private readonly draftField = viewChild<ElementRef<HTMLTextAreaElement>>('draftField');

  /** The card in the air, and the other column it would move to if dropped now. */
  protected readonly dragging = signal<BoardTask | null>(null);
  protected readonly dropTarget = signal<ColumnDef | null>(null);

  protected readonly columns = computed<ColumnDef[]>(() => {
    const b = this.board();
    if (!b?.sprint) return [];
    return [
      { id: 'todo', title: COLUMN_LABELS.todo, tasks: b.todo },
      { id: 'in_progress', title: COLUMN_LABELS.in_progress, tasks: b.inProgress },
      { id: 'done', title: COLUMN_LABELS.done, tasks: b.done },
    ];
  });

  constructor() {
    openTaskFromQuery(() => void this.reload());
  }

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    try {
      this.board.set(await this.api.board());
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load the board.');
    } finally {
      this.loading.set(false);
    }
  }

  protected sprintTitle(sprint: Sprint): string {
    return sprint.name ? `${sprint.key} · ${sprint.name}` : sprint.key;
  }

  protected async completeSprint(): Promise<void> {
    const sprint = this.board()?.sprint;
    if (!sprint) return;
    const open = sprint.taskCount - sprint.doneTaskCount;
    const ok = await this.confirm.ask({
      title: `Complete ${this.sprintTitle(sprint)}?`,
      message: open === 0
        ? 'Everything in it is done.'
        : `${open} unfinished ${open === 1 ? 'task moves' : 'tasks move'} to the next planned ` +
          'sprint, or to the backlog when there is none.',
      confirmLabel: 'Complete sprint',
    });
    if (!ok) return;

    await this.run(() => this.api.completeSprint(sprint.key, {}), 'Could not complete the sprint.');
  }

  // ------------------------------------------------------------------ adding

  protected startAdd(column: BoardColumn): void {
    this.addingTo.set(column);
    this.clearDraft();
  }

  /** Enter creates; Shift+Enter is left alone, and Esc gives up. */
  protected onDraftKey(event: KeyboardEvent, column: BoardColumn): void {
    if (event.key === 'Escape') {
      this.addingTo.set(null);
    } else if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.add(column);
    }
  }

  /** Creates the draft in that column and, like Jira, leaves the field open for the next one. */
  protected async add(column: BoardColumn): Promise<void> {
    const sprint = this.board()?.sprint;
    const title = this.draftTitle().trim();
    if (!sprint || !title) return;

    const draft = { title, points: this.draftPoints, sprintKey: sprint.key, column };
    const created = await this.run(
      () => withScopeConfirmation(ack => this.api.create(draft, ack)),
      'Could not add that task.'
    );
    if (created) this.clearDraft();
  }

  /** Empties the draft and puts the cursor back in it, once the field is on screen. */
  private clearDraft(): void {
    this.draftTitle.set('');
    this.draftPoints = null;
    afterNextRender(() => {
      const field = this.draftField()?.nativeElement;
      if (!field) return;
      field.value = '';
      field.focus();
    }, { injector: this.injector });
  }

  // ------------------------------------------------------------------ moving

  protected async moveTo(task: BoardTask, column: BoardColumn, index: number | null = null): Promise<void> {
    const sprintKey = column === 'backlog' ? null : this.board()?.sprint?.key ?? null;
    await this.run(
      () => withScopeConfirmation(ack => this.api.move(task.key, column, sprintKey, index, ack)),
      'Could not move that task.'
    );
  }

  /**
   * Tracks which other column the pointer is over. The columns are not connected drop lists, so
   * the card's placeholder stays in its own column (faded, as Jira shows it) instead of jumping
   * into whichever column it passes.
   */
  protected onDragMoved(event: CdkDragMove<BoardTask>): void {
    const { x, y } = event.pointerPosition;
    const id = this.document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-column]')?.dataset['column'];
    const target = id && id !== event.source.data.column
      ? this.columns().find(c => c.id === id) ?? null
      : null;
    if (target !== this.dropTarget()) this.dropTarget.set(target);
  }

  /**
   * A card was let go. Over another column it moves there, to the end, the way Jira moves an issue
   * dropped on a column; within its own column it takes the place it was dropped in.
   */
  protected async onDrop(event: CdkDragDrop<ColumnDef, ColumnDef, BoardTask>): Promise<void> {
    const task = event.item.data;
    const target = this.dropTarget();
    this.dragging.set(null);
    this.dropTarget.set(null);

    if (target) {
      await this.moveTo(task, target.id);
    } else if (event.isPointerOverContainer && event.previousIndex !== event.currentIndex) {
      await this.moveTo(task, task.column, event.currentIndex);
    }
  }

  // ------------------------------------------------------------------ display helpers

  protected columnTitle(id: BoardColumn): string {
    return COLUMN_LABELS[id];
  }

  protected columnPoints(column: ColumnDef): number {
    return column.tasks.reduce((sum, t) => sum + (t.points ?? 0), 0);
  }

  protected overWip(column: ColumnDef): boolean {
    const b = this.board();
    return !!b && column.id === 'in_progress' && column.tasks.length > b.wipLimit;
  }

  protected moveTargets(task: BoardTask): { id: BoardColumn; title: string }[] {
    return [
      ...this.columns().filter(c => c.id !== task.column).map(c => ({ id: c.id, title: c.title })),
      { id: 'backlog' as BoardColumn, title: 'Backlog' },
    ];
  }

  protected hue(goalKey: string | null): number {
    return goalHue(goalKey);
  }

  protected when(value: string): string {
    return formatWhen(value);
  }

  /**
   * Runs a change and reloads. Resolves to undefined when it failed (the error is shown), and to
   * the result — null when the user declined a scope change — otherwise.
   */
  private async run<T>(action: () => Promise<T>, fallback: string): Promise<T | undefined> {
    try {
      const result = await action();
      await this.reload();
      return result;
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? fallback);
      return undefined;
    }
  }
}
