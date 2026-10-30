import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';

import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { UpdatesService } from '../core/updates.service';

/** What each updater state means, in the user's words. */
const STATES: Record<string, string> = {
  requested: 'Waiting for the updater…',
  'backing-up': 'Backing up your data…',
  pulling: 'Downloading the new version…',
  restarting: 'Restarting…',
  checking: 'Checking the new version answers…',
  done: 'Updated.',
  failed: 'The update failed.',
  'rolled-back': 'The update did not work, so the previous version was put back.',
};

/**
 * Settings → Updates: the version this server runs, the newest release, Update now, and whether
 * new releases install by themselves. Installing needs the updater (compose profile "updater");
 * without it the card says how to update by hand.
 */
@Component({
  selector: 'app-updates-config',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>Updates</mat-card-title>
        <mat-card-subtitle>
          @if (updates.view(); as v) {
            This server runs {{ v.currentVersion }}
            @if (v.updateAvailable && v.latest) {
              · {{ v.latest.version }} is available
            } @else if (v.latest) {
              · up to date
            }
          }
        </mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (updates.view(); as v) {
          @if (updates.working()) {
            <p data-testid="update-progress">{{ stateText(v.updater?.state) }}</p>
            <mat-progress-bar mode="indeterminate" />
          } @else if (v.updater?.state === 'failed' || v.updater?.state === 'rolled-back') {
            <p class="error" data-testid="update-problem">
              {{ stateText(v.updater.state) }} {{ v.updater.message }}
            </p>
          }

          @if (v.updateAvailable && v.latest; as latest) {
            <p>
              <strong>{{ latest.name }}</strong>
              @if (latest.prerelease) { (pre-release) }
              · {{ latest.publishedAt | date: 'mediumDate' }} ·
              <a [href]="latest.notesUrl" target="_blank" rel="noopener">What's new</a>
            </p>
          }

          @if (v.canInstall) {
            <mat-slide-toggle
              [checked]="v.autoInstall"
              [disabled]="saving()"
              (change)="setAutoInstall($event.checked)"
              data-testid="auto-install"
            >
              Install updates automatically
            </mat-slide-toggle>
            <p class="muted small">
              New releases install by themselves within about 15 minutes. Your data is backed up
              first, and a release that does not start is rolled back.
            </p>
          } @else {
            <p class="muted small" data-testid="manual-update">
              @if (v.updater?.state === 'not-set-up') {
                {{ v.updater.message }}
              } @else {
                To update from here, turn on the updater: add <code>updater</code> to
                <code>COMPOSE_PROFILES</code> in <code>.env</code> and set <code>PERSONAOS_DIR</code>.
                Until then, update with <code>docker compose pull &amp;&amp; docker compose up -d</code>.
              }
            </p>
          }

          @if (v.latestApp?.appDownloadUrl; as app) {
            <p class="muted small" data-testid="phone-app">
              Phone app: <a [href]="app" target="_blank" rel="noopener">download {{ v.latestApp.version }}</a>
            </p>
          }

          @if (error(); as e) {
            <p class="error">{{ e }}</p>
          }
        } @else {
          <p class="muted small">Could not check for updates.</p>
        }
      </mat-card-content>
      @if (updates.view(); as v) {
        @if (v.canInstall && v.updateAvailable && v.latest) {
          <mat-card-actions align="end">
            <button
              mat-flat-button
              (click)="install(v.latest.version)"
              [disabled]="saving() || updates.working()"
              data-testid="update-now"
            >
              <mat-icon>download-simple</mat-icon>
              Update to {{ v.latest.version }}
            </button>
          </mat-card-actions>
        }
      }
    </mat-card>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class UpdatesConfig implements OnInit {
  protected readonly updates = inject(UpdatesService);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.updates.check();
  }

  protected stateText(state: string | undefined): string {
    return STATES[state ?? ''] ?? '';
  }

  protected async install(version: string): Promise<void> {
    await this.run(() => this.updates.install(version));
  }

  protected async setAutoInstall(enabled: boolean): Promise<void> {
    await this.run(() => this.updates.setAutoInstall(enabled));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.error?.message ? e.error.message : 'Could not reach the server.');
    } finally {
      this.saving.set(false);
    }
  }
}
