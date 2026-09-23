import { Component, OnInit, inject, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { Confirm } from '../core/confirm';
import {
  BoardService,
  WorkItemAttachment,
  WorkItemComment,
  apiError,
  formatWhen,
} from '../core/board.service';

/**
 * Comments and attachments for a task or a goal. Both items behave the same, so one component
 * serves both rather than the task and goal pages growing their own copies.
 */
@Component({
  selector: 'app-discussion',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressBarModule,
  ],
  templateUrl: './discussion.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './discussion.scss',
})
export class Discussion implements OnInit {
  private readonly api = inject(BoardService);
  private readonly confirm = inject(Confirm);

  readonly itemType = input.required<'task' | 'goal'>();
  readonly itemKey = input.required<string>();
  readonly assistantName = input<string>('Assistant');

  protected readonly comments = signal<WorkItemComment[]>([]);
  protected readonly attachments = signal<WorkItemAttachment[]>([]);
  protected readonly uploading = signal(false);
  protected readonly editingId = signal<number | null>(null);

  draft = '';
  editDraft = '';

  /**
   * Loads itself: the parent renders this only once it has the item, and comments do not change
   * when the item does, so waiting for the parent only left the first paint empty.
   */
  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    try {
      const [comments, attachments] = await Promise.all([
        this.api.comments(this.itemType(), this.itemKey()),
        this.api.attachments(this.itemType(), this.itemKey()),
      ]);
      this.comments.set(comments);
      this.attachments.set(attachments);
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not load comments.');
    }
  }

  protected async addComment(): Promise<void> {
    const body = this.draft.trim();
    if (!body) return;
    try {
      await this.api.addComment(this.itemType(), this.itemKey(), body);
      this.draft = '';
      await this.load();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not add that comment.');
    }
  }

  protected startEdit(comment: WorkItemComment): void {
    this.editingId.set(comment.id);
    this.editDraft = comment.body;
  }

  protected async saveEdit(): Promise<void> {
    const id = this.editingId();
    if (id === null || !this.editDraft.trim()) return;
    try {
      await this.api.updateComment(id, this.editDraft.trim());
      this.editingId.set(null);
      await this.load();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not save that comment.');
    }
  }

  protected async deleteComment(comment: WorkItemComment): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete this comment?',
      message: comment.body,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    try {
      await this.api.deleteComment(comment.id);
      await this.load();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not delete that comment.');
    }
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    if (!file) return;
    this.uploading.set(true);
    try {
      await this.api.uploadAttachment(this.itemType(), this.itemKey(), file);
      input.value = '';
      await this.load();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not upload that file.');
    } finally {
      this.uploading.set(false);
    }
  }

  protected async deleteAttachment(attachment: WorkItemAttachment): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${attachment.fileName}?`,
      message: 'The file is removed from the server. This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    try {
      await this.api.deleteAttachment(attachment.id);
      await this.load();
    } catch (e: unknown) {
      this.confirm.error(apiError(e).message ?? 'Could not delete that file.');
    }
  }

  protected url(attachment: WorkItemAttachment): string {
    return this.api.attachmentUrl(attachment.id);
  }

  protected isImage(attachment: WorkItemAttachment): boolean {
    return attachment.contentType.startsWith('image/');
  }

  protected size(bytes: number): string {
    return bytes < 1024
      ? `${bytes} B`
      : bytes < 1024 * 1024
        ? `${Math.round(bytes / 1024)} KB`
        : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected when(value: string): string {
    return formatWhen(value);
  }

  protected author(comment: WorkItemComment): string {
    return comment.author === 'assistant' ? this.assistantName() : 'You';
  }
}
