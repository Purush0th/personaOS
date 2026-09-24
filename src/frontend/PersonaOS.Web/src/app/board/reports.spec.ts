import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BoardColumn, BoardService, BoardTask, Priority, Sprint, SprintDetail, SprintReport } from '../core/board.service';
import { Confirm } from '../core/confirm';
import { Reports } from './reports';

function sprint(n: number, status: Sprint['status'], committed: number | null, completed: number): Sprint {
  return {
    id: n, key: `SPRINT-${n}`, number: n, name: n === 1 ? 'Paperwork week' : null, status,
    startsAtUtc: '2026-01-01T00:00:00Z', endsAtUtc: '2099-01-08T00:00:00Z',
    startedAtUtc: '2026-01-01T00:00:00Z', closedAtUtc: status === 'closed' ? '2026-01-08T00:00:00Z' : null,
    committedPoints: committed, addedPoints: 2, removedPoints: 1, completedPoints: completed,
    carriedOverPoints: null, totalPoints: 4, taskCount: 0, doneTaskCount: 0, unestimatedCount: 0,
    carriedInCount: 0, scopeLocked: true,
  };
}

function task(n: number, column: BoardColumn, priority: Priority, sprintKey: string): BoardTask {
  return {
    id: n, key: `TASK-${n}`, title: `Task number ${n}`, description: null, points: n, priority,
    column, sprintId: null, sprintKey, sprintName: null, goalId: null, goalKey: null, goalTitle: null,
    sortOrder: n, addedMidSprint: false, carryOverCount: 0, commentCount: 0, attachmentCount: 0,
    completedAtUtc: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
  };
}

// The API lists the newest sprint first.
const report: SprintReport = {
  velocity: 7.5,
  sprints: [sprint(3, 'active', 10, 2), sprint(2, 'closed', 8, 5), sprint(1, 'closed', 5, 10)],
};

const details: Record<string, SprintDetail> = {
  'SPRINT-3': {
    sprint: report.sprints[0],
    tasks: [task(1, 'todo', 'high', 'SPRINT-3'), task(2, 'in_progress', 'medium', 'SPRINT-3'), task(3, 'done', 'medium', 'SPRINT-3')],
    burndown: [
      { date: '2026-09-23', remainingPoints: 6, completedPoints: 0 },
      { date: '2026-09-24', remainingPoints: 3, completedPoints: 3 },
    ],
    velocity: 7.5,
  },
  'SPRINT-1': {
    sprint: report.sprints[2],
    tasks: [task(7, 'done', 'low', 'SPRINT-1')],
    burndown: [], velocity: 7.5,
  },
};

const settle = () => new Promise(resolve => setTimeout(resolve, 30));

