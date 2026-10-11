import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { BoardTask } from './board.service';

/** The longest note the server accepts: a thought, not a document. */
export const NOTE_MAX_LENGTH = 2000;

/** Something jotted down to deal with later. */
export interface Note {
  id: number;
  text: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class NotesService {
  private readonly http = inject(HttpClient);

  /** Every note, the latest edited first; with `search`, only those containing it. */
  list(search?: string): Promise<Note[]> {
    const params = search ? new HttpParams().set('search', search) : undefined;
    return firstValueFrom(this.http.get<Note[]>('/api/notes', { params }));
  }

  create(text: string): Promise<Note> {
    return firstValueFrom(this.http.post<Note>('/api/notes', { text }));
  }

  update(id: number, text: string): Promise<Note> {
    return firstValueFrom(this.http.put<Note>(`/api/notes/${id}`, { text }));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/notes/${id}`));
  }

  /** Makes the note a task in the backlog (its first line the title) and removes the note. */
  moveToBacklog(id: number): Promise<BoardTask> {
    return firstValueFrom(this.http.post<BoardTask>(`/api/notes/${id}/to-backlog`, {}));
  }
}
