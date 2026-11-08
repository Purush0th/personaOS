import { Component, EventEmitter, Input, OnDestroy, Output, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription } from 'rxjs';

import { LocalModelService, LocalModelStatus, PullLine } from '../core/local-model.service';

/** The model chosen: where Ollama is and what to ask it for. */
export interface LocalModelChoice {
  address: string;
  model: string;
}

/**
 * The recommended local model, offered where Ollama is picked (Setup Wizard, Settings). It looks
 * for the owner's Ollama, says what the model is (size, licence) before anything downloads, and
 * downloads it into that Ollama only when asked. Installing Ollama itself stays the owner's job.
 */
@Component({
  selector: 'local-model-panel',
  imports: [DecimalPipe, MatButtonModule, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <section class="local-model" aria-live="polite">
      <h3>{{ status()?.displayName ?? 'PersonaOS model' }}</h3>

      @if (status(); as s) {
        <p class="message" [class.ok]="s.found">
          <mat-icon inline>{{ s.found ? 'check-circle' : 'warning-circle' }}</mat-icon>
          {{ s.message }}
        </p>
        <p class="about">
          {{ s.sizeBytes / 1e9 | number: '1.1-1' }} GB download · licence
          <a [href]="s.licenseUrl" target="_blank" rel="noopener">{{ s.license }}</a>. {{ s.notice }}
        </p>
      }

      @if (pulling()) {
        <mat-progress-bar [mode]="percent() === null ? 'indeterminate' : 'determinate'" [value]="percent() ?? 0" />
        <p class="step">{{ step() }}@if (percent() !== null) { · {{ percent() | number: '1.0-0' }}% }</p>
      }

      @if (error(); as e) {
        <p class="message error"><mat-icon inline>warning-circle</mat-icon> {{ e }}</p>
      }

      <div class="actions">
        @if (pulling()) {
          <button mat-button type="button" (click)="cancel()">Cancel download</button>
        } @else if (status()?.installed) {
          <button mat-flat-button type="button" (click)="use()"><mat-icon>check-circle</mat-icon> Use this model</button>
        } @else if (status()?.found) {
          <button mat-flat-button type="button" (click)="download()"><mat-icon>download-simple</mat-icon> Download</button>
        }
        @if (!pulling()) {
          <button mat-button type="button" [disabled]="looking()" (click)="look()">
            <mat-icon>magnifying-glass</mat-icon> {{ status() ? 'Look again' : 'Find Ollama' }}
          </button>
        }
      </div>
    </section>
  `,
  styles: `
    .local-model {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      padding: 0.75rem 1rem;
      border-radius: var(--mat-sys-corner-medium);
      background: var(--mat-sys-surface-container);
    }
    h3 { font: var(--mat-sys-title-small); margin: 0; }
    p { margin: 0; }
    .message { display: flex; gap: 0.4rem; align-items: center; }
    .message.ok mat-icon { color: var(--mat-sys-primary); }
    .message.error { color: var(--mat-sys-error); }
    .about, .step { font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  `,
})
export class LocalModelPanel implements OnDestroy {
  private readonly service = inject(LocalModelService);

  /** The base URL typed so far; blank lets the server look in the usual places. */
  @Input() address = '';

  @Output() chosen = new EventEmitter<LocalModelChoice>();

  protected readonly status = signal<LocalModelStatus | null>(null);
  protected readonly looking = signal(false);
  protected readonly pulling = signal(false);
  protected readonly step = signal('');
  protected readonly percent = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);

  private download$?: Subscription;

  protected async look(): Promise<void> {
    this.looking.set(true);
    this.error.set(null);
    try {
      this.status.set(await this.service.status(this.address));
    } catch (e: unknown) {
      this.error.set((e as { error?: { error?: string } })?.error?.error ?? 'Could not ask the server about Ollama.');
    } finally {
      this.looking.set(false);
    }
  }

  protected download(): void {
    const address = this.status()?.address;
    if (!address) return;
    this.error.set(null);
    this.pulling.set(true);
    this.step.set('Starting the download…');
    this.percent.set(null);
    this.download$ = this.service.pull(address).subscribe({
      next: lines => this.show(lines),
      error: () => this.finish('The download stopped. Try again: Ollama resumes where it left off.'),
      complete: () => this.finish(null),
    });
  }

  protected cancel(): void {
    this.download$?.unsubscribe();
    this.finish('Download cancelled. Starting again resumes it.');
  }

  protected use(): void {
    const s = this.status();
    if (s?.address) this.chosen.emit({ address: s.address, model: s.model });
  }

  ngOnDestroy(): void {
    this.download$?.unsubscribe();
  }

  private show(lines: PullLine[]): void {
    const last = lines[lines.length - 1];
    if (!last) return;
    if (last.error) {
      this.download$?.unsubscribe();
      this.finish(last.error);
      return;
    }
    if (last.done) return;
    this.step.set(last.status ?? '');
    this.percent.set(last.total && last.completed != null ? (100 * last.completed) / last.total : null);
  }

  private finish(error: string | null): void {
    this.pulling.set(false);
    this.error.set(error);
    if (!error) void this.look();
  }
}
