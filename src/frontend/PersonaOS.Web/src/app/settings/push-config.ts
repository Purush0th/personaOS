import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';

import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

import { Confirm } from '../core/confirm';
import { PushStatus, SettingsService } from '../core/settings.service';

/**
 * Bring-your-own Firebase push, configured by uploading the owner's own project files.
 *
 * Kept separate from the main settings form on purpose: uploading a credential is its own
 * action with its own validation, and it must not be tangled up with — or silently re-sent
 * by — an unrelated "Save settings".
 */
@Component({
  selector: 'app-push-config',
  imports: [MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './push-config.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './push-config.scss',
})
export class PushConfig implements OnInit {
  private readonly settings = inject(SettingsService);
  private readonly confirm = inject(Confirm);

  protected readonly status = signal<PushStatus | null>(null);
  protected readonly busy = signal(false);

  /** File names only, for display. The contents are held just long enough to upload. */
  protected readonly serviceAccountName = signal<string | null>(null);
  protected readonly googleServicesName = signal<string | null>(null);

  private serviceAccountJson: string | null = null;
  private googleServicesJson: string | null = null;

  protected get canUpload(): boolean {
    return !!this.serviceAccountJson && !!this.googleServicesJson && !this.busy();
  }

  async ngOnInit(): Promise<void> {
    try {
      this.status.set(await this.settings.getPush());
    } catch {
      this.confirm.error('Could not load push notification settings.');
    }
  }

  protected async pick(which: 'serviceAccount' | 'googleServices', event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    const text = await file.text();
    if (which === 'serviceAccount') {
      this.serviceAccountJson = text;
      this.serviceAccountName.set(file.name);
    } else {
      this.googleServicesJson = text;
      this.googleServicesName.set(file.name);
    }
  }

  protected async upload(): Promise<void> {
    if (!this.serviceAccountJson || !this.googleServicesJson) return;

    this.busy.set(true);
    try {
      const status = await this.settings.setPush(this.serviceAccountJson, this.googleServicesJson);
      this.status.set(status);
      this.confirm.done(`Push notifications are on for project “${status.projectId}”. ` +
          'Sign in on the phone again for it to register.'
      );
      this.forgetFiles();
    } catch (e: unknown) {
      // The server explains exactly what is wrong — swapped files, mismatched projects, the
      // wrong package name — so show its message rather than a generic failure.
      this.confirm.error(
        (e as { error?: { error?: string } })?.error?.error ?? 'Could not save the Firebase files.'
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    if (!confirm('Turn off push notifications and delete the stored Firebase key?')) return;

    this.busy.set(true);
    try {
      await this.settings.clearPush();
      this.status.set({ configured: false, projectId: null });
      this.confirm.done('Push notifications are off. The Firebase key has been deleted.');
    } catch {
      this.confirm.error('Could not remove the Firebase key.');
    } finally {
      this.busy.set(false);
    }
  }

  /** Drops the file contents from memory once they have been uploaded. */
  private forgetFiles(): void {
    this.serviceAccountJson = null;
    this.googleServicesJson = null;
    this.serviceAccountName.set(null);
    this.googleServicesName.set(null);
  }
}
