import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { Confirm } from '../core/confirm';
import {
  MEMORY_CATEGORIES,
  MEMORY_MAX_LENGTH,
  Memory,
  MemoriesService,
  categoryLabel,
} from '../core/memories.service';

/**
 * What the assistant remembers across conversations: every memory, to read, edit and delete, and
 * the auto-save switch. A section of Settings on the web (the phone has its own screen).
 *
 * Kept apart from the main settings form, like push: each change here saves on its own, and must
 * not wait for — or be re-sent by — "Save settings".
 */
@Component({
  selector: 'app-memories',
  imports: [
    FormsModule,
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
  ],
  templateUrl: './memories.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './memories.scss',
})
export class Memories implements OnInit {
  private readonly memories = inject(MemoriesService);
  private readonly confirm = inject(Confirm);

  protected readonly categories = MEMORY_CATEGORIES;
  protected readonly max = MEMORY_MAX_LENGTH;
  protected readonly label = categoryLabel;

  protected readonly items = signal<Memory[]>([]);
  protected readonly loaded = signal(false);
  protected readonly autoSave = signal(true);
  protected readonly search = signal('');
  protected readonly category = signal<string | null>(null);

  /** The memory being edited, or 'new' for the add form; null when neither is open. */
  protected readonly editing = signal<number | 'new' | null>(null);
  protected draftContent = '';
  protected draftCategory: string = 'fact';

  /** Filtered here rather than on the server: the list is small and filtering stays instant. */
  protected readonly shown = computed(() => {
    const words = this.search().trim().toLowerCase().split(/\s+/).filter(w => w.length > 0);
    const category = this.category();
    return this.items().filter(
      m =>
        (!category || m.category === category) &&
        words.every(w => m.content.toLowerCase().includes(w)),
    );
  });

  async ngOnInit(): Promise<void> {
    try {
      const [items, autoSave] = await Promise.all([this.memories.list(), this.memories.getAutoSave()]);
      this.items.set(items);
      this.autoSave.set(autoSave);
    } catch {
      this.confirm.error('Could not load memories.');
    } finally {
      this.loaded.set(true);
    }
  }

  protected async toggleAutoSave(on: boolean): Promise<void> {
    try {
      this.autoSave.set(await this.memories.setAutoSave(on));
    } catch {
      this.autoSave.set(!on);
      this.confirm.error('Could not change auto-save.');
    }
  }

  protected startAdd(): void {
    this.draftContent = '';
    this.draftCategory = 'fact';
    this.editing.set('new');
  }

  protected startEdit(memory: Memory): void {
    this.draftContent = memory.content;
    this.draftCategory = memory.category;
    this.editing.set(memory.id);
  }

  protected cancel(): void {
    this.editing.set(null);
  }

  protected async saveDraft(): Promise<void> {
    const content = this.draftContent.trim();
    const editing = this.editing();
    if (!content || editing === null) return;

    try {
      if (editing === 'new') {
        const saved = await this.memories.create(content, this.draftCategory);
        // Saving what is already there refreshes it rather than adding a second one.
        this.items.update(list => [saved, ...list.filter(m => m.id !== saved.id)]);
      } else {
        const saved = await this.memories.update(editing, content, this.draftCategory);
        this.items.update(list => list.map(m => (m.id === saved.id ? saved : m)));
      }
      this.editing.set(null);
    } catch (e: unknown) {
      this.confirm.error((e as { error?: { error?: string } })?.error?.error ?? 'Could not save the memory.');
    }
  }

  protected async remove(memory: Memory): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete memory?',
      message: `“${memory.content}” will be forgotten.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;

    try {
      await this.memories.delete(memory.id);
      this.items.update(list => list.filter(m => m.id !== memory.id));
    } catch {
      this.confirm.error('Could not delete the memory.');
    }
  }
}
