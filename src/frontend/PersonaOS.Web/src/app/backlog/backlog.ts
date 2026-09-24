import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import { Confirm } from '../core/confirm';
import { DRAG_DEFAULTS } from '../core/drag-defaults';
import { BoardTabs } from '../board/board-tabs';
import { SprintForm } from '../board/sprint-form';
import { openTaskFromQuery } from '../board/task-dialog';
import { InlineCreate, NewTask } from '../shared/inline-create';

import {
  BoardColumn,
  BoardService,
  BoardTask,
  COLUMN_LABELS,
  PRIORITY_ICONS,
  PRIORITY_LABELS,
  PlanView,
  Priority,
  Sprint,
  SprintPlan,
  apiError,
  formatWhen,
  goalHue,
  sprintDayHasCome,
  withScopeConfirmation,
} from '../core/board.service';
import { Goal, GoalsService } from '../core/goals.service';

/** A section of the page: a sprint, or the backlog at the bottom. */
interface Group {
  key: string | null;
  sprint: Sprint | null;
  tasks: BoardTask[];
}

/**
 * The plan, the way a Jira backlog reads: the running sprint at the top, sprints to come under it,
 * and the backlog at the bottom. Work is dragged between them; sprints are created, started and
 * completed from here.
 */
@Component({
  selector: 'app-backlog',
  imports: [
    RouterLink,
    BoardTabs,
    DragDropModule,
    InlineCreate,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatProgressBarModule,
    SprintForm,
  ],
  providers: [DRAG_DEFAULTS],
  templateUrl: './backlog.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './backlog.scss',
})
export class Backlog implements OnInit {
  private readonly api = inject(BoardService);
  private readonly goalsApi = inject(GoalsService);
  private readonly confirm = inject(Confirm);

  protected readonly plan = signal<PlanView | null>(null);
  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  protected readonly collapsed = signal<Set<string>>(new Set());

  /** The section whose Create is open: "backlog" or a sprint key. */
  protected readonly addingTo = signal<string | null>(null);

  /** The sprint being created or edited in the sprint form. */
  protected readonly editingSprint = signal<Sprint | 'new' | null>(null);


  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly priorityIcons = PRIORITY_ICONS;
  protected readonly columnLabels = COLUMN_LABELS;

  constructor() {
    openTaskFromQuery(() => void this.reload());
  }

  async ngOnInit(): Promise<void> {
    await Promise.all([this.reload(), this.loadGoals()]);
  }

