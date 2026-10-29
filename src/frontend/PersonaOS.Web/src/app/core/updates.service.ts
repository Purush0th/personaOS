import { HttpClient } from '@angular/common/http';
import { Injectable, NgZone, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** localStorage key remembering the version the user dismissed. */
const DISMISSED_KEY = 'personaos.dismissedUpdate';

export interface ReleaseInfo {
  version: string;
  name: string;
  notesUrl: string;
  prerelease: boolean;
  publishedAt: string;
  appDownloadUrl: string | null;
  /** A phone app release ("mobile-v…"), released apart from the server. */
  isApp: boolean;
}

export interface UpdaterStatus {
  heartbeat: string;
  requestId: string | null;
  version: string | null;
  /** idle, requested, backing-up, pulling, restarting, checking, done, failed, rolled-back or not-set-up. */
  state: string;
  message: string | null;
  finishedAt: string | null;
}

export interface UpdatesView {
  currentVersion: string;
  latest: ReleaseInfo | null;
  updateAvailable: boolean;
  autoInstall: boolean;
  /** An updater is running, so Update now can work. */
  canInstall: boolean;
  updater: UpdaterStatus | null;
  /** The newest phone app release, released apart from the server. */
  latestApp: ReleaseInfo | null;
}

/** The states in which an update is under way. */
export const WORKING = ['requested', 'backing-up', 'pulling', 'restarting', 'checking'];

/** How often to look again while an update runs. */
const POLL_MS = 5000;

/**
 * Updates, from the server (/api/updates): which version runs, the newest release, and installing
 * it through the updater. The server reads the releases, so prereleases count and the browser makes
 * no call to GitHub. While an update runs the page asks every few seconds, and reloads once the new
 * version answers, so the web app itself is the new one too.
 */
@Injectable({ providedIn: 'root' })
export class UpdatesService {
  private readonly http = inject(HttpClient);
  private readonly zone = inject(NgZone);

  readonly view = signal<UpdatesView | null>(null);
  private readonly dismissedVersion = signal(readDismissed());
  private poll: ReturnType<typeof setTimeout> | null = null;

  /** True while the updater works on an update. */
  readonly working = computed(() => WORKING.includes(this.view()?.updater?.state ?? ''));

  /** The update for the banner, or null when up to date, dismissed, or not known. */
  readonly update = computed(() => {
    const view = this.view();
    if (!view?.updateAvailable || !view.latest) return null;
    return view.latest.version === this.dismissedVersion() && !this.working() ? null : view.latest;
  });

  async check(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.http.get<UpdatesView>('/api/updates')));
    } catch {
      // Offline, signed out, or an older server without updates: no banner, never an error. In
      // the middle of an update the server is restarting: keep asking until it is back.
      if (this.working()) this.later();
    }
  }

  /** Asks the server to install `version`; the error's message is for the user. */
  async install(version: string): Promise<void> {
    this.apply(await firstValueFrom(this.http.post<UpdatesView>('/api/updates/install', { version })));
  }

  async setAutoInstall(enabled: boolean): Promise<void> {
    this.apply(await firstValueFrom(this.http.put<UpdatesView>('/api/updates/auto-install', { enabled })));
  }

  dismiss(): void {
    const available = this.view()?.latest;
    if (!available) return;
    this.dismissedVersion.set(available.version);
    try {
      localStorage.setItem(DISMISSED_KEY, available.version);
    } catch {
      // Storage blocked: dismissed for this visit only.
    }
  }

  private apply(view: UpdatesView): void {
    const before = this.view()?.currentVersion;
    this.view.set(view);

    // The server restarted on a new version: load the new web app with it.
    if (before && before !== view.currentVersion) {
      location.reload();
      return;
    }

    if (this.poll) clearTimeout(this.poll);
    this.poll = null;
    if (this.working()) this.later();
  }

  /** Looks again shortly; the wait is outside Angular's zone, so the page still counts as settled. */
  private later(): void {
    this.zone.runOutsideAngular(() => {
      this.poll = setTimeout(() => this.zone.run(() => void this.check()), POLL_MS);
    });
  }
}

function readDismissed(): string | null {
  try {
    return localStorage.getItem(DISMISSED_KEY);
  } catch {
    return null;
  }
}