describe('Reports', () => {
  let api: { report: jasmine.Spy; sprint: jasmine.Spy };
  let harness: RouterTestingHarness;

  async function render(url: string, data: SprintReport = report) {
    api = {
      report: jasmine.createSpy('report').and.resolveTo(data),
      sprint: jasmine.createSpy('sprint').and.callFake((key: string) => Promise.resolve(details[key])),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'board/reports', component: Reports },
          { path: 'board/sprints/:key', children: [] },
        ]),
        { provide: BoardService, useValue: api },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error']) },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    await stable();
    return harness.routeNativeElement as HTMLElement;
  }

  /** The report arrives, the effect asks for the sprint, and the sprint arrives: three rounds. */
  async function stable(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await settle();
      harness.detectChanges();
    }
  }

  const text = (el: Element | null | undefined) => el?.textContent?.replace(/\s+/g, ' ').trim();
  const keys = (page: HTMLElement) => [...page.querySelectorAll('app-work-item-table tbody td.key a')].map(a => text(a));

  it('asks for every started sprint, so old ones can be found', async () => {
    await render('/board/reports');
    expect(api.report).toHaveBeenCalledOnceWith(104);
  });

  it('opens on the running sprint and lists its tasks', async () => {
    const page = await render('/board/reports');

    expect(text(page.querySelector('.sprint-head h2'))).toBe('SPRINT-3');
    expect(keys(page)).toEqual(['TASK-1', 'TASK-2', 'TASK-3']);
    const tiles = [...page.querySelectorAll('.tile b')].map(b => text(b));
    expect(tiles.slice(0, 3)).toEqual(['1 of 3', '2 of 10', '+2 / −1']);
    expect(text(page.querySelector('.tile:last-child span'))).toBe('left in the sprint');
  });

  it('shows the sprint the URL names, finished ones included', async () => {
    const page = await render('/board/reports?sprint=SPRINT-1');

    expect(text(page.querySelector('.sprint-head h2'))).toBe('SPRINT-1 · Paperwork week');
    expect(text(page.querySelector('.sprint-head .lozenge'))).toBe('Finished');
    expect(keys(page)).toEqual(['TASK-7']);
    expect(text(page.querySelector('.tile:last-child b'))).toBe('Finished');
  });

  it('finds an old sprint by searching, and puts the choice in the URL', async () => {
    const page = await render('/board/reports');

    const search = page.querySelector<HTMLInputElement>('.sprint-search input')!;
    search.focus();
    search.value = 'paperwork';
    search.dispatchEvent(new Event('input'));
    harness.detectChanges();
    await settle();

    const options = [...document.querySelectorAll<HTMLElement>('mat-option')];
    expect(options.map(o => text(o.querySelector('b')))).toEqual(['SPRINT-1 · Paperwork week']);
    options[0].click();
    await stable();

    expect(TestBed.inject(Router).url).toBe('/board/reports?sprint=SPRINT-1');
    expect(keys(page)).toEqual(['TASK-7']);
    expect(search.value).toBe('');
  });

  it('shows the burndown above the task details, in place of a link to it', async () => {
    const page = await render('/board/reports');
    const headings = [...page.querySelectorAll('.panel h3')].map(h => text(h));

    expect(headings.indexOf('Points remaining')).toBe(headings.indexOf('Task details') - 1);
    expect(page.querySelectorAll('app-burndown-chart .dot').length).toBe(2);
    expect(page.querySelector('a[href^="/board/sprints/"]')).toBeNull();
  });

  it('counts the tasks by status and by priority', async () => {
    const page = await render('/board/reports');
    const [status, priority] = [...page.querySelectorAll('app-breakdown-bar')];

    expect([...status.querySelectorAll('li')].map(li => text(li))).toEqual(['This week 1', 'In progress 1', 'Done 1']);
    expect([...priority.querySelectorAll('li')].map(li => text(li))).toEqual(
      ['Highest 0', 'High 1', 'Medium 2', 'Low 0', 'Lowest 0']
    );
  });

  it('charts velocity oldest first, and a bar picks its sprint', async () => {
    const page = await render('/board/reports');
    const bars = [...page.querySelectorAll<HTMLElement>('.chart .sprint')];

    expect(bars.map(b => text(b.querySelector('.label')))).toEqual(['SPRINT-1', 'SPRINT-2', 'SPRINT-3']);
    expect([...bars[0].querySelectorAll<HTMLElement>('.bar')].map(b => b.style.height)).toEqual(['50%', '100%']);
    expect(bars[2].getAttribute('aria-current')).toBe('true');
    expect(bars[0].getAttribute('href')).toBe('/board/reports?sprint=SPRINT-1');
    expect(text(page.querySelector('.velocity-head'))).toContain('7.5 points a sprint');
  });

  it('says there is no average before a sprint finishes, and shows a real zero as zero', async () => {
    let page = await render('/board/reports', { velocity: null, sprints: [report.sprints[0]] });
    expect(text(page.querySelector('.velocity-head'))).toContain('once a sprint is finished');

    TestBed.resetTestingModule();
    page = await render('/board/reports', { velocity: 0, sprints: [report.sprints[0]] });
    expect(text(page.querySelector('.velocity-head'))).toContain('0 points a sprint');
  });

  it('points to the backlog before any sprint has started', async () => {
    const page = await render('/board/reports', { velocity: null, sprints: [] });
    expect(text(page.querySelector('.empty-state'))).toContain('Plan one in the backlog');
    expect(api.sprint).not.toHaveBeenCalled();
  });

  it('lights the Reports tab and only that one', async () => {
    const page = await render('/board/reports');
    const active = [...page.querySelectorAll('app-board-tabs a.mdc-tab--active')].map(a => text(a));
    expect(active).toEqual(['Reports']);
  });
});
