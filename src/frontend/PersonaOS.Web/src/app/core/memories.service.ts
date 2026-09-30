import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** What kind of thing a memory is. */
export const MEMORY_CATEGORIES = ['fact', 'preference', 'project', 'decision'] as const;
export type MemoryCategory = (typeof MEMORY_CATEGORIES)[number];

/** The longest memory the server accepts: one short sentence. */
export const MEMORY_MAX_LENGTH = 500;

export interface Memory {
  id: number;
  content: string;
  category: MemoryCategory;
  /** The conversation's address, when the memory came from chat. */
  sourceConversationId: string | null;
  sourceConversationTitle: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

/** "preference" reads "Preference". */
export function categoryLabel(category: string): string {
  return category ? category[0].toUpperCase() + category.slice(1) : category;
}

@Injectable({ providedIn: 'root' })
export class MemoriesService {
  private readonly http = inject(HttpClient);

  list(search?: string, category?: string): Promise<Memory[]> {
    let params = new HttpParams();
    if (search?.trim()) params = params.set('search', search.trim());
    if (category) params = params.set('category', category);
    return firstValueFrom(this.http.get<Memory[]>('/api/memories', { params }));
  }

  create(content: string, category: string): Promise<Memory> {
    return firstValueFrom(this.http.post<Memory>('/api/memories', { content, category }));
  }

  update(id: number, content: string, category: string): Promise<Memory> {
    return firstValueFrom(this.http.put<Memory>(`/api/memories/${id}`, { content, category }));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/memories/${id}`));
  }

  /** Auto-save on: the assistant saves memories and shows a receipt. Off: each is a card first. */
  getAutoSave(): Promise<boolean> {
    return firstValueFrom(this.http.get<{ autoSave: boolean }>('/api/memories/settings')).then(s => s.autoSave);
  }

  setAutoSave(autoSave: boolean): Promise<boolean> {
    return firstValueFrom(this.http.put<{ autoSave: boolean }>('/api/memories/settings', { autoSave })).then(s => s.autoSave);
  }
}
