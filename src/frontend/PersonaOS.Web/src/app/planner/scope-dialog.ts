import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatRadioModule } from '@angular/material/radio';
import { firstValueFrom } from 'rxjs';

import { EditScope } from '../core/planner.service';

export interface ScopeRequest {
  action: 'edit' | 'delete';
  /** "This event" is not offered: the change touches what belongs to the whole series (its repeat, alert or links). */
  seriesOnly?: boolean;
}

/**
 * Which events of a repeating series a change or delete is for, as Google Calendar asks it:
 * This event, This and following events, All events.
 */
@Component({
  selector: 'app-scope-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatRadioModule],
  template: `
    <h2 mat-dialog-title>{{ data.action === 'delete' ? 'Delete recurring event' : 'Edit recurring event' }}</h2>
    <mat-dialog-content>
      <mat-radio-group class="choices" [ngModel]="scope()" (ngModelChange)="scope.set($event)" aria-label="Which events">
        @if (!data.seriesOnly) {
          <mat-radio-button value="this" data-testid="scope-this">This event</mat-radio-button>
        }
        <mat-radio-button value="following" data-testid="scope-following">This and following events</mat-radio-button>
        <mat-radio-button value="all" data-testid="scope-all">All events</mat-radio-button>
      </mat-radio-group>
      @if (data.seriesOnly) {
        <p class="muted small">The repeat and the notification apply to the whole series, so they cannot change for one event alone.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">Cancel</button>
      <button mat-flat-button [mat-dialog-close]="scope()" cdkFocusInitial data-testid="scope-ok">OK</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    /* Three choices always fit; the radios' touch targets reach a few pixels past the content,
       which drew a scrollbar for nothing. */
    mat-dialog-content {
      overflow: visible;
    }
    .choices {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    p {
      max-width: 22rem;
    }
  `,
})
export class ScopeDialog {
  protected readonly data = inject<ScopeRequest>(MAT_DIALOG_DATA);
  protected readonly scope = signal<EditScope>(this.data.seriesOnly ? 'following' : 'this');

  /** Asks; null when cancelled. */
  static ask(dialog: MatDialog, request: ScopeRequest): Promise<EditScope | null> {
    return firstValueFrom(
      dialog.open<ScopeDialog, ScopeRequest, EditScope | null>(ScopeDialog, { data: request, width: '24rem' }).afterClosed()
    ).then(result => result ?? null);
  }
}
