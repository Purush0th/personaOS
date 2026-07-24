import { HttpClient } from '@angular/common/http';
import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

interface FeatureOption {
  key: string;
  label: string;
  enabled: boolean;
}

/**
 * First-run Setup Wizard. Collects the assistant nickname, persona, admin
 * credentials, the user's Anthropic API key, model, time zone, and feature
 * toggles, then POSTs them to /api/setup exactly once.
 */
@Component({
  selector: 'setup-wizard',
  imports: [FormsModule],
  templateUrl: './setup-wizard.html',
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
  anthropicApiKey = '';
  claudeModel = 'claude-opus-4-8';
  timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'UTC';

  readonly models = [
    { id: 'claude-opus-4-8', label: 'Claude Opus 4.8 (most capable)' },
    { id: 'claude-sonnet-5', label: 'Claude Sonnet 5 (balanced)' },
    { id: 'claude-haiku-4-5-20251001', label: 'Claude Haiku 4.5 (fastest)' },
  ];

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
    if (!this.anthropicApiKey.trim()) { this.error.set('Your Anthropic API key is required.'); return; }

    this.submitting.set(true);
    try {
      await firstValueFrom(this.http.post('/api/setup', {
        assistantNickname: this.nickname.trim(),
        personaTemplate: this.persona.trim() || null,
        adminUsername: this.adminUsername.trim(),
        adminPassword: this.adminPassword,
        anthropicApiKey: this.anthropicApiKey.trim(),
        claudeModel: this.claudeModel,
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
