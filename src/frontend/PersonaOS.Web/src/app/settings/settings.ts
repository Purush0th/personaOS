import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { SettingsService, SettingsUpdate } from '../core/settings.service';
import { PushConfig } from './push-config';

/** Module keys the server accepts; unknown keys are ignored server-side. */
const MODULES = ['goals', 'board', 'planner', 'reminders', 'docs', 'voice', 'proactive'] as const;

@Component({
  selector: 'app-settings',
  imports: [
    FormsModule,
    PushConfig,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSlideToggleModule,
  ],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings implements OnInit {
  private readonly settings = inject(SettingsService);
  private readonly branding = inject(BrandingService);
  private readonly confirm = inject(Confirm);

  protected readonly modules = MODULES;
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly hasKey = signal(false);
  protected readonly testing = signal(false);
  protected readonly testResult = signal<{ ok: boolean; message: string } | null>(null);

  protected readonly providers = [
    { id: 'anthropic', label: 'Anthropic (Claude)' },
    { id: 'openai_compatible', label: 'OpenAI-compatible (OpenAI, Ollama, Groq, OpenRouter…)' },
  ];

  nickname = '';
  persona = '';
  provider = 'anthropic';
  model = '';
  baseUrl = '';
  timeZone = '';
  /** Blank means "leave the stored key untouched" — we never receive the current one. */
  apiKey = '';
  features: Record<string, boolean> = {};

  protected get isCompatible(): boolean {
    return this.provider === 'openai_compatible';
  }

  protected async test(): Promise<void> {
    this.testing.set(true);
    this.testResult.set(null);
    try {
      const result = await this.settings.testConnection({
        aiProvider: this.provider,
        aiModel: this.model.trim(),
        aiBaseUrl: this.isCompatible ? this.baseUrl.trim() : '',
        // Send a freshly-typed key if present; otherwise the server tests the stored one.
        anthropicApiKey: this.apiKey.trim() || undefined,
      });
      this.testResult.set(result);
    } catch (e: unknown) {
      this.testResult.set({
        ok: false,
        message: (e as { error?: { error?: string } })?.error?.error ?? 'Could not run the test.',
      });
    } finally {
      this.testing.set(false);
    }
  }

  async ngOnInit(): Promise<void> {
    try {
      const current = await this.settings.get();
      this.nickname = current.assistantNickname;
      this.persona = current.personaTemplate;
      this.provider = current.aiProvider;
      this.model = current.aiModel;
      this.baseUrl = current.aiBaseUrl ?? '';
      this.timeZone = current.timeZone;
      this.features = { ...current.features };
      this.hasKey.set(current.hasAnthropicApiKey);
    } catch {
      this.confirm.error('Could not load settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);

    const update: SettingsUpdate = {
      assistantNickname: this.nickname.trim(),
      personaTemplate: this.persona.trim(),
      aiProvider: this.provider,
      aiModel: this.model.trim(),
      // Send the base URL for compatible providers; clear it (empty) for Anthropic.
      aiBaseUrl: this.isCompatible ? this.baseUrl.trim() : '',
      timeZone: this.timeZone.trim(),
      features: this.features,
    };
    if (this.apiKey.trim()) update.anthropicApiKey = this.apiKey.trim();

    try {
      const result = await this.settings.update(update);
      this.apiKey = '';
      this.hasKey.set(true);
      this.confirm.done(result.message);
      // Nickname and feature gating drive the header and nav, so refresh them.
      await this.branding.load();
    } catch (e: unknown) {
      this.confirm.error((e as { error?: { error?: string } })?.error?.error ?? 'Could not save settings.'
      );
    } finally {
      this.saving.set(false);
    }
  }
}
