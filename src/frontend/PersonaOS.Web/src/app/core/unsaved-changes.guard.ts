import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { Confirm } from './confirm';

/** A page with edits that are lost on leaving it unsaved. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Asks before leaving a page with unsaved edits. Closing or reloading the tab is the page's own
 * `beforeunload`, since the router does not see it.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = component =>
  !component.hasUnsavedChanges() ||
  inject(Confirm).ask({
    title: 'Leave without saving?',
    message: 'Your changes on this page are not saved yet.',
    confirmLabel: 'Leave',
    destructive: true,
  });
