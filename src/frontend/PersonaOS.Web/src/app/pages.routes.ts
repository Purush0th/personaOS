import { Routes, UrlMatchResult, UrlSegment } from '@angular/router';

import { authGuard } from './core/auth.guard';
import { unsavedChangesGuard } from './core/unsaved-changes.guard';
import { FORM_FIELD_DEFAULTS } from './core/form-field-defaults';

/** "chat", or "chat/<slug>" with the slug as the :slug parameter. */
export function chatUrl(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments[0]?.path !== 'chat' || segments.length > 2) return null;
  return segments.length === 2 ? { consumed: segments, posParams: { slug: segments[1] } } : { consumed: segments };
}

/** The pages, under one parent that gives them all the same form-field defaults. */
export const PAGE_ROUTES: Routes = [
  {
    path: '',
    providers: [FORM_FIELD_DEFAULTS],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'chat' },
      {
        path: 'login',
        loadComponent: () => import('./login/login').then(m => m.Login),
      },
      {
        // /chat and /chat/k3n9x2qp are one route, so the page stays when a new thread gets its
        // address after the first reply. As two routes Angular rebuilt the page there, and the
        // whole thread vanished and redrew (owner saw it wobble, 2026-11-03).
        matcher: chatUrl,
        canActivate: [authGuard],
        loadComponent: () => import('./chat/chat').then(m => m.Chat),
      },
      {
        path: 'goals',
        canActivate: [authGuard],
        loadComponent: () => import('./goals/goals').then(m => m.Goals),
      },
      {
        // The same goals drawn on a year's calendar (PRD 3.2): a tab of Goals, so it sits under
        // /goals — before goals/:key, which would otherwise read "timeline" as a goal's key.
        path: 'goals/timeline',
        canActivate: [authGuard],
        loadComponent: () => import('./timeline/timeline').then(m => m.Timeline),
      },
      { path: 'timeline', pathMatch: 'full', redirectTo: 'goals/timeline' },
      {
        // Goals stand apart from the board (which holds sprints, tasks and reports).
        path: 'goals/:key',
        canActivate: [authGuard],
        loadComponent: () => import('./goals/goal-detail').then(m => m.GoalDetail),
      },
      {
        path: 'board',
        canActivate: [authGuard],
        loadComponent: () => import('./board/board').then(m => m.Board),
      },
      {
        // The backlog is the board's other view, not a section of its own.
        path: 'board/backlog',
        canActivate: [authGuard],
        loadComponent: () => import('./backlog/backlog').then(m => m.Backlog),
      },
      { path: 'backlog', pathMatch: 'full', redirectTo: 'board/backlog' },
      {
        path: 'board/reports',
        canActivate: [authGuard],
        loadComponent: () => import('./board/reports').then(m => m.Reports),
      },
      {
        path: 'board/tasks/:key',
        canActivate: [authGuard],
        loadComponent: () => import('./board/task-detail').then(m => m.TaskDetail),
      },
      {
        path: 'board/sprints/:key',
        canActivate: [authGuard],
        loadComponent: () => import('./board/sprint-detail').then(m => m.SprintDetail),
      },
      // Where a goal's page used to be; links from before the move still arrive.
      { path: 'board/goals/:key', redirectTo: 'goals/:key' },
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
        path: 'notes',
        canActivate: [authGuard],
        loadComponent: () => import('./notes/notes').then(m => m.Notes),
      },
      {
        path: 'settings',
        canActivate: [authGuard],
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./settings/settings').then(m => m.Settings),
      },
      { path: '**', redirectTo: 'chat' },
    ],
  },
];
