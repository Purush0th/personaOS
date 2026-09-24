import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BoardService, Sprint, SprintReport } from '../core/board.service';
import { Confirm } from '../core/confirm';
import { Reports } from './reports';

function sprint(n: number, status: Sprint['status'], committed: number | null, completed: number): Sprint {
  return {
    id: n, key: `SPRINT-${n}`, number: n, name: n === 1 ? 'Paperwork week' : null, status,
    startsAtUtc: '2026-01-01T00:00:00Z', endsAtUtc: '2026-01-08T00:00:00Z',
    startedAtUtc: '2026-01-01T00:00:00Z', closedAtUtc: null, committedPoints: committed,
    addedPoints: 0, removedPoints: 0, completedPoints: completed, carriedOverPoints: null,
    totalPoints: 4, taskCount: 0, doneTaskCount: 0, unestimatedCount: 0, carriedInCount: 0,
    scopeLocked: true,
  };
}

const settle = () => new Promise(resolve => setTimeout(resolve, 20));

describe('Reports', () => {
  async function render(report: SprintReport) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'board/reports', component: Reports },
          { path: 'board/sprints/:key', children: [] },
        ]),
        { provide: BoardService, useValue: { report: () => Promise.resolve(report) } },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error']) },
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/board/reports');
    await settle();
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  // The API lists the newest sprint first.
  const report: SprintReport = {
    velocity: 7.5,
    sprints: [sprint(3, 'active', 10, 2), sprint(2, 'closed', 8, 5), sprint(1, 'closed', 5, 10)],
  };

  it('charts commitment against completion, oldest sprint first, against the tallest bar', async () => {
    const page = await render(report);
    const bars = [...page.querySelectorAll<HTMLElement>('.chart .sprint')];

    expect(bars.map(b => b.querySelector('.label')?.textContent)).toEqual(['SPRINT-1', 'SPRINT-2', 'SPRINT-3']);
    const heights = (b: HTMLElement) =>
      [...b.querySelectorAll<HTMLElement>('.bar')].map(bar => bar.style.height);
    expect(heights(bars[0])).toEqual(['50%', '100%']); // 5 committed, 10 completed
    expect(heights(bars[2])).toEqual(['100%', '20%']); // 10 committed, 2 completed so far
    expect(bars[0].getAttribute('href')).toBe('/board/sprints/SPRINT-1');
  });

  it('states the average velocity, or says when there is none yet', async () => {
    expect((await render(report)).querySelector('.average')?.textContent).toContain('7.5');
  });

  it('says when no sprint has finished, instead of an average of nothing', async () => {
    const page = await render({ velocity: null, sprints: [sprint(1, 'active', 4, 0)] });
    expect(page.querySelector('.average')?.textContent).toContain('once a sprint is finished');
  });

  it('lists each sprint with its numbers and a link to its burndown', async () => {
    const page = await render(report);
    const rows = [...page.querySelectorAll('tbody tr')];

    expect(rows.length).toBe(3);
    const cells = [...rows[2].querySelectorAll('td')].map(td => td.textContent?.trim());
    expect(cells[0]).toBe('SPRINT-1 · Paperwork week');
    expect(cells[2]).toBe('Finished');
    expect(cells.slice(3)).toEqual(['5', '10', '0', '0', '–']);
    expect(rows[0].querySelector('.lozenge')?.textContent?.trim()).toBe('Running');
  });

  it('points to the backlog before any sprint has started', async () => {
    const page = await render({ velocity: null, sprints: [] });
    expect(page.querySelector('.empty-state')?.textContent).toContain('Plan one in the backlog');
    expect(page.querySelector('.chart')).toBeNull();
  });

  it('lights the Reports tab and only that one', async () => {
    const page = await render(report);
    const active = [...page.querySelectorAll('app-board-tabs a.mdc-tab--active')].map(a => a.textContent?.trim());
    expect(active).toEqual(['Reports']);
  });
});
