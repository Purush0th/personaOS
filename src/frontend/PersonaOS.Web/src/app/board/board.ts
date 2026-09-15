import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  BoardColumn,
  BoardService,
  BoardTask,
  BoardView,
  SprintReport,
  SprintView,
  apiError,
  goalHue,
  withScopeConfirmation,
} from '../core/board.service';
import { Goal, GoalsService } from '../core/goals.service';

interface ColumnDef {
  id: BoardColumn;
  title: string;
  tasks: BoardTask[];
}

/** Server timestamps are UTC but may arrive without a zone suffix. */
function parseUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);
}

@Component({
  selector: 'app-board',
  imports: [FormsModule],
  templateUrl: './board.html',
  styleUrl: './board.scss',
})
export class Board implements OnInit {
  private readonly boardApi = inject(BoardService);
  private readonly goalsApi = inject(GoalsService);

  protected readonly view = signal<SprintView>('current');
  protected readonly board = signal<BoardView | null>(null);
  protected readonly goals = signal<Goal[]>([]);
  protected readonly report = signal<SprintReport | null>(null);
  protected readonly showReport = signal(false);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** Column being added to, with the draft's fields. */
  protected readonly addingTo = signal<BoardColumn | null>(null);
  draftTitle = '';
  draftPoints: number | null = null;
  draftGoalId: number | null = null;

  /** The task open in the edit dialog, and its editable copy. */
  protected readonly editing = signal<BoardTask | null>(null);
  editTitle = '';
  editDescription = '';
  editPoints: number | null = null;
  editGoalId: number | null = null;

  protected readonly draggingId = signal<number | null>(null);
  protected readonly dropColumn = signal<BoardColumn | null>(null);

  protected readonly columns = computed<ColumnDef[]>(() => {
    const b = this.board();
    if (!b) return [];
    const all: ColumnDef[] = [
      { id: 'backlog', title: 'Backlog', tasks: b.backlog },
      { id: 'todo', title: 'This week', tasks: b.todo },
      { id: 'in_progress', title: 'In progress', tasks: b.inProgress },
      { id: 'done', title: 'Done', tasks: b.done },
    ];
    // A sprint that has not started only holds this week's work (plus anything carried over
    // while still in progress).
    return b.sprint.status === 'active'
      ? all
      : all.filter(c => c.id === 'backlog' || c.id === 'todo' || c.tasks.length > 0);
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.reload(), this.loadGoals()]);
  }

  protected async reload(): Promise<void> {
    try {
      this.board.set(await this.boardApi.get(this.view()));
      if (this.showReport()) this.report.set(await this.boardApi.report());
      this.error.set(null);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not load the board.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadGoals(): Promise<void> {
    try {
      this.goals.set((await this.goalsApi.getAll()).filter(g => g.status === 'active'));
    } catch {
      // The goals module may be switched off; tasks simply have no goal to pick.
      this.goals.set([]);
    }
  }

  protected async switchView(view: SprintView): Promise<void> {
    if (this.view() === view) return;
    this.view.set(view);
    this.addingTo.set(null);
    await this.reload();
  }

  protected async toggleReport(): Promise<void> {
    this.showReport.update(v => !v);
    if (this.showReport()) {
      try {
        this.report.set(await this.boardApi.report());
      } catch {
        this.error.set('Could not load the sprint report.');
      }
    }
  }

  protected async startSprint(): Promise<void> {
    const b = this.board();
    if (!b || !confirm(`Start sprint ${b.sprint.number} now with ${b.sprint.totalPoints} points?`)) return;
    await this.run(() => this.boardApi.startSprint(), 'Could not start the sprint.');
  }

  // ------------------------------------------------------------------ adding

  protected startAdd(column: BoardColumn): void {
    this.addingTo.set(column);
    this.draftTitle = '';
    this.draftPoints = null;
    this.draftGoalId = null;
  }

  protected async add(): Promise<void> {
    const column = this.addingTo();
    const title = this.draftTitle.trim();
    if (!column || !title) return;

    const destination = column === 'backlog' ? 'backlog' : this.view();
    const created = await this.run(
      () =>
        withScopeConfirmation(ack =>
          this.boardApi.create(
            { title, points: this.draftPoints, goalId: this.draftGoalId, destination },
            ack
          )
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
    const sprint = column === 'backlog' ? null : this.view();
    await this.run(
      () => withScopeConfirmation(ack => this.boardApi.move(task.id, column, sprint, index, ack)),
      'Could not move that task.'
    );
  }

  protected onDragStart(event: DragEvent, task: BoardTask): void {
    this.draggingId.set(task.id);
    event.dataTransfer?.setData('text/plain', String(task.id));
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  protected onDragEnd(): void {
    this.draggingId.set(null);
    this.dropColumn.set(null);
  }

  protected onDragOver(event: DragEvent, column: BoardColumn): void {
    if (this.draggingId() === null) return;
    event.preventDefault();
    this.dropColumn.set(column);
  }

  /** Dropped on a card: lands just before it. Dropped on the column: lands at the end. */
  protected async onDrop(event: DragEvent, column: ColumnDef, before: BoardTask | null): Promise<void> {
    event.preventDefault();
    event.stopPropagation();
    const id = this.draggingId();
    this.onDragEnd();
    if (id === null) return;

    const task = this.findTask(id);
    if (!task || (before && before.id === id)) return;

    // The server places the task among the column's other tasks, so count without it.
    const others = column.tasks.filter(t => t.id !== id);
    const index = before ? others.findIndex(t => t.id === before.id) : others.length;
    if (task.column === column.id && column.tasks.indexOf(task) === index) return;

    await this.moveTo(task, column.id, index);
  }

  // ------------------------------------------------------------------ editing

  protected openEdit(task: BoardTask): void {
    this.editing.set(task);
    this.editTitle = task.title;
    this.editDescription = task.description ?? '';
    this.editPoints = task.points;
    this.editGoalId = task.goalId;
  }

  protected closeEdit(): void {
    this.editing.set(null);
  }

  protected async saveEdit(): Promise<void> {
    const task = this.editing();
    if (!task || !this.editTitle.trim()) return;
    const saved = await this.run(
      () =>
        this.boardApi.update(task.id, {
          title: this.editTitle.trim(),
          description: this.editDescription,
          points: this.editPoints,
          clearPoints: this.editPoints === null,
          goalId: this.editGoalId,
          clearGoal: this.editGoalId === null,
        }),
      'Could not save that task.'
    );
    if (saved !== undefined) this.closeEdit();
  }

  protected async deleteEditing(): Promise<void> {
    const task = this.editing();
    if (!task || !confirm(`Delete ${task.key} “${task.title}”? Its number will be reused.`)) return;
    const deleted = await this.run(() => this.boardApi.delete(task.id), 'Could not delete that task.');
    if (deleted !== undefined) this.closeEdit();
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
    return this.columns()
      .filter(c => c.id !== task.column)
      .filter(c => this.board()?.sprint.status === 'active' || c.id === 'backlog' || c.id === 'todo')
      .map(c => ({ id: c.id, title: c.title }));
  }

  protected hue(goalKey: string | null): number {
    return goalHue(goalKey);
  }

  protected when(value: string): string {
    return parseUtc(value).toLocaleString(undefined, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected day(value: string): string {
    return parseUtc(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }

  private findTask(id: number): BoardTask | undefined {
    const b = this.board();
    return b ? [...b.backlog, ...b.todo, ...b.inProgress, ...b.done].find(t => t.id === id) : undefined;
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
