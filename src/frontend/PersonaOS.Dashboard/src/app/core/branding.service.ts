import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Branding {
  assistantNickname: string;
  isConfigured: boolean;
  enabledFeatures: string[];
  apiVersion: string;
  minSupportedClient: string;
}

/**
 * Loads the instance branding (assistant nickname + enabled features) from the
 * API at startup. The PersonaOS visual identity is fixed; only the nickname and
 * feature availability vary per install.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly http = inject(HttpClient);

  readonly branding = signal<Branding | null>(null);
  readonly loadError = signal<string | null>(null);

  async load(): Promise<void> {
    try {
      const result = await firstValueFrom(this.http.get<Branding>('/api/branding'));
      this.branding.set(result);
    } catch {
      this.loadError.set('Could not reach the PersonaOS server.');
    }
  }

  isEnabled(feature: string): boolean {
    return this.branding()?.enabledFeatures.includes(feature) ?? false;
  }
}
