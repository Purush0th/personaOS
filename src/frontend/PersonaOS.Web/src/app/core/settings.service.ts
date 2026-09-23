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
  /** Set in Settings; null means the model's default applies. */
  aiContextTokens: number | null;
  /** The context size this model gets when none is set. */
  defaultContextTokens: number;
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
  /** 0 clears it back to the model's default. */
  aiContextTokens?: number;
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

  /** Whether push is on, and for which Firebase project. Never includes the key. */
  getPush(): Promise<PushStatus> {
    return firstValueFrom(this.http.get<PushStatus>('/api/setup/push'));
  }

  /** Uploads both Firebase files; the server rejects the pair unless they share a project. */
  setPush(serviceAccountJson: string, googleServicesJson: string): Promise<PushStatus> {
    return firstValueFrom(
      this.http.put<PushStatus>('/api/setup/push', { serviceAccountJson, googleServicesJson })
    );
  }

  clearPush(): Promise<void> {
    return firstValueFrom(this.http.delete<void>('/api/setup/push'));
  }
}

export interface PushStatus {
  configured: boolean;
  projectId: string | null;
}

export interface TestConnection {
  aiProvider?: string;
  aiModel?: string;
  aiBaseUrl?: string;
  anthropicApiKey?: string;
  aiContextTokens?: number;
}

export interface TestConnectionResult {
  ok: boolean;
  message: string;
}
