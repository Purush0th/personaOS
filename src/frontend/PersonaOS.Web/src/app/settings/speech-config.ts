import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { Confirm } from '../core/confirm';
import { SpeechService, SpeechSettings, SpeechStatus } from '../core/speech.service';

/**
 * The optional speech service the server calls for the phone: speech-to-text and text-to-speech
 * over the OpenAI-style audio API. Kept apart from the main form, like push: it has its own key,
 * its own test, and saving it must not wait on "Save settings". The web app itself has no voice.
 */
@Component({
  selector: 'app-speech-config',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>Speech service</mat-card-title>
        <mat-card-subtitle>
          @if (status(); as s) {
            @if (s.speechToText || s.textToSpeech) {
              On ·
              {{ s.speechToText && s.textToSpeech ? 'speech-to-text and text-to-speech' : s.speechToText ? 'speech-to-text only' : 'text-to-speech only' }}
            } @else {
              Off · the phone uses its own speech engine
            }
          }
        </mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <p class="muted small">
          Optional. A speech server with an OpenAI-style audio API (faster-whisper, Kokoro, OpenAI…) can
          transcribe what you say to the phone and read replies back. The phone sends audio only to
          PersonaOS, which calls the service. Choose it on the phone under Settings → Voice.
        </p>
        <form (ngSubmit)="save()">
          <mat-form-field>
            <mat-label>Address</mat-label>
            <input matInput name="baseUrl" [(ngModel)]="baseUrl" placeholder="http://localhost:8000/v1" />
            <mat-hint>Blank turns the speech service off.</mat-hint>
          </mat-form-field>
          <div class="pair">
            <mat-form-field>
              <mat-label>Speech-to-text model</mat-label>
              <input matInput name="sttModel" [(ngModel)]="sttModel" placeholder="whisper-1" />
            </mat-form-field>
            <mat-form-field>
              <mat-label>Text-to-speech model</mat-label>
              <input matInput name="ttsModel" [(ngModel)]="ttsModel" placeholder="tts-1" />
            </mat-form-field>
          </div>
          <div class="pair">
            <mat-form-field>
              <mat-label>Voice</mat-label>
              <input matInput name="ttsVoice" [(ngModel)]="ttsVoice" placeholder="alloy" />
            </mat-form-field>
            <mat-form-field>
              <mat-label>API key</mat-label>
              <input
                matInput
                name="speechApiKey"
                type="password"
                autocomplete="off"
                [(ngModel)]="apiKey"
                [placeholder]="status()?.hasApiKey ? 'A key is stored — leave blank to keep it' : 'Blank for keyless servers'"
              />
            </mat-form-field>
          </div>

          @if (result(); as r) {
            <p class="test" [class.ok]="r.ok">
              <mat-icon>{{ r.ok ? 'check_circle' : 'error' }}</mat-icon>
              {{ r.message }}
            </p>
          }

          <div class="actions">
            @if (status()?.hasApiKey) {
              <button mat-button type="button" (click)="forgetKey()" [disabled]="busy()">Remove key</button>
            }
            <button mat-stroked-button type="button" (click)="test()" [disabled]="busy() || !baseUrl.trim()">
              {{ testing() ? 'Testing…' : 'Test' }}
            </button>
            <button mat-flat-button type="submit" [disabled]="busy()">Save speech</button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; margin-top: 1rem; }
    mat-card-content, form { display: flex; flex-direction: column; gap: 0.75rem; }
    mat-card-content { padding-top: 1rem; }
    mat-form-field { width: 100%; }
    .pair { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 0 1rem; }
    .test { display: flex; align-items: center; gap: 0.4rem; margin: 0; }
    .test.ok { color: var(--mat-sys-primary); }
    .actions { display: flex; gap: 0.5rem; justify-content: flex-end; flex-wrap: wrap; }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class SpeechConfig implements OnInit {
  private readonly speech = inject(SpeechService);
  private readonly confirm = inject(Confirm);

  protected readonly status = signal<SpeechStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly testing = signal(false);
  protected readonly result = signal<{ ok: boolean; message: string } | null>(null);

  baseUrl = '';
  sttModel = '';
  ttsModel = '';
  ttsVoice = '';
  /** Blank keeps the stored key: it is never sent back to this page. */
  apiKey = '';

  async ngOnInit(): Promise<void> {
    try {
      this.show(await this.speech.status());
    } catch {
      this.confirm.error('Could not load the speech settings.');
    }
  }

  private show(status: SpeechStatus): void {
    this.status.set(status);
    this.baseUrl = status.baseUrl ?? '';
    this.sttModel = status.sttModel ?? '';
    this.ttsModel = status.ttsModel ?? '';
    this.ttsVoice = status.ttsVoice ?? '';
    this.apiKey = '';
  }

  private get fields(): SpeechSettings {
    return {
      baseUrl: this.baseUrl.trim(),
      sttModel: this.sttModel.trim(),
      ttsModel: this.ttsModel.trim(),
      ttsVoice: this.ttsVoice.trim(),
      ...(this.apiKey.trim() ? { apiKey: this.apiKey.trim() } : {}),
    };
  }

  protected async test(): Promise<void> {
    this.busy.set(true);
    this.testing.set(true);
    this.result.set(null);
    try {
      this.result.set(await this.speech.test(this.fields));
    } catch (e: unknown) {
      this.result.set({ ok: false, message: (e as { error?: { error?: string } })?.error?.error ?? 'Could not run the test.' });
    } finally {
      this.busy.set(false);
      this.testing.set(false);
    }
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      this.show(await this.speech.update(this.fields));
      this.confirm.done('Speech settings saved.');
    } catch (e: unknown) {
      this.confirm.error((e as { error?: { error?: string } })?.error?.error ?? 'Could not save the speech settings.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async forgetKey(): Promise<void> {
    this.busy.set(true);
    try {
      this.show(await this.speech.update({ apiKey: '' }));
    } catch {
      this.confirm.error('Could not remove the key.');
    } finally {
      this.busy.set(false);
    }
  }
}
