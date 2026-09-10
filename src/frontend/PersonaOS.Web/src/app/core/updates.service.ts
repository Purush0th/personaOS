import { Injectable, computed, inject, signal } from '@angular/core';

import { BrandingService } from './branding.service';

/** localStorage key remembering the version the user dismissed. */
const DISMISSED_KEY = 'personaos.dismissedUpdate';

export interface AvailableUpdate {
  version: string;
  name: string;
  notesUrl: string;
}

/**
 * Notify-only update check. Compares the running API version (from /api/branding)
 * against the latest GitHub Release and, if newer, surfaces a dismissible banner.
 * Deliberately fail-silent: a private/absent repo, rate limiting, or being offline
 * simply means no banner — never an error in the user's face.
 */
@Injectable({ providedIn: 'root' })
export class UpdatesService {
  private readonly branding = inject(BrandingService);

  private readonly latest = signal<AvailableUpdate | null>(null);
  private readonly dismissedVersion = signal(localStorage.getItem(DISMISSED_KEY));

  /** The update to show, or null when up to date / dismissed / check failed. */
  readonly update = computed(() => {
    const available = this.latest();
    if (!available) return null;
    return available.version === this.dismissedVersion() ? null : available;
  });

  async check(): Promise<void> {
    const branding = this.branding.branding();
    const current = branding?.apiVersion;
    // The repo comes from the API, so the backend stays the single source of truth.
    // A second hardcoded copy here is exactly how this banner ended up polling a
    // repository that does not exist.
    const repo = branding?.repository;
    if (!current || !repo) return;

    try {
      const response = await fetch(`https://api.github.com/repos/${repo}/releases/latest`, {
        headers: { Accept: 'application/vnd.github+json' },
      });
      if (!response.ok) return; // 404 (no public repo/release yet), rate limit, etc.

      const release = (await response.json()) as {
        tag_name?: string;
        name?: string;
        html_url?: string;
      };
      const latestVersion = (release.tag_name ?? '').replace(/^v/, '');
      if (!latestVersion || !isNewer(latestVersion, current)) return;

      this.latest.set({
        version: latestVersion,
        name: release.name || `v${latestVersion}`,
        notesUrl: release.html_url ?? `https://github.com/${repo}/releases`,
      });
    } catch {
      // Offline or blocked — stay silent.
    }
  }

  dismiss(): void {
    const available = this.latest();
    if (!available) return;
    this.dismissedVersion.set(available.version);
    localStorage.setItem(DISMISSED_KEY, available.version);
  }
}

/** True when `candidate` is a strictly higher dotted-numeric version than `current`. */
export function isNewer(candidate: string, current: string): boolean {
  const a = candidate.split('.').map(n => parseInt(n, 10) || 0);
  const b = current.split('.').map(n => parseInt(n, 10) || 0);
  const length = Math.max(a.length, b.length);
  for (let i = 0; i < length; i++) {
    const left = a[i] ?? 0;
    const right = b[i] ?? 0;
    if (left !== right) return left > right;
  }
  return false;
}
