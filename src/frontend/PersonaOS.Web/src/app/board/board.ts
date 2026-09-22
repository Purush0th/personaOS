import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';

import { Confirm } from '../core/confirm';

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
 * the sprints to come live in the board's other view, so this one stays what you look at daily.
 */
@Component({
  selector: 'app-board',
  imports: [
    FormsModule,
    RouterLink,
    DragDropModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTabsModule,
  ],
  templateUrl: './board.html',
  styleUrl: './board.scss',
})
export class Board implements OnInit {
  private readonly api = inject(BoardService);
  private readonly confirm = inject(Confirm);

  protected readonly board = signal<BoardView | null>(null);
  protected readonly loading = signal(true);

  /** Column being added to, with the draft's title. */
  protected readonly addingTo = signal<BoardColumn | null>(null);
  draftTitle = '';
  draftPoints: number | null = null;


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

  /**
   * A card was dropped. CDK gives the column it landed in and where, which is all the server
   * needs; the previous hand-rolled HTML5 drag did the same but never fired on a touchscreen.
   */
  protected async onDrop(event: CdkDragDrop<ColumnDef>): Promise<void> {
    const task = event.item.data as BoardTask;
    const target = event.container.data;
    const movedWithin = event.previousContainer === event.container;
    if (movedWithin && event.previousIndex === event.currentIndex) return;

    // The server places the task among the column's other tasks, so count without it.
    const others = target.tasks.filter(t => t.key !== task.key);
    const index = Math.min(event.currentIndex, others.length);

    await this.moveTo(task, target.id, index);
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
