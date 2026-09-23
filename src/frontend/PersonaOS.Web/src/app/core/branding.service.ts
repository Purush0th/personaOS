import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Branding {
  assistantNickname: string;
  isConfigured: boolean;
  enabledFeatures: string[];
  apiVersion: string;
  minSupportedClient: string;
  /** GitHub "owner/name" to poll for releases; served so it is defined in one place. */
  repository: string;
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

  /**
   * Fetches the branding, trying again a few times before giving up: the API may still be
   * starting (after a restart or an update), and a page stuck on "could not reach" until someone
   * reloads it is worse than a short wait.
   */
  async load(retryDelaysMs: readonly number[] = [1000, 2000, 4000, 8000]): Promise<void> {
    this.loadError.set(null);
    for (let attempt = 0; ; attempt++) {
      try {
        this.branding.set(await firstValueFrom(this.http.get<Branding>('/api/branding')));
        return;
      } catch {
        if (attempt >= retryDelaysMs.length) {
          this.loadError.set('Could not reach the PersonaOS server.');
          return;
        }
        await new Promise(resolve => setTimeout(resolve, retryDelaysMs[attempt]));
      }
    }
  }

  isEnabled(feature: string): boolean {
    return this.branding()?.enabledFeatures.includes(feature) ?? false;
  }
}
