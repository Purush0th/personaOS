import { ChangeDetectionStrategy, Component, DestroyRef, Injector, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';

import { TaskDetail } from './task-detail';

/**
 * The query parameter naming the task open over a page: /board?task=TASK-3. Templates link to it
 * as `[queryParams]="{ task: key }"`, since a template cannot use a computed key.
 */
export const TASK_PARAM = 'task';

interface TaskDialogData {
  key: string;
}

/**
 * A task opened from a card, over the page it was opened from, the way Jira opens an issue from
 * its board. The full page is one click away, and Esc or the close button returns to the board.
 */
@Component({
  selector: 'app-task-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIconModule, RouterLink, TaskDetail],
  template: `
    <div class="bar">
      <a
        mat-icon-button
        [routerLink]="['/board/tasks', data.key]"
        mat-dialog-close
        aria-label="Open full page"
        title="Open full page"
      >
        <mat-icon>open_in_full</mat-icon>
      </a>
      <button mat-icon-button mat-dialog-close aria-label="Close" title="Close">
        <mat-icon>close</mat-icon>
      </button>
    </div>
    <mat-dialog-content>
      <app-task-detail [taskKey]="data.key" [embedded]="true" (deleted)="ref.close()" />
    </mat-dialog-content>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .bar {
      display: flex;
      justify-content: flex-end;
      gap: 0.25rem;
      padding: 0.5rem 0.5rem 0;
    }

    mat-dialog-content {
      max-height: calc(90vh - 4rem);
      padding-top: 0;
    }
  `,
})
export class TaskDialog {
  protected readonly data = inject<TaskDialogData>(MAT_DIALOG_DATA);
  protected readonly ref = inject(MatDialogRef<TaskDialog>);
}

/**
 * Opens the task named by `?task=` in a dialog over the calling page, and keeps the URL and the
 * dialog in step: closing the dialog drops the parameter, and Back (or a link that drops it)
 * closes the dialog. Call it from a component's constructor; `onClosed` runs after each close,
 * for the page to reload what the dialog may have changed.
 */
export function openTaskFromQuery(onClosed: () => void): void {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  const dialog = inject(MatDialog);
  // The page's injector, not the root one MatDialog would use: the task inside needs what the
  // page routes provide, such as the outlined form fields.
  const injector = inject(Injector);

  let open: MatDialogRef<TaskDialog> | null = null;
  let destroyed = false;
  const close = () => {
    const ref = open;
    open = null;
    ref?.close();
  };
  inject(DestroyRef).onDestroy(() => {
    destroyed = true;
    close();
  });

  route.queryParamMap
    .pipe(
      map(params => params.get(TASK_PARAM)),
      distinctUntilChanged(),
      takeUntilDestroyed()
    )
    .subscribe(key => {
      close();
      if (!key) return;

      const ref = dialog.open(TaskDialog, {
        data: { key } satisfies TaskDialogData,
        injector,
        width: '960px',
        maxWidth: 'calc(100vw - 2rem)',
        maxHeight: '90vh',
        panelClass: 'task-dialog',
      });
      open = ref;
      ref.afterClosed().subscribe(() => {
        if (destroyed) return;
        onClosed();
        // Closed by the user rather than by the URL: take the parameter off to match.
        if (open !== ref) return;
        open = null;
        void router.navigate([], {
          relativeTo: route,
          queryParams: { [TASK_PARAM]: null },
          queryParamsHandling: 'merge',
        });
      });
    });
}
