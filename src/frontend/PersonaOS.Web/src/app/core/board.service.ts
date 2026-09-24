import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type BoardColumn = 'backlog' | 'todo' | 'in_progress' | 'done';
export type Priority = 'highest' | 'high' | 'medium' | 'low' | 'lowest';
export type SprintStatus = 'planned' | 'active' | 'closed';

export interface BoardTask {
  id: number;
  key: string;
  title: string;
  description: string | null;
  points: number | null;
  priority: Priority;
  column: BoardColumn;
  sprintId: number | null;
  sprintKey: string | null;
  sprintName: string | null;
  goalId: number | null;
  goalKey: string | null;
  goalTitle: string | null;
  sortOrder: number;
  addedMidSprint: boolean;
  carryOverCount: number;
  commentCount: number;
  attachmentCount: number;
  completedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface Sprint {
  id: number;
  key: string;
  number: number;
  name: string | null;
  status: SprintStatus;
  startsAtUtc: string;
  endsAtUtc: string;
  startedAtUtc: string | null;
  closedAtUtc: string | null;
  committedPoints: number | null;
  addedPoints: number;
  removedPoints: number;
  completedPoints: number;
  carriedOverPoints: number | null;
  totalPoints: number;
  taskCount: number;
  doneTaskCount: number;
  unestimatedCount: number;
  carriedInCount: number;
  scopeLocked: boolean;
}

export interface BoardView {
  sprint: Sprint | null;
  velocity: number | null;
  wipLimit: number;
  allowedPoints: number[];
  todo: BoardTask[];
  inProgress: BoardTask[];
  done: BoardTask[];
}

export interface SprintPlan {
  sprint: Sprint;
  tasks: BoardTask[];
}

export interface PlanView {
  sprints: SprintPlan[];
  backlog: BoardTask[];
  velocity: number | null;
  allowedPoints: number[];
  priorities: Priority[];
}

export interface BurndownPoint {
  date: string;
  remainingPoints: number;
  completedPoints: number;
}

export interface SprintDetail {
  sprint: Sprint;
  tasks: BoardTask[];
  burndown: BurndownPoint[];
  velocity: number | null;
}

export interface WorkItemComment {
  id: number;
  author: 'user' | 'assistant';
  body: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface WorkItemAttachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
}

export interface TaskDetail {
  task: BoardTask;
  comments: WorkItemComment[];
  attachments: WorkItemAttachment[];
}

export interface SprintReport {
  velocity: number | null;
  sprints: Sprint[];
}

export interface TaskDraft {
  title: string;
  description?: string | null;
  points?: number | null;
  priority?: Priority | null;
  goalId?: number | null;
  /** Sprint key like SPRINT-2; omit for the backlog. */
  sprintKey?: string | null;
  /** Column in the running sprint; omit for To do. */
  column?: BoardColumn | null;
}

export interface TaskChanges {
  title?: string;
  description?: string;
  clearDescription?: boolean;
  points?: number | null;
  clearPoints?: boolean;
  priority?: Priority;
  goalId?: number | null;
  clearGoal?: boolean;
}

/** The server refuses an unacknowledged change to a running sprint with this code. */
export const SCOPE_CHANGE_CODE = 'scope_change_unacknowledged';

export const PRIORITY_LABELS: Record<Priority, string> = {
  highest: 'Highest',
  high: 'High',
  medium: 'Medium',
  low: 'Low',
  lowest: 'Lowest',
};

/** Material Symbols for each priority: arrows up for urgent, down for whenever. */
export const PRIORITY_ICONS: Record<Priority, string> = {
  highest: 'keyboard_double_arrow_up',
  high: 'keyboard_arrow_up',
  medium: 'drag_handle',
  low: 'keyboard_arrow_down',
  lowest: 'keyboard_double_arrow_down',
};

export const COLUMN_LABELS: Record<BoardColumn, string> = {
  backlog: 'Backlog',
  todo: 'This week',
  in_progress: 'In progress',
  done: 'Done',
};

@Injectable({ providedIn: 'root' })
export class BoardService {
  private readonly http = inject(HttpClient);

  board(): Promise<BoardView> {
    return firstValueFrom(this.http.get<BoardView>('/api/board'));
  }

  plan(): Promise<PlanView> {
    return firstValueFrom(this.http.get<PlanView>('/api/board/plan'));
  }

  /** Started sprints, newest first; `count` is how many (the server allows up to 104, two years). */
  report(count = 12): Promise<SprintReport> {
    return firstValueFrom(this.http.get<SprintReport>('/api/board/sprints', { params: { count } }));
  }

  sprint(key: string): Promise<SprintDetail> {
    return firstValueFrom(this.http.get<SprintDetail>(`/api/board/sprints/${key}`));
  }

