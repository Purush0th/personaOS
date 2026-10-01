import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { Confirm } from '../core/confirm';
import { Goal, GoalsService } from '../core/goals.service';
import { Timeline } from './timeline';

function goal(id: number, periodType: Goal['periodType'], start: string, end: string, overrides: Partial<Goal> = {}): Goal {
  return {
    id, key: `GOAL-${id}`, title: `Goal ${id}`, description: null, periodType, slot: '', periodStart: start,
    periodEnd: end, parentId: null, parentKey: null, parentTitle: null, status: 'active', priority: 'medium',
    commentCount: 0, attachmentCount: 0, progress: 0, effectiveProgress: 40, taskCount: 0, doneTaskCount: 0,
    childCount: 0, completedChildCount: 0, completeProblem: null, createdAtUtc: '', updatedAtUtc: '',
    tasks: [], children: [], ...overrides,
  };
}

const year = goal(1, 'year', '2026-01-01', '2026-12-31');
const q4 = goal(2, 'quarter', '2026-10-01', '2026-12-31', { parentId: 1 });
const october = goal(3, 'month', '2026-10-01', '2026-10-31', { parentId: 2 });
const standalone = goal(4, 'month', '2026-11-01', '2026-11-30');
const nextYear = goal(5, 'year', '2027-01-01', '2027-12-31');

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Timeline', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 8, 27, 9, 0));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'timeline', component: Timeline }]),
        { provide: GoalsService, useValue: { getAll: () => Promise.resolve([year, q4, october, standalone, nextYear]) } },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error']) },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    jasmine.clock().uninstall(); // keep the mocked date; let timers run for real
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/timeline');
    await settle();
    harness.detectChanges();
  });

  const page = () => harness.routeNativeElement!;
  const bars = () => [...page().querySelectorAll<HTMLElement>('a.bar')];
  const bar = (key: string) => bars().find(b => b.getAttribute('aria-label')!.startsWith(key + ' '))!;

  it('shows the current year: parents before their children, standalone goals at their own level', () => {
    expect(bars().map(b => b.getAttribute('aria-label')!.split(' ')[0])).toEqual(['GOAL-1', 'GOAL-2', 'GOAL-3', 'GOAL-4']);
    const levels = [...page().querySelectorAll<HTMLElement>('.label')].map(l => l.style.getPropertyValue('--level'));
    expect(levels).toEqual(['0', '1', '2', '2']);
  });

  it('gives each top-level goal and everything under it its own lane', () => {
    const firsts = [...page().querySelectorAll<HTMLElement>('.label.lane-first')].map(l => l.querySelector('.key')!.textContent!.trim());
    const lasts = [...page().querySelectorAll<HTMLElement>('.label.lane-last')].map(l => l.querySelector('.key')!.textContent!.trim());

    expect(firsts).toEqual(['GOAL-1', 'GOAL-4']);
    expect(lasts).toEqual(['GOAL-3', 'GOAL-4']);
  });

  it('places each bar at its share of the year', () => {
    expect(parseFloat(bar('GOAL-1').style.left)).toBe(0);
    expect(parseFloat(bar('GOAL-1').style.width)).toBeCloseTo(100, 2);
    expect(parseFloat(bar('GOAL-2').style.left)).toBeCloseTo((273 / 365) * 100, 2); // 1 October
    expect(parseFloat(bar('GOAL-3').style.width)).toBeCloseTo((31 / 365) * 100, 2);
    expect(bar('GOAL-3').querySelector<HTMLElement>('.fill')!.style.width).toBe('40%');
  });

  it('folds a goal\'s children away and back', () => {
    page().querySelector<HTMLButtonElement>('button.fold')!.click(); // GOAL-1
    harness.detectChanges();
    expect(bars().map(b => b.getAttribute('aria-label')!.split(' ')[0])).toEqual(['GOAL-1', 'GOAL-4']);

    page().querySelector<HTMLButtonElement>('button.fold')!.click();
    harness.detectChanges();
    expect(bars().length).toBe(4);
  });

  it('switches year from the dropdown, and marks today only in the current year', async () => {
    expect(page().querySelector('.today')).not.toBeNull();

    (harness.routeDebugElement!.componentInstance as unknown as { setYear(y: number): void }).setYear(2027);
    harness.detectChanges();

    expect(bars().map(b => b.getAttribute('aria-label')!.split(' ')[0])).toEqual(['GOAL-5']);
    expect(page().querySelector('.today')).toBeNull();
  });

  it('opens the goal of each bar over the timeline, as a quick view', () => {
    expect(bar('GOAL-2').getAttribute('href')).toContain('goal=GOAL-2');
  });
});
