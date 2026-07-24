import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { BrandingService } from '../core/branding.service';
import { SettingsService, SettingsUpdate } from '../core/settings.service';

/** Module keys the server accepts; unknown keys are ignored server-side. */
const MODULES = ['goals', 'planner', 'reminders', 'docs', 'voice', 'proactive'] as const;

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings implements OnInit {
  private readonly settings = inject(SettingsService);
  private readonly branding = inject(BrandingService);

  protected readonly modules = MODULES;
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal<string | null>(null);
  protected readonly hasKey = signal(false);

  nickname = '';
  persona = '';
  model = '';
  timeZone = '';
  /** Blank means "leave the stored key untouched" — we never receive the current one. */
  apiKey = '';
  features: Record<string, boolean> = {};

  async ngOnInit(): Promise<void> {
    try {
      const current = await this.settings.get();
      this.nickname = current.assistantNickname;
      this.persona = current.personaTemplate;
      this.model = current.claudeModel;
      this.timeZone = current.timeZone;
      this.features = { ...current.features };
      this.hasKey.set(current.hasAnthropicApiKey);
    } catch {
      this.error.set('Could not load settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.saved.set(null);
    this.error.set(null);

    const update: SettingsUpdate = {
      assistantNickname: this.nickname.trim(),
      personaTemplate: this.persona.trim(),
      claudeModel: this.model.trim(),
      timeZone: this.timeZone.trim(),
      features: this.features,
    };
    if (this.apiKey.trim()) update.anthropicApiKey = this.apiKey.trim();

    try {
      const result = await this.settings.update(update);
      this.apiKey = '';
      this.hasKey.set(true);
      this.saved.set(result.message);
      // Nickname and feature gating drive the header and nav, so refresh them.
      await this.branding.load();
    } catch (e: unknown) {
      this.error.set(
        (e as { error?: { error?: string } })?.error?.error ?? 'Could not save settings.'
      );
    } finally {
      this.saving.set(false);
    }
  }
}
