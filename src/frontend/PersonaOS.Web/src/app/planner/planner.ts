import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { BoardService, BoardTask, goalHue } from '../core/board.service';
import { BrandingService } from '../core/branding.service';
import { Confirm } from '../core/confirm';
import { shiftLocalDate, todayLocal } from '../core/local-date';
import { PlannerItemDto, PlannerService } from '../core/planner.service';

@Component({
  selector: 'app-planner',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './planner.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './planner.scss',
})
export class Planner implements OnInit {
  private readonly planner = inject(PlannerService);
  private readonly boardApi = inject(BoardService);
  private readonly branding = inject(BrandingService);
  private readonly confirm = inject(Confirm);

  /** The running sprint's unfinished tasks, to pick the day's work from. */
  protected readonly sprintTasks = signal<BoardTask[]>([]);

  protected readonly items = signal<PlannerItemDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly date = signal(todayLocal());

  draftTitle = '';
  draftTime = '';

  async ngOnInit(): Promise<void> {
    await Promise.all([this.reload(), this.loadSprintTasks()]);
  }

  private async loadSprintTasks(): Promise<void> {
    if (!this.branding.isEnabled('board')) return;
    try {
      const board = await this.boardApi.board();
      this.sprintTasks.set([...board.inProgress, ...board.todo]);
    } catch {
      this.sprintTasks.set([]);
    }
  }

  /** Plans a board task for this day; the item takes the task's title and goal. */
  protected async pickTask(value: string): Promise<void> {
    const taskId = Number(value);
    if (!taskId) return;
    try {
      await this.planner.create({ title: '', date: this.date(), taskId, scheduledTime: this.draftTime || null });
      this.draftTime = '';
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(this.messageFrom(e, 'Could not plan that task.'));
    }
  }

  protected hue(goalKey: string | null): number {
    return goalHue(goalKey);
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const day = await this.planner.getDay(this.date());
      this.items.set(day.items);
    } catch {
      this.confirm.error('Could not load that day.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async shiftDay(days: number): Promise<void> {
    this.date.set(shiftLocalDate(this.date(), days));
    await this.reload();
  }

  protected async onDateChange(value: string): Promise<void> {
    if (!value) return;
    this.date.set(value);
    await this.reload();
  }

  protected async add(): Promise<void> {
    const title = this.draftTitle.trim();
    if (!title) return;

    try {
      await this.planner.create({
        title,
        date: this.date(),
        scheduledTime: this.draftTime || null,
      });
      this.draftTitle = '';
      this.draftTime = '';
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(this.messageFrom(e, 'Could not add that task.'));
    }
  }

  protected async cycleStatus(item: PlannerItemDto): Promise<void> {
    // planned → done → skipped → planned
    const next =
      item.status === 'planned' ? 'done' : item.status === 'done' ? 'skipped' : 'planned';
    try {
      await this.planner.updateStatus(item.id, next);
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(this.messageFrom(e, 'Could not update that task.'));
    }
  }

  protected async moveToTomorrow(item: PlannerItemDto): Promise<void> {
    try {
      await this.planner.move(item.id, shiftLocalDate(this.date(), 1));
      await this.reload();
    } catch (e: unknown) {
      this.confirm.error(this.messageFrom(e, 'Could not move that task.'));
    }
  }

  protected async remove(item: PlannerItemDto): Promise<void> {
    try {
      await this.planner.delete(item.id);
      await this.reload();
    } catch {
      this.confirm.error('Could not delete that task.');
    }
  }

  protected get doneCount(): number {
    return this.items().filter(i => i.status === 'done').length;
  }

  private messageFrom(error: unknown, fallback: string): string {
    return (error as { error?: { error?: string } })?.error?.error ?? fallback;
  }
}
