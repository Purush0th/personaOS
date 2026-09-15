import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface PlannerItemDto {
  id: number;
  title: string;
  notes: string | null;
  date: string;
  scheduledTime: string | null;
  sortOrder: number;
  status: 'planned' | 'done' | 'skipped';
  goalId: number | null;
  goalTitle: string | null;
  goalKey: string | null;
  /** The sprint-board task this is a day's work on, if picked from the board. */
  taskId: number | null;
  taskKey: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PlannerDay {
  date: string;
  items: PlannerItemDto[];
}

@Injectable({ providedIn: 'root' })
export class PlannerService {
  private readonly http = inject(HttpClient);

  getDay(date: string): Promise<PlannerDay> {
    return firstValueFrom(this.http.get<PlannerDay>(`/api/planner?date=${date}`));
  }

  create(item: {
    title: string;
    date: string;
    scheduledTime?: string | null;
    goalId?: number | null;
    taskId?: number | null;
  }): Promise<PlannerItemDto> {
    return firstValueFrom(this.http.post<PlannerItemDto>('/api/planner/items', item));
  }

  updateStatus(id: number, status: string): Promise<PlannerItemDto> {
    return firstValueFrom(
      this.http.put<PlannerItemDto>(`/api/planner/items/${id}/status`, { status })
    );
  }

  move(id: number, date: string): Promise<PlannerItemDto> {
    return firstValueFrom(this.http.put<PlannerItemDto>(`/api/planner/items/${id}/date`, { date }));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/planner/items/${id}`));
  }
}
