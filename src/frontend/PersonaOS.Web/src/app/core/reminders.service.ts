import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface ReminderDto {
  id: number;
  message: string;
  dueAtUtc: string;
  /** Due time already rendered in the install's configured zone — display this, not dueAtUtc. */
  dueAtLocal: string;
  status: string;
  deliveredAtUtc: string | null;
  goalId: number | null;
  goalTitle: string | null;
  plannerItemId: number | null;
  plannerItemTitle: string | null;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class RemindersService {
  private readonly http = inject(HttpClient);

  list(includeCompleted = false): Promise<ReminderDto[]> {
    return firstValueFrom(
      this.http.get<ReminderDto[]>(`/api/reminders?includeCompleted=${includeCompleted}`)
    );
  }

  /** `dueAtLocal` is interpreted in the install's time zone, not the browser's. */
  create(message: string, dueAtLocal: string): Promise<ReminderDto> {
    return firstValueFrom(this.http.post<ReminderDto>('/api/reminders', { message, dueAtLocal }));
  }

  cancel(id: number): Promise<ReminderDto> {
    return firstValueFrom(this.http.post<ReminderDto>(`/api/reminders/${id}/cancel`, {}));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/reminders/${id}`));
  }
}
