import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { BoardColumn, BoardTask, Priority } from '../core/board.service';
import { WorkItemTable } from './work-item-table';

function task(id: number, title: string, column: BoardColumn, priority: Priority, points: number | null): BoardTask {
  return {
    id, key: `TASK-${id}`, title, description: null, points, priority, column, sprintId: 1,
    sprintKey: 'SPRINT-1', sprintName: null, goalId: null, goalKey: null,
    goalTitle: id === 2 ? 'Get fit' : null, sortOrder: 0, addedMidSprint: false, carryOverCount: 0,
    commentCount: 0, attachmentCount: 0, completedAtUtc: null,
    createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
  };
}

@Component({
  imports: [WorkItemTable],
  template: `<app-work-item-table [tasks]="tasks()" />`,
})
class Host {
  readonly tasks = signal<BoardTask[]>([
    task(10, 'File the tax return', 'done', 'high', 5),
    task(2, 'Book a gym session', 'todo', 'low', null),
    task(3, 'Renew car insurance', 'in_progress', 'highest', 3),
  ]);
}

describe('WorkItemTable', () => {
  function render() {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    const keys = () => [...page.querySelectorAll('tbody td.key a')].map(a => a.textContent?.trim());
    const header = (label: string) =>
      [...page.querySelectorAll<HTMLElement>('th')].find(th => th.textContent?.includes(label))!;
    const search = (value: string) => {
      const input = page.querySelector<HTMLInputElement>('input[type=search]')!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const sortBy = (label: string) => {
      header(label).querySelector('button')!.click();
      fixture.detectChanges();
    };
    return { fixture, page, keys, header, search, sortBy };
  }

  it('lists the tasks by key, numerically, and counts them', () => {
    const { page, keys } = render();
    expect(keys()).toEqual(['TASK-2', 'TASK-3', 'TASK-10']);
    expect(page.querySelector('.count')?.textContent).toContain('Showing 3 of 3');
  });

  it('searches keys, titles and goals, every word having to match', () => {
    const { page, keys, search } = render();

    search('car renew');
    expect(keys()).toEqual(['TASK-3']);
    search('fit');
    expect(keys()).toEqual(['TASK-2']);
    search('task-10');
    expect(keys()).toEqual(['TASK-10']);
    search('holiday');
    expect(keys()).toEqual([]);
    expect(page.querySelector('td.none')?.textContent).toContain('No task matches “holiday”');
  });

  it('sorts by a column, and reverses on a second click', () => {
    const { keys, header, sortBy } = render();

    sortBy('Priority');
    expect(keys()).toEqual(['TASK-3', 'TASK-10', 'TASK-2']);
    expect(header('Priority').getAttribute('aria-sort')).toBe('ascending');
    sortBy('Priority');
    expect(keys()).toEqual(['TASK-2', 'TASK-10', 'TASK-3']);
    expect(header('Priority').getAttribute('aria-sort')).toBe('descending');
    expect(header('Key').getAttribute('aria-sort')).toBe('none');
  });

  it('puts unestimated tasks after estimated ones', () => {
    const { keys, sortBy } = render();
    sortBy('Points');
    expect(keys()).toEqual(['TASK-3', 'TASK-10', 'TASK-2']);
  });

  it('sorts statuses in board order, not alphabetically', () => {
    const { keys, sortBy } = render();
    sortBy('Status');
    expect(keys()).toEqual(['TASK-2', 'TASK-3', 'TASK-10']);
  });

  it('opens a task in the dialog, through the URL', () => {
    const { page } = render();
    expect(page.querySelector('tbody td.key a')?.getAttribute('href')).toBe('/?task=TASK-2');
  });

  it('says when a sprint has no tasks at all', () => {
    const { fixture, page } = render();
    fixture.componentInstance.tasks.set([]);
    fixture.detectChanges();
    expect(page.querySelector('td.none')?.textContent).toContain('No tasks in this sprint.');
  });
});
