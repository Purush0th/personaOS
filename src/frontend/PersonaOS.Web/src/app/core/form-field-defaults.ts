import { Provider } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS, MatFormFieldDefaultOptions } from '@angular/material/form-field';

/**
 * Every field in the app is outlined, and only takes room for a hint when it has one, so a column
 * of fields is evenly spaced whether or not some of them carry hints.
 *
 * Provided by the lazily loaded page routes and by the setup wizard, not by the app config:
 * importing it at the root pulled the whole form-field module (and Angular forms with it) into
 * the first download, for a shell that has no fields.
 */
export const FORM_FIELD_DEFAULTS: Provider = {
  provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
  useValue: { appearance: 'outline', subscriptSizing: 'dynamic' } satisfies MatFormFieldDefaultOptions,
};
