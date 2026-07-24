import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface GoalNode {
  id: number;
  title: string;
  description: string | null;
  parentGoalId: number | null;
  periodType: 'year' | 'quarter' | 'month';
  periodStart: string;
  status: 'active' | 'completed' | 'dropped';
  progress: number;
  effectiveProgress: number;
  children: GoalNode[];
}

@Injectable({ providedIn: 'root' })
export class GoalsService {
  private readonly http = inject(HttpClient);

  getTree(includeDropped = false): Promise<GoalNode[]> {
    return firstValueFrom(
      this.http.get<GoalNode[]>(`/api/goals?includeDropped=${includeDropped}`)
    );
  }

  create(goal: {
    title: string;
    periodType: string;
    periodStart: string;
    parentGoalId?: number | null;
    description?: string | null;
  }): Promise<GoalNode> {
    return firstValueFrom(this.http.post<GoalNode>('/api/goals', goal));
  }

  updateStatus(id: number, status: string): Promise<GoalNode> {
    return firstValueFrom(this.http.put<GoalNode>(`/api/goals/${id}/status`, { status }));
  }

  updateProgress(id: number, progress: number): Promise<GoalNode> {
    return firstValueFrom(this.http.put<GoalNode>(`/api/goals/${id}`, { progress }));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/goals/${id}`));
  }
}
