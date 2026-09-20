import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import {
  BoardService,
  BoardTask,
  COLUMN_LABELS,
  PRIORITY_LABELS,
  PlanView,
  Priority,
  Sprint,
  SprintPlan,
  apiError,
  formatWhen,
  goalHue,
  toLocalInput,
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
  imports: [FormsModule, RouterLink],
  templateUrl: './backlog.html',
  styleUrl: './backlog.scss',
})
export class Backlog implements OnInit {
  private readonly api = inject(BoardService);
  private readonly goalsApi = inject(GoalsService);

  protected readonly plan = signal<PlanView | null>(null);
  protected readonly goals = signal<Goal[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly collapsed = signal<Set<string>>(new Set());

  /** Group being added to ("backlog" or a sprint key), with the draft's fields. */
  protected readonly addingTo = signal<string | null>(null);
  draftTitle = '';
  draftPoints: number | null = null;
  draftGoalId: number | null = null;

  /** The sprint being created or edited, as form fields. */
  protected readonly editingSprint = signal<Sprint | 'new' | null>(null);
  sprintName = '';
  sprintStart = '';
  sprintEnd = '';

  protected readonly draggingKey = signal<string | null>(null);
  protected readonly dropGroup = signal<string | null>(null);

  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly columnLabels = COLUMN_LABELS;

  async ngOnInit(): Promise<void> {
    await Promise.all([this.reload(), this.loadGoals()]);
  }

  protected async reload(): Promise<void> {
    try {
      this.plan.set(await this.api.plan());
      this.error.set(null);
    } catch (e: unknown) {
      this.error.set(apiError(e).message ?? 'Could not load the plan.');
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
    this.sprintName = '';
    this.sprintStart = '';
    this.sprintEnd = '';
  }

  protected editSprint(sprint: Sprint): void {
    this.editingSprint.set(sprint);
    this.sprintName = sprint.name ?? '';
    this.sprintStart = toLocalInput(sprint.startsAtUtc);
    this.sprintEnd = toLocalInput(sprint.endsAtUtc);
  }

  protected async saveSprint(): Promise<void> {
    const editing = this.editingSprint();
    if (!editing) return;

    const saved = await this.run(
      () =>
        editing === 'new'
          ? this.api.createSprint({
              name: this.sprintName.trim() || null,
              startsAtLocal: this.sprintStart || null,
              endsAtLocal: this.sprintEnd || null,
            })
          : this.api.updateSprint(editing.key, {
              name: this.sprintName.trim() || null,
              clearName: !this.sprintName.trim(),
              startsAtLocal: this.sprintStart || undefined,
              endsAtLocal: this.sprintEnd || undefined,
            }),
      'Could not save that sprint.'
    );
    if (saved) this.editingSprint.set(null);
  }

  protected async startSprint(sprint: Sprint): Promise<void> {
    if (!confirm(`Start ${this.sprintLabel(sprint)} with ${sprint.totalPoints} points?`)) return;
    await this.run(() => this.api.startSprint(sprint.key), 'Could not start that sprint.');
  }

  protected async completeSprint(sprint: Sprint): Promise<void> {
    const open = sprint.taskCount - sprint.doneTaskCount;
    const question = open === 0
      ? `Complete ${this.sprintLabel(sprint)}?`
      : `Complete ${this.sprintLabel(sprint)}? ${open} unfinished ${open === 1 ? 'task moves' : 'tasks move'} ` +
        'to the next planned sprint, or to the backlog when there is none.';
    if (!confirm(question)) return;
    await this.run(() => this.api.completeSprint(sprint.key, {}), 'Could not complete that sprint.');
  }

  protected async deleteSprint(sprint: Sprint): Promise<void> {
    if (!confirm(`Delete ${this.sprintLabel(sprint)}? Its tasks go back to the backlog.`)) return;
    await this.run(() => this.api.deleteSprint(sprint.key), 'Could not delete that sprint.');
  }

  protected sprintLabel(sprint: Sprint): string {
    return sprint.name ? `${sprint.key} “${sprint.name}”` : sprint.key;
  }

  // ------------------------------------------------------------------ tasks

  protected startAdd(group: Group): void {
    this.addingTo.set(this.groupId(group));
    this.draftTitle = '';
    this.draftPoints = null;
    this.draftGoalId = null;
  }

  protected async add(group: Group): Promise<void> {
    const title = this.draftTitle.trim();
    if (!title) return;

    const created = await this.run(
      () =>
        withScopeConfirmation(ack =>
          this.api.create(
            { title, points: this.draftPoints, goalId: this.draftGoalId, sprintKey: group.key },
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

  protected onDragStart(event: DragEvent, task: BoardTask): void {
    this.draggingKey.set(task.key);
    event.dataTransfer?.setData('text/plain', task.key);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  protected onDragEnd(): void {
    this.draggingKey.set(null);
    this.dropGroup.set(null);
  }

  protected onDragOver(event: DragEvent, groupId: string): void {
    if (this.draggingKey() === null) return;
    event.preventDefault();
    this.dropGroup.set(groupId);
  }

  protected async onDrop(event: DragEvent, group: Group, before: BoardTask | null): Promise<void> {
    event.preventDefault();
    event.stopPropagation();
    const key = this.draggingKey();
    this.onDragEnd();
    if (key === null || before?.key === key) return;

    const task = this.findTask(key);
    if (!task) return;

    const others = group.tasks.filter(t => t.key !== key);
    const index = before ? others.findIndex(t => t.key === before.key) : others.length;
    await this.moveToGroup(task, this.groupId(group), index);
  }

  // ------------------------------------------------------------------ helpers

  protected hue(goalKey: string | null): number {
    return goalHue(goalKey);
  }

  protected when(value: string): string {
    return formatWhen(value);
  }

  protected canStart(sprint: Sprint): boolean {
    return sprint.status === 'planned' && !this.plan()?.sprints.some(s => s.sprint.status === 'active');
  }

  protected moveTargets(task: BoardTask): { id: string; title: string }[] {
    return this.groups()
      .filter(g => this.groupId(g) !== (task.sprintKey ?? 'backlog'))
      .map(g => ({ id: this.groupId(g), title: this.title(g) }));
  }

  private findTask(key: string): BoardTask | undefined {
    return this.groups().flatMap(g => g.tasks).find(t => t.key === key);
  }

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
