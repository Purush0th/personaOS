import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { Confirm } from '../core/confirm';
import { ReminderDto, RemindersService } from '../core/reminders.service';

@Component({
  selector: 'app-reminders',
  imports: [
    FormsModule,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressBarModule,
  ],
  templateUrl: './reminders.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './reminders.scss',
})
export class Reminders implements OnInit {
  private readonly reminders = inject(RemindersService);
  private readonly confirm = inject(Confirm);

  protected readonly items = signal<ReminderDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly includeCompleted = signal(false);

  draftMessage = '';
  draftDue = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await this.reminders.list(this.includeCompleted()));
    } catch {
      this.confirm.error('Could not load reminders.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async toggleCompleted(): Promise<void> {
    this.includeCompleted.update(v => !v);
    await this.reload();
  }

  protected async add(): Promise<void> {
    const message = this.draftMessage.trim();
    if (!message || !this.draftDue) return;

    try {
      // The datetime-local value is a wall-clock time; the server reads it in the
      // install's configured zone, which is the zone the user actually means.
      await this.reminders.create(message, this.draftDue);
      this.draftMessage = '';
      this.draftDue = '';
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(this.messageFrom(e, 'Could not create that reminder.'));
    }
  }

  protected async cancel(item: ReminderDto): Promise<void> {
    try {
      await this.reminders.cancel(item.id);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(this.messageFrom(e, 'Could not cancel that reminder.'));
    }
  }

  protected async remove(item: ReminderDto): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete this reminder?',
      message: item.message,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;

    try {
      await this.reminders.delete(item.id);
      await this.reload();
    } catch {
      this.confirm.error('Could not delete that reminder.');
    }
  }

  private messageFrom(error: unknown, fallback: string): string {
    return (error as { error?: { error?: string } })?.error?.error ?? fallback;
  }
}
