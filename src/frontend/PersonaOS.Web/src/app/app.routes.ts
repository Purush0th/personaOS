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
    // /chat/14-what-are-my-goals — the id leads, the readable tail is for humans.
    path: 'chat/:slug',
    canActivate: [authGuard],
    loadComponent: () => import('./chat/chat').then(m => m.Chat),
  },
  {
    path: 'goals',
    canActivate: [authGuard],
    loadComponent: () => import('./goals/goals').then(m => m.Goals),
  },
  {
    path: 'board',
    canActivate: [authGuard],
    loadComponent: () => import('./board/board').then(m => m.Board),
  },
  {
    path: 'backlog',
    canActivate: [authGuard],
    loadComponent: () => import('./backlog/backlog').then(m => m.Backlog),
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
  {
    path: 'board/goals/:key',
    canActivate: [authGuard],
    loadComponent: () => import('./board/goal-detail').then(m => m.GoalDetail),
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
