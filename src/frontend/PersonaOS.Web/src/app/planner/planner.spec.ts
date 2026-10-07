import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BoardService } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { GoalsService } from '../core/goals.service';
import { PlannerDay, PlannerItemDto, PlannerService } from '../core/planner.service';
import { Planner } from './planner';

function item(id: number, date: string, start: string | null, end: string | null, extra: Partial<PlannerItemDto> = {}): PlannerItemDto {
  return {
    id, title: `Event ${id}`, notes: null, date, scheduledTime: start, endTime: end, sortOrder: 0, status: 'planned',
    goalId: null, goalTitle: null, goalKey: null, taskId: null, taskKey: null, notifyBeforeMinutes: null, recurrence: null,
    recurrenceText: null, occurrenceDate: date, isChanged: false, createdAtUtc: '', updatedAtUtc: '', ...extra,
  };
}

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Planner calendar', () => {
  let harness: RouterTestingHarness;
  let ranges: { from: string; to: string }[];
  let events: PlannerItemDto[];

  const page = () => harness.routeNativeElement!;
  const title = () => page().querySelector('[data-testid=calendar-title]')!.textContent!.trim();
  const shown = () => [...page().querySelectorAll<HTMLElement>('[data-testid=event]')].map(e => e.textContent!.trim());
  const key = async (k: string) => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
    await settle();
    harness.detectChanges();
  };

  beforeEach(async () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 9, 3, 11, 0)); // Saturday 3 October 2026, 11:00
    ranges = [];
    events = [
      item(1, '2026-10-03', '08:30:00', '09:30:00', { title: 'Orders Collection' }),
      item(2, '2026-10-03', '12:00:00', '15:30:00', { title: 'Send Mail', status: 'done' }),
      item(3, '2026-10-02', null, null, { title: 'Gandhi Jayanti' }),
      item(4, '2026-10-01', '20:00:00', '20:30:00', { title: 'Check weekend events', status: 'skipped', recurrence: 'FREQ=WEEKLY', recurrenceText: 'Weekly on Thursday' }),
    ];
    const planner: Partial<PlannerService> = {
      getRange: (from: string, to: string): Promise<PlannerDay[]> => {
        ranges.push({ from, to });
        const inRange = events.filter(e => e.date >= from && e.date <= to);
        const dates = [...new Set(inRange.map(e => e.date))].sort();
        return Promise.resolve(dates.map(date => ({ date, items: inRange.filter(e => e.date === date) })));
      },
    };
    try {
      localStorage.removeItem('planner.options');
    } catch {
      // Storage blocked in this browser: defaults apply anyway.
    }
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'planner', component: Planner }]),
        { provide: PlannerService, useValue: planner },
        { provide: GoalsService, useValue: { getAll: () => Promise.resolve([]) } },
        { provide: BoardService, useValue: { board: () => Promise.resolve({ inProgress: [], todo: [] }) } },
        { provide: BrandingService, useValue: { isEnabled: () => false } },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error', 'ask']) },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    jasmine.clock().uninstall(); // let timers run for real
    harness = await RouterTestingHarness.create();
    // The day is pinned in the address (the page reads it), so the tests do not depend on today.
    await open('/planner?date=2026-10-03');
  });

  async function open(url: string): Promise<void> {
    await harness.navigateByUrl(url);
    await settle();
    harness.detectChanges();
  }

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  it('opens on the week, Monday to Sunday, and loads just that week', () => {
    expect(title()).toBe('Sept – Oct 2026');
    expect(ranges.at(-1)).toEqual({ from: '2026-09-28', to: '2026-10-04' });
    expect(page().querySelectorAll('.day-head').length).toBe(7);
    expect(shown()).toContain('Gandhi Jayanti');
  });

  it('switches views with Google Calendar\'s keys and remembers the view in the address', async () => {
    await key('m');
    expect(title()).toBe('October 2026');
    expect(ranges.at(-1)).toEqual({ from: '2026-09-28', to: '2026-11-01' });

    await key('d');
    expect(title()).toBe('3 October 2026');
    expect(page().querySelectorAll('.day-head').length).toBe(1);

    await key('x');
    expect(page().querySelectorAll('.day-head').length).toBe(4);

    await key('y');
    expect(title()).toBe('2026');
    expect(page().querySelectorAll('app-year-view h3').length).toBe(12);

    expect(TestBed.inject(Location).path()).toContain('view=year');
  });

  it('steps with J and K and comes back with T', async () => {
    await key('j');
    expect(ranges.at(-1)).toEqual({ from: '2026-10-05', to: '2026-10-11' });
    await key('k');
    await key('k');
    expect(ranges.at(-1)).toEqual({ from: '2026-09-21', to: '2026-09-27' });
    await key('t');
    const today = new Date();
    const monday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - ((today.getDay() + 6) % 7));
    const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    expect(ranges.at(-1)!.from).toBe(iso(monday));
  });

  it('hides completed and skipped events and weekends from the view menu toggles', async () => {
    expect(shown().some(s => s.includes('Send Mail'))).toBeTrue();
    const planner = harness.routeDebugElement!.componentInstance as unknown as { setOption(k: string): void };

    planner.setOption('showCompleted');
    planner.setOption('showSkipped');
    harness.detectChanges();
    expect(shown().some(s => s.includes('Send Mail'))).toBeFalse();
    expect(shown().some(s => s.includes('Check weekend'))).toBeFalse();
    expect(shown().some(s => s.includes('Orders Collection'))).toBeTrue();

    planner.setOption('showWeekends');
    harness.detectChanges();
    expect(page().querySelectorAll('.day-head').length).toBe(5);
  });

  it('places a timed event by its time', () => {
    const orders = [...page().querySelectorAll<HTMLElement>('app-time-grid .event')].find(e => e.textContent!.includes('Orders'))!;
    expect(orders.style.top).toBe(`${8.5 * 48}px`);
    expect(orders.style.height).toBe(`${48 - 2}px`);
    // The red current-time line is drawn only on today, which this pinned week does not hold.
    expect(page().querySelector('.now')).toBeNull();
  });

  it('draws the current time on today', async () => {
    await open('/planner?view=day');
    expect(page().querySelector('.now')).not.toBeNull();
  });

  it('a keyboard shortcut typed in a field does nothing', async () => {
    const input = document.createElement('input');
    page().appendChild(input);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'm', bubbles: true }));
    await settle();
    harness.detectChanges();
    expect(title()).toBe('Sept – Oct 2026');
  });

  it('C opens the new event dialog', async () => {
    await key('c');
    expect(document.querySelector('mat-dialog-container h2')?.textContent).toContain('New event');
  });
});

