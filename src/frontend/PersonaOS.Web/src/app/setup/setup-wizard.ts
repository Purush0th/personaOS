import { HttpClient } from '@angular/common/http';
import { Component, EventEmitter, Output, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { firstValueFrom } from 'rxjs';

import { AI_PROVIDERS, aiProvider } from '../core/ai-providers';
import { FORM_FIELD_DEFAULTS } from '../core/form-field-defaults';

interface FeatureOption {
  key: string;
  label: string;
  enabled: boolean;
}

/**
 * First-run Setup Wizard. Collects the assistant nickname, persona, admin
 * credentials, the AI provider + model + key (or base URL for local/compatible
 * providers), time zone, and feature toggles, then POSTs them to /api/setup once.
 */
@Component({
  selector: 'setup-wizard',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
  ],
  // The wizard renders outside the page routes, which is where the form-field defaults live.
  providers: [FORM_FIELD_DEFAULTS],
  templateUrl: './setup-wizard.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './setup-wizard.scss'
})
export class SetupWizard {
  private readonly http = inject(HttpClient);

  @Output() completed = new EventEmitter<void>();

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  nickname = '';
  persona = '';
  adminUsername = '';
  adminPassword = '';
  adminPasswordConfirm = '';
  apiKey = '';
  aiProvider = 'anthropic';
  aiModel = 'claude-opus-4-8';
  aiBaseUrl = '';
  timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'UTC';

  readonly providers = AI_PROVIDERS;

  get selected() {
    return aiProvider(this.aiProvider);
  }

  readonly features: FeatureOption[] = [
    { key: 'goals', label: 'Goals (yearly / quarterly / monthly)', enabled: true },
    { key: 'planner', label: 'Daily planner', enabled: true },
    { key: 'reminders', label: 'Reminders & notifications', enabled: true },
    { key: 'docs', label: 'Document storage', enabled: true },
    { key: 'voice', label: 'Voice assistant (push-to-talk)', enabled: true },
    { key: 'proactive', label: 'Proactive scheduler (morning brief)', enabled: false },
  ];

  async submit(): Promise<void> {
    this.error.set(null);

    if (!this.nickname.trim()) { this.error.set('Give your assistant a name.'); return; }
    if (!this.adminUsername.trim()) { this.error.set('Choose an admin username.'); return; }
    if (this.adminPassword.length < 8) { this.error.set('Password must be at least 8 characters.'); return; }
    if (this.adminPassword !== this.adminPasswordConfirm) { this.error.set('Passwords do not match.'); return; }
    if (!this.selected.keyless && !this.apiKey.trim()) { this.error.set('Your Anthropic API key is required.'); return; }
    if (this.selected.needsBaseUrl && !this.aiBaseUrl.trim()) {
      this.error.set(`Enter the provider's base URL (e.g. ${this.selected.baseUrlPlaceholder}).`);
      return;
    }
    if (!this.aiModel.trim()) { this.error.set('Enter a model id.'); return; }

    this.submitting.set(true);
    try {
      await firstValueFrom(this.http.post('/api/setup', {
        assistantNickname: this.nickname.trim(),
        personaTemplate: this.persona.trim() || null,
        adminUsername: this.adminUsername.trim(),
        adminPassword: this.adminPassword,
        anthropicApiKey: this.apiKey.trim(),
        aiProvider: this.aiProvider,
        aiModel: this.aiModel.trim(),
        aiBaseUrl: this.selected.needsBaseUrl ? this.aiBaseUrl.trim() : null,
        timeZone: this.timeZone,
        features: Object.fromEntries(this.features.map(f => [f.key, f.enabled])),
      }));
      this.completed.emit();
    } catch (err: unknown) {
      const message = (err as { error?: { error?: string } })?.error?.error
        ?? 'Setup failed — is the server reachable?';
      this.error.set(message);
      this.submitting.set(false);
    }
  }
}
