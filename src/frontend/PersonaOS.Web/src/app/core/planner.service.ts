import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * One event on one day. For an event that repeats this is one occurrence: `id` is the series and
 * `occurrenceDate` says which (send it back to change or delete just this one).
 */
export interface PlannerItemDto {
  id: number;
  title: string;
  notes: string | null;
  date: string;
  /** Start, `HH:mm:ss`; null means all day. */
  scheduledTime: string | null;
  /** End of a timed event (an hour after the start when none was set); null for all day. */
  endTime: string | null;
  sortOrder: number;
  status: PlannerStatus;
  goalId: number | null;
  goalTitle: string | null;
  goalKey: string | null;
  /** The sprint-board task this is a day's work on, if picked from the board. */
  taskId: number | null;
  taskKey: string | null;
  /** Minutes before the start the phone is notified; null means no notification. */
  notifyBeforeMinutes: number | null;
  /** The repeat rule (RFC 5545 RRULE text); null for a one-off. */
  recurrence: string | null;
  /** The repeat in words, like "Weekly on Saturday". */
  recurrenceText: string | null;
  /** The day the series put this occurrence on. */
  occurrenceDate: string;
  /** This occurrence was changed on its own. */
  isChanged: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export type PlannerStatus = 'planned' | 'done' | 'skipped';

export interface PlannerDay {
  date: string;
  items: PlannerItemDto[];
}

/** Which events of a repeating series a change or delete applies to. */
export type EditScope = 'this' | 'following' | 'all';

/** What a change or delete is aimed at: for a repeating event, the occurrence and the scope. */
export interface EventTarget {
  scope: EditScope;
  occurrence: string;
}

export interface EventFields {
  title: string;
  date: string;
  notes?: string | null;
  scheduledTime?: string | null;
  endTime?: string | null;
  notifyBeforeMinutes?: number | null;
  recurrence?: string | null;
  goalId?: number | null;
  taskId?: number | null;
}

/** A partial change; the clear flags remove a value (null alone means "leave it"). */
export interface EventChange {
  title?: string;
  notes?: string;
  date?: string;
  scheduledTime?: string;
  clearScheduledTime?: boolean;
  endTime?: string;
  clearEndTime?: boolean;
  notifyBeforeMinutes?: number;
  clearNotify?: boolean;
  recurrence?: string;
  clearRecurrence?: boolean;
  goalId?: number;
  clearGoal?: boolean;
}

@Injectable({ providedIn: 'root' })
export class PlannerService {
  private readonly http = inject(HttpClient);

  getDay(date: string): Promise<PlannerDay> {
    return firstValueFrom(this.http.get<PlannerDay>(`/api/planner?date=${date}`));
  }

  /** The days from `from` to `to` (inclusive) that have events, repeats expanded. */
  getRange(from: string, to: string): Promise<PlannerDay[]> {
    return firstValueFrom(this.http.get<PlannerDay[]>(`/api/planner?from=${from}&to=${to}`));
  }

  create(item: EventFields): Promise<PlannerItemDto> {
    return firstValueFrom(this.http.post<PlannerItemDto>('/api/planner/items', item));
  }

  update(id: number, change: EventChange, target?: EventTarget): Promise<PlannerItemDto> {
    return firstValueFrom(
      this.http.put<PlannerItemDto>(`/api/planner/items/${id}`, change, { params: params(target) })
    );
  }

  updateStatus(id: number, status: PlannerStatus, occurrence?: string): Promise<PlannerItemDto> {
    return firstValueFrom(
      this.http.put<PlannerItemDto>(`/api/planner/items/${id}/status`, { status }, {
        params: occurrence ? new HttpParams().set('occurrence', occurrence) : undefined,
      })
    );
  }

  move(id: number, date: string, target?: EventTarget): Promise<PlannerItemDto> {
    return firstValueFrom(
      this.http.put<PlannerItemDto>(`/api/planner/items/${id}/date`, { date }, { params: params(target) })
    );
  }

  delete(id: number, target?: EventTarget): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/planner/items/${id}`, { params: params(target) }));
  }
}

function params(target?: EventTarget): HttpParams | undefined {
  return target ? new HttpParams().set('scope', target.scope).set('occurrence', target.occurrence) : undefined;
}
