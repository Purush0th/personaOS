import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CurrentSettings {
  assistantNickname: string;
  personaTemplate: string;
  claudeModel: string;
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
  claudeModel?: string;
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
}
