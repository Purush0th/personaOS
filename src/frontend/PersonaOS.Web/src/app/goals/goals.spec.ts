import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BoardService } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { Goal, GoalsService } from '../core/goals.service';
import { askDeleteGoal } from './goal-delete-dialog';
import { askNewGoal } from './goal-form-dialog';
import { Goals } from './goals';

function goal(id: number, periodType: Goal['periodType'], overrides: Partial<Goal> = {}): Goal {
  return {
    id, key: `GOAL-${id}`, title: `Goal ${id}`, description: null, periodType, slot: 'Q4 2026',
    periodStart: '2026-10-01', periodEnd: '2026-12-31', parentId: null, parentKey: null, parentTitle: null,
    status: 'active', priority: 'medium', commentCount: 0, attachmentCount: 0, progress: 0, effectiveProgress: 0,
    taskCount: 0, doneTaskCount: 0, childCount: 0, completedChildCount: 0, completeProblem: null,
    createdAtUtc: '2026-09-01T00:00:00Z', updatedAtUtc: '2026-09-01T00:00:00Z', tasks: [], children: [],
    ...overrides,
  };
}

const child = (g: Goal) => ({
  id: g.id, key: g.key, title: g.title, periodType: g.periodType, slot: g.slot, periodStart: g.periodStart,
  periodEnd: g.periodEnd, status: g.status, effectiveProgress: g.effectiveProgress,
});

// A year > quarter > month, plus a standalone month the listing puts first by date.
const month = goal(3, 'month', {
  slot: 'Oct 2026', periodEnd: '2026-10-31', parentId: 2, parentKey: 'GOAL-2', taskCount: 1,
  completeProblem: 'GOAL-3 has open tasks: TASK-9. Mark them done or move them to another monthly goal first.',
  tasks: [{ id: 9, key: 'TASK-9', title: 'Run', points: null, column: 'backlog', sprintNumber: null }],
});
const quarter = goal(2, 'quarter', { parentId: 1, parentKey: 'GOAL-1', childCount: 1, children: [child(month)], completeProblem: 'Complete GOAL-2\'s child goals first: GOAL-3.' });
const year = goal(1, 'year', { slot: '2026', periodStart: '2026-09-27', childCount: 1, children: [child(quarter)] });
const standalone = goal(4, 'month', { slot: 'Nov 2026', periodStart: '2026-11-01', periodEnd: '2026-11-30' });

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Goals', () => {
  let goalsApi: jasmine.SpyObj<GoalsService>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    goalsApi = jasmine.createSpyObj<GoalsService>('GoalsService', ['getAll', 'create', 'delete', 'updateStatus']);
    goalsApi.getAll.and.resolveTo([standalone, year, quarter, month]);
    goalsApi.create.and.resolveTo(standalone);
    goalsApi.delete.and.resolveTo();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'goals', component: Goals }]),
        { provide: GoalsService, useValue: goalsApi },
        { provide: BoardService, useValue: jasmine.createSpyObj('BoardService', ['create']) },
        { provide: BrandingService, useValue: { isEnabled: () => true } },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error', 'ask']) },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/goals');
    await settle();
    harness.detectChanges();
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  const cards = () => [...harness.routeNativeElement!.querySelectorAll<HTMLElement>('section.goal')];
  const card = (key: string) => cards().find(l => l.getAttribute('aria-label')?.startsWith(key + ' '))!;
  const text = (el: Element | null | undefined) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  async function openMenu(key: string): Promise<HTMLElement[]> {
    card(key).querySelector<HTMLButtonElement>('button.menu')!.click();
    harness.detectChanges();
    await settle();
    const panels = document.querySelectorAll<HTMLElement>('.mat-mdc-menu-panel');
    return [...panels[panels.length - 1].querySelectorAll<HTMLElement>('[mat-menu-item]')];
  }

  it('shows each child under its parent, one step in per level', () => {
    expect(cards().map(l => l.getAttribute('aria-label')!.split(' ')[0])).toEqual(['GOAL-4', 'GOAL-1', 'GOAL-2', 'GOAL-3']);
    expect(cards().map(l => l.style.getPropertyValue('--depth'))).toEqual(['0', '0', '1', '2']);
  });

  it('gives each top-level goal and everything under it its own swim lane', () => {
    const lanes = [...harness.routeNativeElement!.querySelectorAll<HTMLElement>('.lane')];
    const keys = (lane: HTMLElement) => [...lane.querySelectorAll('section.goal')].map(s => s.getAttribute('aria-label')!.split(' ')[0]);

    expect(lanes.map(keys)).toEqual([['GOAL-4'], ['GOAL-1', 'GOAL-2', 'GOAL-3']]);
  });

  it('folds child goals away and back, one goal or all', () => {
    const fold = (key: string) => card(key).querySelector<HTMLButtonElement>('button.fold')!;

    fold('GOAL-2').click();
    harness.detectChanges();
    expect(cards().map(l => l.getAttribute('aria-label')!.split(' ')[0])).toEqual(['GOAL-4', 'GOAL-1', 'GOAL-2']);
    expect(fold('GOAL-2').getAttribute('aria-expanded')).toBe('false');

    const all = [...harness.routeNativeElement!.querySelectorAll<HTMLButtonElement>('button')].find(b => text(b).includes('Expand all'))!;
    all.click();
    harness.detectChanges();
    expect(cards().length).toBe(4);
    expect(card('GOAL-4').querySelector('button.fold')).toBeNull(); // nothing under it, nothing to fold
  });

  it('sits in the Goals tabs, with Timeline beside it', () => {
    const tabs = [...harness.routeNativeElement!.querySelectorAll('app-page-tabs a[mat-tab-link]')];
    expect(tabs.map(t => text(t))).toEqual(['Goals', 'Timeline']);
    expect(tabs[1].getAttribute('href')).toBe('/goals/timeline');
  });

  it('offers tasks only on monthly goals, and the next level down on the others', () => {
    expect(text(card('GOAL-3'))).toContain('Create task');
    expect(text(card('GOAL-1'))).toContain('Add quarterly goal');
    expect(text(card('GOAL-1'))).not.toContain('Create task');
    expect(text(card('GOAL-2'))).not.toContain('Create task');
  });

  it('says why a goal cannot be completed or deleted yet instead of failing', async () => {
    const monthItems = await openMenu('GOAL-3');
    const complete = monthItems.find(i => text(i).startsWith('task_alt Complete'))!;
    expect(complete.hasAttribute('disabled') || complete.getAttribute('aria-disabled') === 'true').toBeTrue();
    expect(text(complete)).toContain('open tasks: TASK-9');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    harness.detectChanges();
    await settle();

    const yearItems = await openMenu('GOAL-1');
    const remove = yearItems.find(i => text(i).includes('Delete'))!;
    expect(remove.getAttribute('aria-disabled') === 'true' || remove.hasAttribute('disabled')).toBeTrue();
    expect(text(remove)).toContain('Delete its quarterly goals first.');
    expect(yearItems.some(i => text(i).includes('Update progress'))).toBeFalse();
  });
});

