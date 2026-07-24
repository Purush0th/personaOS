import { Routes } from '@angular/router';

import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  {
    path: 'login',
    loadComponent: () => import('./login/login').then(m => m.Login),
  },
  {
    path: 'chat',
    canActivate: [authGuard],
    loadComponent: () => import('./chat/chat').then(m => m.Chat),
  },
  {
    path: 'goals',
    canActivate: [authGuard],
    loadComponent: () => import('./goals/goals').then(m => m.Goals),
  },
  {
    path: 'planner',
    canActivate: [authGuard],
    loadComponent: () => import('./planner/planner').then(m => m.Planner),
  },
  {
    path: 'reminders',
    canActivate: [authGuard],
    loadComponent: () => import('./reminders/reminders').then(m => m.Reminders),
  },
  {
    path: 'documents',
    canActivate: [authGuard],
    loadComponent: () => import('./documents/documents').then(m => m.Documents),
  },
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () => import('./settings/settings').then(m => m.Settings),
  },
  { path: '**', redirectTo: 'chat' },
];
