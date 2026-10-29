import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnInit, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';

import { apiError } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { NOTE_MAX_LENGTH, Note, NotesService } from '../core/notes.service';

/**
 * Notes, after Google Keep: a "Take a note…" box on top and the notes as cards below, the latest
 * edited first. A note is a place to park a thought, not to keep it: each card moves to the
 * board's backlog in one click (which removes the note), or is deleted, with an undo.
 */
@Component({
  selector: 'app-notes',
  imports: [FormsModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  templateUrl: './notes.html',
  styleUrl: './notes.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class Notes implements OnInit, AfterViewInit {
  private readonly notes = inject(NotesService);
  private readonly confirm = inject(Confirm);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  protected readonly branding = inject(BrandingService);

  protected readonly maxLength = NOTE_MAX_LENGTH;
  protected readonly items = signal<Note[]>([]);
  protected readonly loading = signal(true);

  /**
   * What is in the box on top, for showing Save. The box itself is read and cleared directly: a
   * bound value is not written back when it changes and changes back before the page redraws,
   * which fast typing does (a saved note stayed in the box).
   */
  protected readonly draft = signal('');
  private readonly draftBox = viewChild.required<ElementRef<HTMLTextAreaElement>>('draftBox');
  protected readonly search = signal('');

  /** The note being edited in place, and its text so far. */
  protected readonly editing = signal<number | null>(null);
  protected readonly editText = signal('');
  private readonly editBox = viewChild<ElementRef<HTMLTextAreaElement>>('editBox');

  constructor() {
    // A card opened for editing takes the typing, the caret at the end, as in Keep. Done when its
    // box appears: a timer could fire before the page redraws and leave the typing elsewhere.
    effect(() => {
      const box = this.editBox()?.nativeElement;
      if (!box) return;
      // ngModel fills the box a moment later; filling it now puts the caret after the text.
      box.value = untracked(this.editText);
      box.focus();
      box.setSelectionRange(box.value.length, box.value.length);
    });
  }

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  /**
   * The box on top takes the typing at once, as in Keep, where there is a keyboard. Not on a touch
   * screen: there the on-screen keyboard would open over the notes.
   */
  ngAfterViewInit(): void {
    if (window.matchMedia('(pointer: fine)').matches) this.draftBox().nativeElement.focus();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await this.notes.list(this.search().trim() || undefined));
    } catch {
      this.confirm.error('Could not load notes.');
    } finally {
      this.loading.set(false);
    }
  }

  // --- Writing -------------------------------------------------------------------------

  /** Enter saves and leaves the box open for the next note; Shift+Enter starts a new line. */
  protected async onDraftKey(event: KeyboardEvent): Promise<void> {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      await this.add();
    } else if (event.key === 'Escape') {
      this.setDraft('');
    }
  }

  /**
   * The box is cleared at once, so the next thought can be typed while this one saves (clearing
   * it when the save answered wiped what had been typed since). A note that could not be saved
   * goes back into the box, unless something new is being written there.
   */
  protected async add(): Promise<void> {
    const text = this.draftBox().nativeElement.value.trim();
    if (!text) return;
    this.setDraft('');
    try {
      const note = await this.notes.create(text);
      this.items.update(items => [note, ...items]);
    } catch (e: unknown) {
      if (!this.draftBox().nativeElement.value.trim()) this.setDraft(text);
      this.confirm.error(apiError(e).message ?? 'Could not save the note.');
    }
  }

  private setDraft(text: string): void {
    this.draftBox().nativeElement.value = text;
    this.draft.set(text);
  }

  protected edit(note: Note): void {
    this.editing.set(note.id);
    this.editText.set(note.text);
  }

  /** Ctrl+Enter (or leaving the box) saves; Escape puts the note back as it was. */
  protected async onEditKey(event: KeyboardEvent, note: Note): Promise<void> {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.editing.set(null);
    } else if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      await this.saveEdit(note);
    }
  }

  protected async saveEdit(note: Note): Promise<void> {
    if (this.editing() !== note.id) return;
    this.editing.set(null);
    const text = this.editText().trim();
    if (text === note.text) return;
    if (!text) {
      // Emptying a note is deleting it, as in Keep.
      await this.remove(note);
      return;
    }
    try {
      const saved = await this.notes.update(note.id, text);
      this.items.update(items => [saved, ...items.filter(n => n.id !== note.id)]);
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not save the note.');
    }
  }

  // --- Clearing ------------------------------------------------------------------------

  protected async remove(note: Note): Promise<void> {
    try {
      await this.notes.delete(note.id);
      this.items.update(items => items.filter(n => n.id !== note.id));
    } catch {
      this.confirm.error('Could not delete the note.');
      return;
    }
    const undo = this.snackBar.open('Note deleted', 'Undo', { duration: 6000 });
    undo.onAction().subscribe(() => void this.restore(note.text));
  }

  private async restore(text: string): Promise<void> {
    try {
      const note = await this.notes.create(text);
      this.items.update(items => [note, ...items]);
    } catch {
      this.confirm.error('Could not bring the note back.');
    }
  }

  protected async toBacklog(note: Note): Promise<void> {
    try {
      const task = await this.notes.moveToBacklog(note.id);
      this.items.update(items => items.filter(n => n.id !== note.id));
      const open = this.snackBar.open(`Moved to the backlog as ${task.key}`, 'Open', { duration: 6000 });
      open.onAction().subscribe(() => void this.router.navigate(['/board/tasks', task.key]));
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not move the note.');
    }
  }
}
