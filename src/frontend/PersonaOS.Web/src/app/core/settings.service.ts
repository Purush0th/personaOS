import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CurrentSettings {
  assistantNickname: string;
  personaTemplate: string;
  aiProvider: string;
  aiModel: string;
  aiBaseUrl: string | null;
  timeZone: string;
  features: Record<string, boolean>;
  /** The key itself is never returned — only whether one is stored. */
  hasAnthropicApiKey: boolean;
}

/** Only the fields present are changed; omit the key to leave it untouched. */
export interface SettingsUpdate {
  assistantNickname?: string;
  personaTemplate?: string;
  anthropicApiKey?: string;
  aiProvider?: string;
  aiModel?: string;
  aiBaseUrl?: string;
  timeZone?: string;
  features?: Record<string, boolean>;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);

  get(): Promise<CurrentSettings> {
    return firstValueFrom(this.http.get<CurrentSettings>('/api/setup'));
  }

  update(update: SettingsUpdate): Promise<{ message: string }> {
    return firstValueFrom(this.http.put<{ message: string }>('/api/setup', update));
  }

  /** Verifies the provider is reachable and answering, without saving. Omitted fields fall
   *  back to the stored config, so this can test a not-yet-saved change (incl. a fresh key). */
  testConnection(test: TestConnection): Promise<TestConnectionResult> {
    return firstValueFrom(this.http.post<TestConnectionResult>('/api/setup/test', test));
  }
}

export interface TestConnection {
  aiProvider?: string;
  aiModel?: string;
  aiBaseUrl?: string;
  anthropicApiKey?: string;
}

export interface TestConnectionResult {
  ok: boolean;
  message: string;
}
