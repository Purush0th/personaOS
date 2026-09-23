import { Component, Injectable, inject, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';

export interface ConfirmRequest {
  title: string;
  /** What will happen, in the user's terms. Shown under the title. */
  message?: string;
  confirmLabel?: string;
  /** Colours the confirm button as destructive. */
  destructive?: boolean;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    @if (data.message) {
      <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    }
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">Cancel</button>
      <button
        mat-flat-button
        [mat-dialog-close]="true"
        [class.destructive]="data.destructive"
        cdkFocusInitial
      >
        {{ data.confirmLabel ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .destructive {
      background: var(--mat-sys-error);
      color: var(--mat-sys-on-error);
    }
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmRequest>(MAT_DIALOG_DATA);
  protected readonly ref = inject(MatDialogRef<ConfirmDialog>);
}

/**
 * Asking and telling, in one place.
 *
 * Before Material the app used `confirm()` for deletions and a red paragraph, an alert or nothing
 * at all for failures — three answers to the same question. A native dialog also cannot be styled
 * and looks broken on a phone.
 */
@Injectable({ providedIn: 'root' })
export class Confirm {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  /** Resolves true when the user confirms. */
  ask(request: ConfirmRequest): Promise<boolean> {
    const ref = this.dialog.open<ConfirmDialog, ConfirmRequest, boolean>(ConfirmDialog, {
      data: request,
      width: '28rem',
      maxWidth: '92vw',
      autoFocus: 'dialog',
    });
    return firstValueFrom(ref.afterClosed()).then(result => result === true);
  }

  /** A failure the user should see but need not act on. */
  error(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 6000, politeness: 'assertive' });
  }

  /** Confirmation that something happened, when the change is not visible on screen. */
  done(message: string): void {
    this.snackBar.open(message, undefined, { duration: 3000 });
  }
}