  protected async reload(): Promise<void> {
    try {
      this.plan.set(await this.api.plan());
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load the plan.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadGoals(): Promise<void> {
    try {
      this.goals.set((await this.goalsApi.getAll()).filter(g => g.status === 'active'));
    } catch {
      // The goals module may be switched off; tasks then simply have no goal to pick.
      this.goals.set([]);
    }
  }

  /** Sprints first, backlog last — the order the page reads in. */
  protected groups(): Group[] {
    const plan = this.plan();
    if (!plan) return [];
    return [
      ...plan.sprints.map(s => ({ key: s.sprint.key, sprint: s.sprint, tasks: s.tasks })),
      { key: null, sprint: null, tasks: plan.backlog },
    ];
  }

  protected groupId(group: Group): string {
    return group.key ?? 'backlog';
  }

  protected title(group: Group): string {
    if (!group.sprint) return 'Backlog';
    return group.sprint.name ? `${group.sprint.key} · ${group.sprint.name}` : group.sprint.key;
  }

  protected points(tasks: BoardTask[]): number {
    return tasks.reduce((sum, t) => sum + (t.points ?? 0), 0);
  }

  /** Points to do, in progress and done, for the three lozenges in a sprint's header. */
  protected statusPoints(tasks: BoardTask[]): { column: BoardColumn; points: number }[] {
    const columns: BoardColumn[] = ['todo', 'in_progress', 'done'];
    return columns.map(column => ({ column, points: this.points(tasks.filter(t => t.column === column)) }));
  }

  /** The statuses a task in this sprint can take: work only moves on once the sprint runs. */
  protected statusOptions(sprint: Sprint): BoardColumn[] {
    return sprint.status === 'active' ? ['todo', 'in_progress', 'done'] : ['todo'];
  }

  protected isCollapsed(group: Group): boolean {
    return this.collapsed().has(this.groupId(group));
  }

  protected toggle(group: Group): void {
    const id = this.groupId(group);
    this.collapsed.update(set => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  // ------------------------------------------------------------------ sprints

  protected newSprint(): void {
    this.editingSprint.set('new');
  }

  protected editSprint(sprint: Sprint): void {
    this.editingSprint.set(sprint);
  }

  protected async sprintSaved(): Promise<void> {
    this.editingSprint.set(null);
    await this.reload();
  }

  protected async startSprint(sprint: Sprint): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Start ${this.sprintLabel(sprint)}?`,
      message: `${sprint.totalPoints} points are committed when it starts. Adding work afterwards ` +
        'counts as a scope change.',
      confirmLabel: 'Start sprint',
    });
    if (!ok) return;
    await this.run(() => this.api.startSprint(sprint.key), 'Could not start that sprint.');
  }

  protected async completeSprint(sprint: Sprint): Promise<void> {
    const open = sprint.taskCount - sprint.doneTaskCount;
    const ok = await this.confirm.ask({
      title: `Complete ${this.sprintLabel(sprint)}?`,
      message: open === 0
        ? 'Everything in it is done.'
        : `${open} unfinished ${open === 1 ? 'task moves' : 'tasks move'} to the next planned ` +
          'sprint, or to the backlog when there is none.',
      confirmLabel: 'Complete sprint',
    });
    if (!ok) return;
    await this.run(() => this.api.completeSprint(sprint.key, {}), 'Could not complete that sprint.');
  }

  protected async deleteSprint(sprint: Sprint): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${this.sprintLabel(sprint)}?`,
      message: 'Its tasks go back to the backlog. This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await this.run(() => this.api.deleteSprint(sprint.key), 'Could not delete that sprint.');
  }

  protected sprintLabel(sprint: Sprint): string {
    return sprint.name ? `${sprint.key} “${sprint.name}”` : sprint.key;
  }

  // ------------------------------------------------------------------ tasks

  /** Creates a task in the section whose Create is open; see InlineCreate. */
  protected readonly createTask = async (task: NewTask): Promise<boolean> => {
    const groupId = this.addingTo();
    if (!groupId) return false;

    const draft = { ...task, sprintKey: groupId === 'backlog' ? null : groupId };
    const created = await this.run(
      () => withScopeConfirmation(ack => this.api.create(draft, ack)),
      'Could not add that task.'
    );
    return !!created;
  };

  /** A new status from the row's lozenge: the same move the board makes between columns. */
  protected async setColumn(task: BoardTask, column: BoardColumn): Promise<void> {
    if (column === task.column) return;
    await this.run(
      () => withScopeConfirmation(ack => this.api.move(task.key, column, task.sprintKey, null, ack)),
      'Could not change the status.'
    );
  }

  protected async moveToGroup(task: BoardTask, groupId: string, index: number | null = null): Promise<void> {
    const sprintKey = groupId === 'backlog' ? null : groupId;
    await this.run(
      () =>
        withScopeConfirmation(ack =>
          this.api.move(task.key, sprintKey === null ? 'backlog' : 'todo', sprintKey, index, ack)
        ),
      'Could not move that task.'
    );
  }

  protected async setPoints(task: BoardTask, points: number | null): Promise<void> {
    await this.run(
      () => this.api.update(task.key, { points, clearPoints: points === null }),
      'Could not set the points.'
    );
  }

  protected async setPriority(task: BoardTask, priority: Priority): Promise<void> {
    await this.run(() => this.api.update(task.key, { priority }), 'Could not set the priority.');
  }

  // ------------------------------------------------------------------ drag and drop

  /**
   * A row was dropped into a sprint or the backlog. CDK reports where it landed, and works with
   * touch — the old HTML5 drag did not, so planning on a phone browser was impossible.
   */
  protected async onDrop(event: CdkDragDrop<Group>): Promise<void> {
    const task = event.item.data as BoardTask;
    const group = event.container.data;
    if (event.previousContainer === event.container && event.previousIndex === event.currentIndex) return;

    const others = group.tasks.filter(t => t.key !== task.key);
    const index = Math.min(event.currentIndex, others.length);
    await this.moveToGroup(task, this.groupId(group), index);
  }

  // ------------------------------------------------------------------ helpers

  protected hue(goalKey: string | null): number {
    return goalHue(goalKey);
  }

  protected when(value: string): string {
    return formatWhen(value);
  }

  protected dayHasCome(sprint: Sprint): boolean {
    return sprintDayHasCome(sprint);
  }

  /** Why Start is greyed out before a sprint's first day; nothing once it can start. */
  protected startHint(sprint: Sprint): string {
    return this.dayHasCome(sprint)
      ? ''
      : `Starts ${formatWhen(sprint.startsAtUtc)}. Move its start to today to begin it now.`;
  }

  protected canStart(sprint: Sprint): boolean {
    return sprint.status === 'planned' && !this.plan()?.sprints.some(s => s.sprint.status === 'active');
  }

  protected moveTargets(task: BoardTask): { id: string; title: string }[] {
    return this.groups()
      .filter(g => this.groupId(g) !== (task.sprintKey ?? 'backlog'))
      .map(g => ({ id: this.groupId(g), title: this.title(g) }));
  }

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