describe('Goal dialogs', () => {
  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 8, 27, 9, 0));
    TestBed.configureTestingModule({
      providers: [
        { provide: GoalsService, useValue: jasmine.createSpyObj('GoalsService', ['create', 'delete']) },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    jasmine.clock().uninstall();
  });

  function openNew(parent: Goal | null = null) {
    void askNewGoal(TestBed.inject(MatDialog), TestBed.inject(Injector), { goals: [year, quarter, month, standalone], parent });
    TestBed.tick();
    return TestBed.inject(MatDialog).openDialogs[0].componentInstance as unknown as Record<string, any>;
  }

  it('offers only quarters with enough days left, and shows the dates it will get', () => {
    const form = openNew();
    form['setType']('quarter');
    TestBed.tick();

    expect(form['slots']().map((s: { label: string; blocked: string | null }) => `${s.label}:${s.blocked ?? ''}`))
      .toEqual(['Q1:over', 'Q2:over', 'Q3:too short', 'Q4:']);

    form['slot'].set(4);
    TestBed.tick();
    expect(form['resolved']().dates).toEqual({ start: '2026-10-01', end: '2026-12-31' });
  });

  it('under a quarter, offers its months and marks the one taken', () => {
    const form = openNew(quarter);

    expect(form['typeValue']()).toBe('month');
    expect(form['slots']().map((s: { label: string; blocked: string | null }) => `${s.label}:${s.blocked ?? ''}`))
      .toEqual(['Oct:taken', 'Nov:', 'Dec:']);
  });

  it('deletes with each task mapped to the goal picked for it', async () => {
    const api = TestBed.inject(GoalsService) as jasmine.SpyObj<GoalsService>;
    api.delete.and.resolveTo();
    const done = askDeleteGoal(TestBed.inject(MatDialog), TestBed.inject(Injector), { goal: month, goals: [year, quarter, month, standalone] });
    TestBed.tick();
    const dialog = TestBed.inject(MatDialog).openDialogs[0].componentInstance as unknown as Record<string, any>;

    expect(dialog['targets']().map((g: Goal) => g.key)).toEqual(['GOAL-4']); // open months other than itself
    dialog['action'].set('reassign');
    dialog['map']('TASK-9', 'GOAL-4');
    await dialog['save']();

    expect(api.delete).toHaveBeenCalledWith(3, 'reassign', { 'TASK-9': 'GOAL-4' });
    expect(await done).toBeTrue();
  });
});
