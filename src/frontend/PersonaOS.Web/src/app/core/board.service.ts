import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type BoardColumn = 'backlog' | 'todo' | 'in_progress' | 'done';
export type SprintView = 'current' | 'next';

export interface BoardTask {
  id: number;
  key: string;
  title: string;
  description: string | null;
  points: number | null;
  column: BoardColumn;
  sprintId: number | null;
  sprintNumber: number | null;
  goalId: number | null;
  goalKey: string | null;
  goalTitle: string | null;
  sortOrder: number;
  addedMidSprint: boolean;
  carryOverCount: number;
  completedAtUtc: string | null;
}

export interface Sprint {
  id: number;
  number: number;
  status: 'planned' | 'active' | 'closed';
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
  unestimatedCount: number;
  carriedInCount: number;
  scopeLocked: boolean;
}

export interface BoardView {
  view: SprintView;
  sprint: Sprint;
  inPlanningWindow: boolean;
  canStartSprint: boolean;
  velocity: number | null;
  wipLimit: number;
  allowedPoints: number[];
  backlog: BoardTask[];
  todo: BoardTask[];
  inProgress: BoardTask[];
  done: BoardTask[];
}

export interface SprintReport {
  velocity: number | null;
  sprints: Sprint[];
}

export interface TaskDraft {
  title: string;
  description?: string | null;
  points?: number | null;
  goalId?: number | null;
  destination?: 'backlog' | SprintView;
}

export interface TaskChanges {
  title?: string;
  description?: string;
  points?: number | null;
  clearPoints?: boolean;
  goalId?: number | null;
  clearGoal?: boolean;
}

/** The server refuses an unacknowledged change to a running sprint with this code. */
export const SCOPE_CHANGE_CODE = 'scope_change_unacknowledged';

@Injectable({ providedIn: 'root' })
export class BoardService {
  private readonly http = inject(HttpClient);

  get(view: SprintView = 'current'): Promise<BoardView> {
    return firstValueFrom(this.http.get<BoardView>(`/api/board?sprint=${view}`));
  }

  report(): Promise<SprintReport> {
    return firstValueFrom(this.http.get<SprintReport>('/api/board/sprints'));
  }

  startSprint(): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>('/api/board/sprints/start', {}));
  }

  create(draft: TaskDraft, acknowledgeScopeChange = false): Promise<BoardTask> {
    return firstValueFrom(
      this.http.post<BoardTask>('/api/board/tasks', { ...draft, acknowledgeScopeChange })
    );
  }

  update(id: number, changes: TaskChanges): Promise<BoardTask> {
    return firstValueFrom(this.http.put<BoardTask>(`/api/board/tasks/${id}`, changes));
  }

  move(
    id: number,
    column: BoardColumn,
    sprint: SprintView | null,
    index: number | null,
    acknowledgeScopeChange = false
  ): Promise<BoardTask> {
    return firstValueFrom(
      this.http.put<BoardTask>(`/api/board/tasks/${id}/move`, {
        column,
        sprint,
        index,
        acknowledgeScopeChange,
      })
    );
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/board/tasks/${id}`));
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
