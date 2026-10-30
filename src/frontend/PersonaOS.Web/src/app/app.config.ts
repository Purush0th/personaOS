import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { MAT_ICON_DEFAULT_OPTIONS } from '@angular/material/icon';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withInMemoryScrolling } from '@angular/router';

import { authInterceptor } from './core/auth.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Opening an item page should start at its top, not wherever the list was scrolled to.
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'enabled' })),
    provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    // Every mat-icon is a Phosphor glyph (styles.scss); without this it would look for the
    // Material Icons font and render each icon as its own name in text.
    { provide: MAT_ICON_DEFAULT_OPTIONS, useValue: { fontSet: 'ph' } },
    // Form-field defaults are provided by the lazy page routes (see FORM_FIELD_DEFAULTS), so the
    // form-field module stays out of the first download.
  ],
};
