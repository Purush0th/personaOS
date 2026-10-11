import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';

import { BoardTask } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { Note, NotesService } from '../core/notes.service';
import { Notes } from './notes';

function note(id: number, text: string): Note {
  return { id, text, createdAtUtc: '2026-10-10T00:00:00Z', updatedAtUtc: '2026-10-10T00:00:00Z' };
}

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Notes', () => {
  let api: jasmine.SpyObj<NotesService>;
  let snack: jasmine.SpyObj<MatSnackBar>;
  let undo: Subject<void>;

  async function render(): Promise<{ el: HTMLElement; detect: () => void }> {
    const fixture = TestBed.createComponent(Notes);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, detect: () => fixture.detectChanges() };
  }

  const texts = (el: HTMLElement) => Array.from(el.querySelectorAll('.note .text')).map(e => e.textContent?.trim());

  beforeEach(() => {
    api = jasmine.createSpyObj<NotesService>('NotesService', ['list', 'create', 'update', 'delete', 'moveToBacklog']);
    api.list.and.resolveTo([note(2, 'Buy batteries'), note(1, 'Fix the door\nThe hinge squeaks')]);
    api.delete.and.resolveTo();
    undo = new Subject<void>();
    snack = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    snack.open.and.returnValue({ onAction: () => undo.asObservable() } as ReturnType<MatSnackBar['open']>);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: NotesService, useValue: api },
        { provide: MatSnackBar, useValue: snack },
        { provide: Confirm, useValue: jasmine.createSpyObj<Confirm>('Confirm', ['error', 'ask', 'done']) },
        { provide: BrandingService, useValue: { isEnabled: () => true } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  it('lists the notes as cards, the latest first', async () => {
    const { el } = await render();

    expect(texts(el)).toEqual(['Buy batteries', 'Fix the door\nThe hinge squeaks']);
  });

  it('Enter saves a note and leaves the box empty for the next', async () => {
    api.create.and.resolveTo(note(3, 'Call the plumber'));
    const { el, detect } = await render();
    const box = el.querySelector('[data-testid="note-draft"]') as HTMLTextAreaElement;

    box.value = 'Call the plumber';
    box.dispatchEvent(new Event('input'));
    detect();
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    detect();

    // ngModel writes the cleared value back to the box after the next check.
    await settle();

    expect(api.create).toHaveBeenCalledOnceWith('Call the plumber');
    expect(texts(el)[0]).toBe('Call the plumber');
    expect(box.value).toBe('');
  });

  it('the next note can be typed while one is saving', async () => {
    // Found by typing fast: clearing the box when the save answered wiped the next note.
    let answer!: (n: Note) => void;
    api.create.and.returnValue(new Promise<Note>(resolve => (answer = resolve)));
    const { el, detect } = await render();
    const box = el.querySelector('[data-testid="note-draft"]') as HTMLTextAreaElement;

    box.value = 'Renew car insurance';
    box.dispatchEvent(new Event('input'));
    detect();
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    detect();
    await settle();
    box.value = 'Order printer ink';
    box.dispatchEvent(new Event('input'));
    detect();
    answer(note(3, 'Renew car insurance'));
    await settle();
    detect();
    await settle();

    expect(box.value).toBe('Order printer ink');
  });

  it('deleting can be undone', async () => {
    api.create.and.resolveTo(note(4, 'Buy batteries'));
    const { el, detect } = await render();

    (el.querySelector('[data-testid="note-delete"]') as HTMLButtonElement).click();
    await settle();
    detect();
    expect(api.delete).toHaveBeenCalledOnceWith(2);
    expect(texts(el)).toEqual(['Fix the door\nThe hinge squeaks']);

    undo.next();
    await settle();
    detect();
    expect(api.create).toHaveBeenCalledOnceWith('Buy batteries');
    expect(texts(el)[0]).toBe('Buy batteries');
  });

  it('moving to the backlog takes the note away and names the task', async () => {
    api.moveToBacklog.and.resolveTo({ key: 'TASK-12' } as BoardTask);
    const { el, detect } = await render();

    (el.querySelectorAll('[data-testid="note-to-backlog"]')[1] as HTMLButtonElement).click();
    await settle();
    detect();

    expect(api.moveToBacklog).toHaveBeenCalledOnceWith(1);
    expect(texts(el)).toEqual(['Buy batteries']);
    expect(snack.open).toHaveBeenCalledWith('Moved to the backlog as TASK-12', 'Open', jasmine.anything());
  });
});
