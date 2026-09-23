import { Routes } from '@angular/router';

/**
 * Every page lives in pages.routes.ts, loaded on first navigation. Keeping them out of this file
 * keeps what they share (form fields, Angular forms) out of the first download too.
 */
export const routes: Routes = [
  { path: '', loadChildren: () => import('./pages.routes').then(m => m.PAGE_ROUTES) },
];
