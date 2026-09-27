import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { BoardColumn } from './board.service';
import { GoalPeriod } from './goal-calendar';

export interface GoalTaskSummary {
  id: number;
  key: string;
  title: string;
  points: number | null;
  column: BoardColumn;
  sprintNumber: number | null;
}

export interface GoalChildSummary {
  id: number;
  key: string;
  title: string;
  periodType: GoalPeriod;
  slot: string;
  periodStart: string;
  periodEnd: string;
  status: 'active' | 'completed';
  effectiveProgress: number;
}

/**
 * A goal: year, quarter or month, nested year → quarter → month or standalone. Only monthly
 * goals hold tasks. Its dates follow its calendar slot.
 */
export interface Goal {
  id: number;
  key: string;
  title: string;
  description: string | null;
  periodType: GoalPeriod;
  /** "2026", "Q4 2026" or "Oct 2026". */
  slot: string;
  periodStart: string;
  /** The day the goal is due, inclusive: the end of its slot. */
  periodEnd: string;
  parentId: number | null;
  parentKey: string | null;
  parentTitle: string | null;
  status: 'active' | 'completed';
  priority: 'highest' | 'high' | 'medium' | 'low' | 'lowest';
  commentCount: number;
  attachmentCount: number;
  /** Manually tracked; used only while the goal has no tasks and no child goals. */
  progress: number;
  /** Completed 100; else the average of its child goals; else done tasks ÷ tasks; else manual. */
  effectiveProgress: number;
  taskCount: number;
  doneTaskCount: number;
  childCount: number;
  completedChildCount: number;
  /** Why it cannot be completed yet (open tasks, active child goals), or null. */
  completeProblem: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  tasks: GoalTaskSummary[];
  children: GoalChildSummary[];
}

export interface NewGoal {
  title: string;
  periodType: GoalPeriod;
  year: number;
  quarter?: number | null;
  month?: number | null;
  /** A year goal's start; today when omitted. */
  periodStart?: string | null;
  parentId?: number | null;
  description?: string | null;
}

export type GoalTaskAction = 'keep' | 'delete' | 'reassign';

@Injectable({ providedIn: 'root' })
export class GoalsService {
  private readonly http = inject(HttpClient);

  /** By its key, e.g. GOAL-3 — what the goal's own page loads from the URL. */
  getByKey(key: string): Promise<Goal> {
    return firstValueFrom(this.http.get<Goal>(`/api/goals/${key}`));
  }

  getAll(): Promise<Goal[]> {
    return firstValueFrom(this.http.get<Goal[]>('/api/goals'));
  }

  create(goal: NewGoal): Promise<Goal> {
    return firstValueFrom(this.http.post<Goal>('/api/goals', goal));
  }

  update(id: number, changes: {
    title?: string;
    description?: string;
    clearDescription?: boolean;
    priority?: string;
  }): Promise<Goal> {
    return firstValueFrom(this.http.put<Goal>(`/api/goals/${id}`, changes));
  }

  updateStatus(id: number, status: 'active' | 'completed'): Promise<Goal> {
    return firstValueFrom(this.http.put<Goal>(`/api/goals/${id}/status`, { status }));
  }

  updateProgress(id: number, progress: number): Promise<Goal> {
    return firstValueFrom(this.http.put<Goal>(`/api/goals/${id}`, { progress }));
  }

  /** Under another parent in the slot given, or detached (`parentId` null) keeping its dates. */
  move(id: number, move: { parentId: number | null; year?: number | null; quarter?: number | null; month?: number | null }): Promise<Goal> {
    return firstValueFrom(this.http.put<Goal>(`/api/goals/${id}/parent`, move));
  }

  /** Deletes a goal; its tasks are kept, deleted, or each reassigned (task key → goal key or null). */
  delete(id: number, taskAction: GoalTaskAction = 'keep', reassign: Record<string, string | null> | null = null): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/goals/${id}`, { body: { taskAction, reassign } }));
  }
}

/** Parents before their children, each child after its parent: the order the goals page shows. */
export function inTreeOrder(goals: Goal[]): { goal: Goal; depth: number }[] {
  const ids = new Set(goals.map(g => g.id));
  const children = new Map<number, Goal[]>();
  for (const goal of goals) {
    if (goal.parentId !== null && ids.has(goal.parentId)) {
      children.set(goal.parentId, [...(children.get(goal.parentId) ?? []), goal]);
    }
  }
  const out: { goal: Goal; depth: number }[] = [];
  const add = (goal: Goal, depth: number) => {
    out.push({ goal, depth });
    for (const child of children.get(goal.id) ?? []) add(child, depth + 1);
  };
  for (const goal of goals) if (goal.parentId === null || !ids.has(goal.parentId)) add(goal, 0);
  return out;
}

/** A goal whose progress is set by hand: open, with no tasks and no child goals. */
export function setsProgressByHand(goal: Goal): boolean {
  return goal.status === 'active' && goal.taskCount === 0 && goal.childCount === 0;
}

/** A goal in the tree: how deep it sits, which goals are above it, and its swim lane. */
export interface TreeRow {
  goal: Goal;
  depth: number;
  /** Its parent, its parent's parent, …: folding any of them hides it. */
  ancestors: number[];
  /** A top-level goal and everything under it share a lane. */
  lane: number;
  /** Whether any goal in the list sits under it, i.e. whether it folds. */
  hasChildren: boolean;
}

/** Every goal in tree order, with what the goals page and the timeline need to draw lanes and fold. */
export function treeRows(goals: Goal[]): TreeRow[] {
  const byId = new Map(goals.map(g => [g.id, g]));
  const parents = new Set(goals.map(g => g.parentId).filter((id): id is number => id !== null && byId.has(id)));
  let lane = -1;
  return inTreeOrder(goals).map(({ goal, depth }) => {
    const ancestors: number[] = [];
    for (let p = goal.parentId; p !== null && byId.has(p); p = byId.get(p)!.parentId) ancestors.push(p);
    if (ancestors.length === 0) lane++;
    return { goal, depth, ancestors, lane, hasChildren: parents.has(goal.id) };
  });
}

/** The rows still shown when the goals in `collapsed` are folded shut. */
export function unfolded<T extends TreeRow>(rows: T[], collapsed: ReadonlySet<number>): T[] {
  return rows.filter(r => !r.ancestors.some(id => collapsed.has(id)));
}

/** Consecutive rows grouped by lane. */
export function byLane<T extends TreeRow>(rows: T[]): T[][] {
  const lanes: T[][] = [];
  for (const row of rows) {
    if (lanes.length === 0 || lanes[lanes.length - 1][0].lane !== row.lane) lanes.push([]);
    lanes[lanes.length - 1].push(row);
  }
  return lanes;
}