  createSprint(sprint: { name?: string | null; startsAtLocal?: string | null; endsAtLocal?: string | null }): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>('/api/board/sprints', sprint));
  }

  updateSprint(
    key: string,
    changes: { name?: string | null; clearName?: boolean; startsAtLocal?: string; endsAtLocal?: string }
  ): Promise<Sprint> {
    return firstValueFrom(this.http.put<Sprint>(`/api/board/sprints/${key}`, changes));
  }

  startSprint(key: string): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>(`/api/board/sprints/${key}/start`, {}));
  }

  completeSprint(key: string, target: { moveUnfinishedToSprintKey?: string | null; toBacklog?: boolean }): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>(`/api/board/sprints/${key}/complete`, target));
  }

  deleteSprint(key: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/board/sprints/${key}`));
  }

  task(key: string): Promise<TaskDetail> {
    return firstValueFrom(this.http.get<TaskDetail>(`/api/board/tasks/${key}`));
  }

  create(draft: TaskDraft, acknowledgeScopeChange = false): Promise<BoardTask> {
    return firstValueFrom(
      this.http.post<BoardTask>('/api/board/tasks', { ...draft, acknowledgeScopeChange })
    );
  }

  update(key: string, changes: TaskChanges): Promise<BoardTask> {
    return firstValueFrom(this.http.put<BoardTask>(`/api/board/tasks/${key}`, changes));
  }

  move(
    key: string,
    column: BoardColumn,
    sprintKey: string | null,
    index: number | null,
    acknowledgeScopeChange = false
  ): Promise<BoardTask> {
    return firstValueFrom(
      this.http.put<BoardTask>(`/api/board/tasks/${key}/move`, {
        column,
        sprintKey,
        index,
        acknowledgeScopeChange,
      })
    );
  }

  delete(key: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/board/tasks/${key}`));
  }

  // --- comments and attachments, shared by tasks and goals -------------------

  comments(itemType: 'task' | 'goal', key: string): Promise<WorkItemComment[]> {
    return firstValueFrom(this.http.get<WorkItemComment[]>(`/api/items/${itemType}/${key}/comments`));
  }

  addComment(itemType: 'task' | 'goal', key: string, body: string): Promise<WorkItemComment> {
    return firstValueFrom(
      this.http.post<WorkItemComment>(`/api/items/${itemType}/${key}/comments`, { body })
    );
  }

  updateComment(id: number, body: string): Promise<WorkItemComment> {
    return firstValueFrom(this.http.put<WorkItemComment>(`/api/items/comments/${id}`, { body }));
  }

  deleteComment(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/items/comments/${id}`));
  }

  attachments(itemType: 'task' | 'goal', key: string): Promise<WorkItemAttachment[]> {
    return firstValueFrom(this.http.get<WorkItemAttachment[]>(`/api/items/${itemType}/${key}/attachments`));
  }

  uploadAttachment(itemType: 'task' | 'goal', key: string, file: File): Promise<WorkItemAttachment> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(
      this.http.post<WorkItemAttachment>(`/api/items/${itemType}/${key}/attachments`, form)
    );
  }

  attachmentUrl(id: number): string {
    return `/api/items/attachments/${id}/download`;
  }

  deleteAttachment(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/items/attachments/${id}`));
  }
}

/** The server's error message and code, when the request was refused. */
export function apiError(error: unknown): { message: string | null; code: string | null } {
  const body = (error as HttpErrorResponse)?.error as { error?: string; code?: string } | undefined;
  return { message: body?.error ?? null, code: body?.code ?? null };
}

/**
 * Runs a board change; when the server says it changes a running sprint's scope, asks the user
 * and retries with the acknowledgement. Resolves to null when the user declines.
 */
export async function withScopeConfirmation<T>(
  attempt: (acknowledge: boolean) => Promise<T>,
  ask: (message: string) => boolean = message => confirm(message)
): Promise<T | null> {
  try {
    return await attempt(false);
  } catch (e: unknown) {
    const { message, code } = apiError(e);
    if (code !== SCOPE_CHANGE_CODE) throw e;
    if (!ask(message ?? 'This changes the scope of a sprint that has started. Go ahead?')) return null;
    return await attempt(true);
  }
}

/** A stable colour per goal, so the same goal's chip looks the same everywhere. */
export function goalHue(goalKey: string | null): number {
  const n = Number(goalKey?.replace(/\D/g, '') ?? 0);
  // Golden-angle steps keep neighbouring goals (GOAL-1, GOAL-2) clearly different.
  return Math.round((n * 137.508) % 360);
}

/** Server timestamps are UTC but may arrive without a zone suffix. */
export function parseUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);
}

export function formatWhen(value: string): string {
  return parseUtc(value).toLocaleString(undefined, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatDay(value: string): string {
  return parseUtc(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
}

/** `yyyy-MM-ddTHH:mm` in local time, for the datetime-local inputs the sprint form uses. */
export function toLocalInput(value: string): string {
  const d = parseUtc(value);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
