import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DocumentDto {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  description: string | null;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentsService {
  private readonly http = inject(HttpClient);

  list(search?: string): Promise<DocumentDto[]> {
    const query = search ? `?search=${encodeURIComponent(search)}` : '';
    return firstValueFrom(this.http.get<DocumentDto[]>(`/api/documents${query}`));
  }

  upload(file: File, description?: string): Promise<DocumentDto> {
    const form = new FormData();
    form.append('file', file);
    if (description) form.append('description', description);
    return firstValueFrom(this.http.post<DocumentDto>('/api/documents', form));
  }

  updateDescription(id: number, description: string): Promise<DocumentDto> {
    return firstValueFrom(
      this.http.put<DocumentDto>(`/api/documents/${id}/description`, { description })
    );
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/documents/${id}`));
  }

  /** Downloads through HttpClient so the bearer interceptor runs — a plain
   *  anchor href can't carry the Authorization header. */
  async download(doc: DocumentDto): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`/api/documents/${doc.id}/content`, { responseType: 'blob' })
    );
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = doc.fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
