import { CdkDrag } from '@angular/cdk/drag-drop';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BoardService, BoardTask, BoardView } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { FORM_FIELD_DEFAULTS } from '../core/form-field-defaults';
import { GoalsService } from '../core/goals.service';
import { Board } from './board';

function task(key: string, title: string, sortOrder: number): BoardTask {
  return {
    id: sortOrder + 1, key, title, description: null, points: 3, priority: 'medium',
    column: 'todo', sprintId: 1, sprintKey: 'SPRINT-1', sprintName: null, goalId: null,
    goalKey: null, goalTitle: null, sortOrder, addedMidSprint: false, carryOverCount: 0,
    commentCount: 0, attachmentCount: 0, completedAtUtc: null,
    createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
  };
}

const view: BoardView = {
  sprint: {
    id: 1, key: 'SPRINT-1', number: 1, name: null, status: 'active',
    startsAtUtc: '2026-01-01T00:00:00Z', endsAtUtc: '2026-01-08T00:00:00Z',
    startedAtUtc: '2026-01-01T00:00:00Z', closedAtUtc: null, committedPoints: 6, addedPoints: 0,
    removedPoints: 0, completedPoints: 0, carriedOverPoints: null, totalPoints: 6, taskCount: 2,
    doneTaskCount: 0, unestimatedCount: 0, carriedInCount: 0, scopeLocked: false,
  },
  velocity: null, wipLimit: 3, allowedPoints: [1, 2, 3, 5, 8],
  todo: [task('TASK-1', 'File the report', 0), task('TASK-2', 'Book the dentist', 1)],
  inProgress: [], done: [],
};

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Board', () => {
  let api: jasmine.SpyObj<BoardService>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    api = jasmine.createSpyObj<BoardService>('BoardService', [
      'board', 'move', 'create', 'task', 'plan', 'comments', 'attachments',
    ]);
    api.board.and.resolveTo(view);
    api.move.and.resolveTo(view.todo[0]);
    api.create.and.resolveTo(view.todo[0]);
    api.task.and.callFake(key => Promise.resolve({
      task: view.todo.find(t => t.key === key)!, comments: [], attachments: [],
    }));
    api.plan.and.rejectWith(new Error('not needed'));
    api.comments.and.resolveTo([]);
    api.attachments.and.resolveTo([]);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          // As in pages.routes.ts, which provides the form-field defaults at the route.
          { path: 'board', component: Board, providers: [FORM_FIELD_DEFAULTS] },
          { path: 'board/tasks/:key', children: [] },
        ]),
        { provide: BoardService, useValue: api },
        { provide: Confirm, useValue: jasmine.createSpyObj('Confirm', ['error', 'ask']) },
        { provide: GoalsService, useValue: { getAll: () => Promise.resolve([]) } },
        { provide: BrandingService, useValue: { branding: () => null } },
        // Dialogs open and close at once, so a test sees the result without waiting on a fade.
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/board');
    await settle();
    harness.detectChanges();

    // The test window is narrower than the board's three-column breakpoint, where the columns
    // stack and the ones below end up off screen. Lay them side by side, in view, as on a desktop.
    const columns = root().querySelector<HTMLElement>('.columns')!;
    columns.style.gridTemplateColumns = 'repeat(3, 1fr)';
    columns.scrollIntoView();
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  const root = () => harness.routeNativeElement as HTMLElement;
  const card = (key: string) =>
    [...root().querySelectorAll<HTMLElement>('.task')].find(c => c.querySelector('.key')?.textContent === key)!;
  const column = (id: string) => root().querySelector<HTMLElement>(`[data-column="${id}"]`)!;

  /**
   * A real mouse drag as CDK sees it: press, move in steps, and optionally let go. The events
   * carry the window, without which the browser leaves the scroll offset out of pageX/pageY.
   */
  async function drag(from: HTMLElement, x: number, y: number, release = true): Promise<void> {
    const start = from.getBoundingClientRect();
    const sx = start.left + 12;
    const sy = start.top + 12;
    from.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, clientX: sx, clientY: sy, button: 0, buttons: 1, detail: 1, view: window,
    }));
    for (let i = 1; i <= 10; i++) {
      document.dispatchEvent(new MouseEvent('mousemove', {
        bubbles: true, cancelable: true, buttons: 1, view: window,
        clientX: sx + ((x - sx) * i) / 10, clientY: sy + ((y - sy) * i) / 10,
      }));
      harness.detectChanges();
    }
    if (release) {
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, clientX: x, clientY: y, button: 0, view: window }));
      await settle();
      harness.detectChanges();
    }
  }

  const centre = (el: HTMLElement) => {
    const r = el.getBoundingClientRect();
    return [r.left + r.width / 2, r.top + r.height / 2] as const;
  };

  it('drags a card from anywhere, so grabbing the title moves the card instead of its link', () => {
    const first = card('TASK-1');
    expect(first.querySelector('[cdkDragHandle]')).toBeNull();

    // With a grip, CDK leaves the rest of the card alone and the browser drags the link away.
    const nativeDrag = new DragEvent('dragstart', { bubbles: true, cancelable: true });
    first.querySelector('.open')!.dispatchEvent(nativeDrag);
    expect(nativeDrag.defaultPrevented).toBeTrue();
  });

  it('waits for a short press on touch, so a swipe across the board still scrolls it', () => {
    const drag = harness.fixture.debugElement.query(By.directive(CdkDrag)).injector.get(CdkDrag);
    expect(drag.dragStartDelay).toEqual({ touch: 250, mouse: 0 });
  });

  it('lights up the column under a dragged card and leaves the card faded where it was', async () => {
    const [x, y] = centre(column('in_progress'));
    await drag(card('TASK-1'), x, y, false);

    expect(column('in_progress').classList).toContain('drop-target');
    expect(column('in_progress').querySelector('header')!.textContent).toContain('This week');
    expect(column('in_progress').querySelector('header')!.textContent).toContain('In progress');
    expect(column('todo').querySelector('.move-hint')?.textContent).toContain('Move to');
    expect(column('todo').querySelector('.cdk-drag-placeholder')).not.toBeNull();
    expect(column('in_progress').querySelector('.cdk-drag-placeholder')).toBeNull();

    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, clientX: x, clientY: y, view: window }));
    await settle();
  });

  it('moves a card dropped on another column to the end of that column', async () => {
    const [x, y] = centre(column('done'));
    await drag(card('TASK-2'), x, y);

    expect(api.move).toHaveBeenCalledOnceWith('TASK-2', 'done', 'SPRINT-1', null, false);
    expect(column('done').classList).not.toContain('drop-target');
  });

  it('reorders a card dropped lower in its own column', async () => {
    const below = card('TASK-2').getBoundingClientRect();
    await drag(card('TASK-1'), below.left + 12, below.bottom - 4);

    expect(api.move).toHaveBeenCalledOnceWith('TASK-1', 'todo', 'SPRINT-1', 1, false);
  });

  it('opens a clicked card in a dialog over the board, and closing it clears the URL', async () => {
    (card('TASK-1').querySelector('.open') as HTMLElement).click();
    await settle();

    const router = TestBed.inject(Router);
    const dialog = TestBed.inject(MatDialog);
    expect(router.url).toBe('/board?task=TASK-1');
    expect(dialog.openDialogs.length).toBe(1);
    expect(api.task).toHaveBeenCalledWith('TASK-1');
    // The dialog sees the page's providers: its fields are outlined like the rest of the app.
    const fields = document.querySelectorAll('mat-dialog-container .mat-mdc-form-field');
    expect(fields.length).toBeGreaterThan(0);
    fields.forEach(f => expect(f.classList).toContain('mat-form-field-appearance-outline'));

    dialog.openDialogs[0].close();
    await settle();
    expect(router.url).toBe('/board');
  });

  it('closes the dialog when the URL drops the task, as Back does', async () => {
    await harness.navigateByUrl('/board?task=TASK-2');
    await settle();
    const dialog = TestBed.inject(MatDialog);
    expect(dialog.openDialogs.length).toBe(1);

    await harness.navigateByUrl('/board');
    await settle();
    expect(dialog.openDialogs.length).toBe(0);
  });

  it('creates straight into the column whose Create was used, and stays open for the next', async () => {
    (column('in_progress').querySelector('.add-button') as HTMLElement).click();
    harness.detectChanges();
    await settle();

    const field = column('in_progress').querySelector('textarea')!;
    expect(document.activeElement).toBe(field);
    field.value = 'Already on it';
    field.dispatchEvent(new Event('input'));
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await settle();
    harness.detectChanges();

    expect(api.create).toHaveBeenCalledOnceWith(
      { title: 'Already on it', points: null, sprintKey: 'SPRINT-1', column: 'in_progress' }, false);
    expect(column('in_progress').querySelector('textarea')?.value).toBe('');
  });
});
