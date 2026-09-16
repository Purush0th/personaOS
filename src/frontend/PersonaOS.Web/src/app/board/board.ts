import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

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

interface ColumnDef {
  id: BoardColumn;
  title: string;
  tasks: BoardTask[];
}

/**
 * The board is the running sprint and nothing else: This week, In progress, Done. The backlog and
 * the sprints to come live on the Backlog page, so the board stays what you look at during the week.
 */
@Component({
  selector: 'app-board',
  imports: [FormsModule, RouterLink],
  templateUrl: './board.html',
  styleUrl: './board.scss',
})
export class Board implements OnInit {
  private readonly api = inject(BoardService);

  protected readonly board = signal<BoardView | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** Column being added to, with the draft's title. */
  protected readonly addingTo = signal<BoardColumn | null>(null);
  draftTitle = '';
  draftPoints: number | null = null;

  protected readonly draggingKey = signal<string | null>(null);
  protected readonly dropColumn = signal<BoardColumn | null>(null);

  protected readonly columns = computed<ColumnDef[]>(() => {
    const b = this.board();
    if (!b?.sprint) return [];
    return [
      { id: 'todo', title: COLUMN_LABELS.todo, tasks: b.todo },
      { id: 'in_progress', title: COLUMN_LABELS.in_progress, tasks: b.inProgress },
      { id: 'done', title: COLUMN_LABELS.done, tasks: b.done },
    ];
  });

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    try {
      this.board.set(await this.api.board());
      this.error.set(null);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not load the board.');
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
    const question = open === 0
      ? `Complete ${this.sprintTitle(sprint)}?`
      : `Complete ${this.sprintTitle(sprint)}? ${open} unfinished ${open === 1 ? 'task moves' : 'tasks move'} ` +
        'to the next planned sprint, or to the backlog when there is none.';
    if (!confirm(question)) return;

    await this.run(() => this.api.completeSprint(sprint.key, {}), 'Could not complete the sprint.');
  }

  // ------------------------------------------------------------------ adding

  protected startAdd(column: BoardColumn): void {
    this.addingTo.set(column);
    this.draftTitle = '';
    this.draftPoints = null;
  }

  protected async add(): Promise<void> {
    const sprint = this.board()?.sprint;
    const title = this.draftTitle.trim();
    if (!sprint || !title) return;

    const created = await this.run(
      () =>
        withScopeConfirmation(ack =>
          this.api.create({ title, points: this.draftPoints, sprintKey: sprint.key }, ack)
        ),
      'Could not add that task.'
    );
    if (created) {
      this.draftTitle = '';
      this.draftPoints = null;
    }
  }

  // ------------------------------------------------------------------ moving

  protected async moveTo(task: BoardTask, column: BoardColumn, index: number | null = null): Promise<void> {
    const sprintKey = column === 'backlog' ? null : this.board()?.sprint?.key ?? null;
    await this.run(
      () => withScopeConfirmation(ack => this.api.move(task.key, column, sprintKey, index, ack)),
      'Could not move that task.'
    );
  }

  protected onDragStart(event: DragEvent, task: BoardTask): void {
    this.draggingKey.set(task.key);
    event.dataTransfer?.setData('text/plain', task.key);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  protected onDragEnd(): void {
    this.draggingKey.set(null);
    this.dropColumn.set(null);
  }

  protected onDragOver(event: DragEvent, column: BoardColumn): void {
    if (this.draggingKey() === null) return;
    event.preventDefault();
    this.dropColumn.set(column);
  }

  /** Dropped on a card: lands just before it. Dropped on the column: lands at the end. */
  protected async onDrop(event: DragEvent, column: ColumnDef, before: BoardTask | null): Promise<void> {
    event.preventDefault();
    event.stopPropagation();
    const key = this.draggingKey();
    this.onDragEnd();
    if (key === null) return;

    const task = this.findTask(key);
    if (!task || before?.key === key) return;

    // The server places the task among the column's other tasks, so count without it.
    const others = column.tasks.filter(t => t.key !== key);
    const index = before ? others.findIndex(t => t.key === before.key) : others.length;
    if (task.column === column.id && column.tasks.indexOf(task) === index) return;

    await this.moveTo(task, column.id, index);
  }

  // ------------------------------------------------------------------ display helpers

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

  private findTask(key: string): BoardTask | undefined {
    const b = this.board();
    return b ? [...b.todo, ...b.inProgress, ...b.done].find(t => t.key === key) : undefined;
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
      this.error.set(apiError(e).message ?? fallback);
      return undefined;
    }
  }
}
