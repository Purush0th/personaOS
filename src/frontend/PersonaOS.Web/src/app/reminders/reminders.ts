import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ReminderDto, RemindersService } from '../core/reminders.service';

@Component({
  selector: 'app-reminders',
  imports: [FormsModule, DatePipe],
  templateUrl: './reminders.html',
  styleUrl: './reminders.scss',
})
export class Reminders implements OnInit {
  private readonly reminders = inject(RemindersService);

  protected readonly items = signal<ReminderDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
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
      this.error.set(null);
    } catch {
      this.error.set('Could not load reminders.');
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
      this.error.set(this.messageFrom(e, 'Could not create that reminder.'));
    }
  }

  protected async cancel(item: ReminderDto): Promise<void> {
    try {
      await this.reminders.cancel(item.id);
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.messageFrom(e, 'Could not cancel that reminder.'));
    }
  }

  protected async remove(item: ReminderDto): Promise<void> {
    try {
      await this.reminders.delete(item.id);
      await this.reload();
    } catch {
      this.error.set('Could not delete that reminder.');
    }
  }

  private messageFrom(error: unknown, fallback: string): string {
    return (error as { error?: { error?: string } })?.error?.error ?? fallback;
  }
}
