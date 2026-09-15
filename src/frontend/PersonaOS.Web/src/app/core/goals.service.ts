import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { BoardColumn } from './board.service';

export interface GoalTaskSummary {
  id: number;
  key: string;
  title: string;
  points: number | null;
  column: BoardColumn;
  sprintNumber: number | null;
}

/** A goal is the epic of the sprint board: it does not nest, and its tasks carry the work. */
export interface Goal {
  id: number;
  key: string;
  title: string;
  description: string | null;
  periodType: 'year' | 'quarter' | 'month';
  periodStart: string;
  status: 'active' | 'completed' | 'dropped';
  /** Manually tracked; used only while the goal has no tasks. */
  progress: number;
  /** From the goal's tasks: done points over estimated points. */
  effectiveProgress: number;
  taskCount: number;
  doneTaskCount: number;
  totalPoints: number;
  donePoints: number;
  tasks: GoalTaskSummary[];
}

@Injectable({ providedIn: 'root' })
export class GoalsService {
  private readonly http = inject(HttpClient);

  getAll(includeDropped = false): Promise<Goal[]> {
    return firstValueFrom(this.http.get<Goal[]>(`/api/goals?includeDropped=${includeDropped}`));
  }

  create(goal: {
    title: string;
    periodType: string;
    periodStart: string;
    description?: string | null;
  }): Promise<Goal> {
    return firstValueFrom(this.http.post<Goal>('/api/goals', goal));
  }

  updateStatus(id: number, status: string): Promise<Goal> {
    return firstValueFrom(this.http.put<Goal>(`/api/goals/${id}/status`, { status }));
  }

  updateProgress(id: number, progress: number): Promise<Goal> {
    return firstValueFrom(this.http.put<Goal>(`/api/goals/${id}`, { progress }));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/goals/${id}`));
  }
}
