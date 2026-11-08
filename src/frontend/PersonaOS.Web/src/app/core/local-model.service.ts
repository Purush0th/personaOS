import { HttpClient, HttpDownloadProgressEvent, HttpEventType, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, firstValueFrom, map } from 'rxjs';

/** What the server found: its Ollama, and whether that already has the recommended model. */
export interface LocalModelStatus {
  found: boolean;
  address: string | null;
  version: string | null;
  model: string;
  displayName: string;
  sizeBytes: number;
  license: string;
  licenseUrl: string;
  notice: string;
  installed: boolean;
  message: string;
}

/** One line of the download stream. */
export interface PullLine {
  status?: string;
  completed?: number | null;
  total?: number | null;
  done?: boolean;
  error?: string;
}

/**
 * The lines in a newline-delimited JSON stream received so far, from the first. A last line
 * still arriving (no newline yet) is left for the next call.
 */
export function readPullLines(text: string): PullLine[] {
  const complete = text.slice(0, text.lastIndexOf('\n') + 1);
  return complete
    .split('\n')
    .filter(line => line.trim())
    .map(line => {
      try {
        return JSON.parse(line) as PullLine;
      } catch {
        return {};
      }
    });
}

/** The recommended local model: find the owner's Ollama, download the model into it. */
@Injectable({ providedIn: 'root' })
export class LocalModelService {
  private readonly http = inject(HttpClient);

  status(address?: string): Promise<LocalModelStatus> {
    const params = address?.trim() ? new HttpParams().set('address', address.trim()) : undefined;
    return firstValueFrom(this.http.get<LocalModelStatus>('/api/setup/local-model', { params }));
  }

  /** Every line received so far, each time more arrive. Unsubscribing cancels the download. */
  pull(address: string): Observable<PullLine[]> {
    return this.http
      .post('/api/setup/local-model/pull', { address }, { observe: 'events', reportProgress: true, responseType: 'text' })
      .pipe(
        filter(e => e.type === HttpEventType.DownloadProgress || e.type === HttpEventType.Response),
        map(e => readPullLines(e.type === HttpEventType.Response ? (e.body ?? '') + '\n' : ((e as HttpDownloadProgressEvent).partialText ?? ''))),
      );
  }
}
