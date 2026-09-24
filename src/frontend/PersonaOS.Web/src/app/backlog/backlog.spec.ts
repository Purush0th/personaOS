import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BoardColumn, BoardService, BoardTask, PlanView, Sprint } from '../core/board.service';
import { Confirm } from '../core/confirm';
import { GoalsService } from '../core/goals.service';
import { Backlog } from './backlog';

function sprint(key: string, status: Sprint['status']): Sprint {
  return {
    id: key === 'SPRINT-1' ? 1 : 2, key, number: key === 'SPRINT-1' ? 1 : 2, name: null, status,
    startsAtUtc: '2026-01-01T00:00:00Z', endsAtUtc: '2026-01-08T00:00:00Z', startedAtUtc: null,
    closedAtUtc: null, committedPoints: null, addedPoints: 0, removedPoints: 0, completedPoints: 0,
    carriedOverPoints: null, totalPoints: 0, taskCount: 0, doneTaskCount: 0, unestimatedCount: 0,
    carriedInCount: 0, scopeLocked: false,
  };
}

function task(key: string, column: BoardColumn, points: number | null, sprintKey: string | null): BoardTask {
  return {
    id: Number(key.split('-')[1]), key, title: `Task ${key}`, description: null, points,
    priority: 'medium', column, sprintId: null, sprintKey, sprintName: null, goalId: null,
    goalKey: null, goalTitle: null, sortOrder: 0, addedMidSprint: false, carryOverCount: 0,
    commentCount: 0, attachmentCount: 0, completedAtUtc: null,
    createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
  };
}

const plan: PlanView = {
  sprints: [
    {
      sprint: sprint('SPRINT-1', 'active'),
      tasks: [
        task('TASK-1', 'todo', 3, 'SPRINT-1'),
        task('TASK-2', 'in_progress', 5, 'SPRINT-1'),
        task('TASK-3', 'done', 2, 'SPRINT-1'),
        task('TASK-4', 'todo', 1, 'SPRINT-1'),
      ],
    },
    { sprint: sprint('SPRINT-2', 'planned'), tasks: [task('TASK-5', 'todo', null, 'SPRINT-2')] },
  ],
  backlog: [task('TASK-6', 'backlog', 8, null)],
  velocity: null,
  allowedPoints: [1, 2, 3, 5, 8],
  priorities: ['highest', 'high', 'medium', 'low', 'lowest'],
};

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Backlog', () => {
  let api: jasmine.SpyObj<BoardService>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    api = jasmine.createSpyObj<BoardService>('BoardService', ['plan', 'move', 'create']);
    api.plan.and.resolveTo(plan);
    api.move.and.resolveTo(plan.backlog[0]);
    api.create.and.resolveTo(plan.backlog[0]);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'backlog', component: Backlog }]),
        { provide: BoardService, useValue: api },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error', 'ask']) },
        { provide: GoalsService, useValue: { getAll: () => Promise.resolve([]) } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/backlog');
    await settle();
    harness.detectChanges();
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  const section = (label: string) =>
    (harness.routeNativeElement as HTMLElement).querySelector<HTMLElement>(`section[aria-label="${label}"]`)!;
  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim();
  const menuItems = () => [...document.querySelectorAll<HTMLElement>('.mat-mdc-menu-item')];

  it("totals a sprint's points by status, as Jira's grey, blue and green lozenges", () => {
    const totals = [...section('SPRINT-1').querySelectorAll('header .totals .lozenge')];
    expect(totals.map(t => [t.className.match(/s-\w+/)?.[0], text(t)])).toEqual([
      ['s-todo', '4'],
      ['s-in_progress', '5'],
      ['s-done', '2'],
    ]);
    expect(text(section('Backlog').querySelector('header .totals'))).toBe('8');
  });

  it('shows each sprint task its status, and changes it from the lozenge', async () => {
    const lozenge = section('SPRINT-1').querySelector<HTMLElement>('[aria-label="Status of TASK-1: This week"]')!;
    expect(lozenge.classList).toContain('s-todo');

    lozenge.click();
    await settle();
    expect(menuItems().map(i => text(i))).toEqual(['This week', 'In progress', 'Done']);
    menuItems()[1].click();
    await settle();

    expect(api.move).toHaveBeenCalledOnceWith('TASK-1', 'in_progress', 'SPRINT-1', null, false);
  });

  it('offers only To do for work in a sprint that has not started', async () => {
    section('SPRINT-2').querySelector<HTMLElement>('.status')!.click();
    await settle();
    expect(menuItems().map(i => text(i))).toEqual(['This week']);
  });

  it('shows no status in the backlog, where every task is simply in the backlog', () => {
    expect(section('Backlog').querySelector('.status')).toBeNull();
  });

  it('creates into the backlog from its own Create', async () => {
    section('Backlog').querySelector<HTMLElement>('.create-trigger')!.click();
    harness.detectChanges();
    await settle();

    const field = section('Backlog').querySelector('textarea')!;
    field.value = 'Someday';
    field.dispatchEvent(new Event('input'));
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await settle();

    expect(api.create).toHaveBeenCalledOnceWith({ title: 'Someday', points: null, sprintKey: null }, false);
  });

  it('keeps Create sprint with the backlog, where sprints are planned from', () => {
    const createSprint = [...section('Backlog').querySelectorAll('header button')]
      .find(b => text(b) === 'Create sprint');
    expect(createSprint).toBeTruthy();
  });
});
