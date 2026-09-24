import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BoardService, Sprint } from '../core/board.service';
import { Confirm } from '../core/confirm';
import { SprintForm } from './sprint-form';

/** A local time on a day (20:00 unless given), as the API sends it: in UTC. */
const utc = (year: number, month: number, day: number, hour = 20) => new Date(year, month - 1, day, hour).toISOString();

const planned: Sprint = {
  id: 2, key: 'SPRINT-2', number: 2, name: 'Paperwork', status: 'planned',
  startsAtUtc: utc(2026, 9, 27), endsAtUtc: utc(2026, 10, 4, 18), startedAtUtc: null, closedAtUtc: null,
  committedPoints: null, addedPoints: 0, removedPoints: 0, completedPoints: 0, carriedOverPoints: null,
  totalPoints: 0, taskCount: 0, doneTaskCount: 0, unestimatedCount: 0, carriedInCount: 0, scopeLocked: false,
};

@Component({
  imports: [SprintForm],
  template: `<app-sprint-form [sprint]="sprint()" (saved)="saved.push($event)" (cancelled)="cancelled = true" />`,
})
class Host {
  readonly sprint = signal<Sprint | null>(null);
  readonly saved: Sprint[] = [];
  cancelled = false;
}

describe('SprintForm', () => {
  let api: jasmine.SpyObj<BoardService>;
  let confirm: jasmine.SpyObj<Confirm>;

  function render(sprint: Sprint | null = null) {
    api = jasmine.createSpyObj<BoardService>('BoardService', ['createSprint', 'updateSprint']);
    api.createSprint.and.resolveTo(planned);
    api.updateSprint.and.resolveTo(planned);
    confirm = jasmine.createSpyObj<Confirm>('Confirm', ['error']);
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [
        { provide: BoardService, useValue: api },
        { provide: Confirm, useValue: confirm },
      ],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.sprint.set(sprint);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    const start = page.querySelector<HTMLInputElement>('input[type=date]')!;
    const end = () => page.querySelector('.end .value')?.textContent?.trim();
    const pick = (day: string) => {
      start.value = day;
      start.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const save = async () => {
      page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await fixture.whenStable();
    };
    return { fixture, page, start, end, pick, save };
  }

  const shown = (d: Date) =>
    d.toLocaleString(undefined, { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' });

  it('asks for a start day only, and shows the end it implies', () => {
    const { start, end, pick, page } = render();

    expect(start.type).toBe('date');
    expect(page.querySelectorAll('input').length).toBe(2); // name and start; the end is not a field
    expect(end()).toBe('The Sunday after it starts');

    pick('2026-09-25'); // a Friday: runs to the coming Sunday
    expect(end()).toBe(shown(new Date(2026, 8, 27, 18, 0)));
    pick('2026-09-27'); // a Sunday: a full week
    expect(end()).toBe(shown(new Date(2026, 9, 4, 18, 0)));
  });

  it('creates a sprint for the day picked, or for the next free week with none', async () => {
    const first = render();
    first.pick('2026-09-25');
    await first.save();
    expect(api.createSprint).toHaveBeenCalledOnceWith({ name: null, startsOn: '2026-09-25' });
    expect(first.fixture.componentInstance.saved).toEqual([planned]);

    TestBed.resetTestingModule();
    const second = render();
    await second.save();
    expect(api.createSprint).toHaveBeenCalledOnceWith({ name: null, startsOn: null });
  });

  it('opens an edit on the sprint as it is, and sends a start only when it moved', async () => {
    const form = render(planned);
    expect(form.page.querySelector<HTMLInputElement>('input:not([type=date])')!.value).toBe('Paperwork');
    expect(form.start.value).toBe('2026-09-27');

    await form.save();
    expect(api.updateSprint).toHaveBeenCalledWith('SPRINT-2', { name: 'Paperwork', clearName: false });

    form.pick('2026-09-30');
    await form.save();
    expect(api.updateSprint).toHaveBeenCalledWith('SPRINT-2', {
      name: 'Paperwork', clearName: false, startsOn: '2026-09-30',
    });
  });

  it('keeps the form open and says why when the server refuses', async () => {
    const form = render();
    api.createSprint.and.rejectWith({ error: { error: 'A sprint must end after it starts.' } });

    await form.save();

    expect(confirm.error).toHaveBeenCalledWith('A sprint must end after it starts.');
    expect(form.fixture.componentInstance.saved).toEqual([]);
  });

  it('gives up on Cancel without saving', () => {
    const form = render(planned);
    [...form.page.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent?.trim() === 'Cancel')!.click();
    expect(form.fixture.componentInstance.cancelled).toBeTrue();
    expect(api.updateSprint).not.toHaveBeenCalled();
  });
});
