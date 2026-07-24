import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { GoalNode, GoalsService } from '../core/goals.service';
import { todayLocal } from '../core/local-date';

@Component({
  selector: 'app-goals',
  imports: [FormsModule],
  templateUrl: './goals.html',
  styleUrl: './goals.scss',
})
export class Goals implements OnInit {
  private readonly goals = inject(GoalsService);

  protected readonly tree = signal<GoalNode[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly includeDropped = signal(false);

  /** Parent for the goal being added, or null for a new top-level goal. */
  protected readonly addingUnder = signal<number | null | undefined>(undefined);

  draftTitle = '';
  draftPeriod = 'year';
  draftStart = todayLocal();

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    try {
      this.tree.set(await this.goals.getTree(this.includeDropped()));
      this.error.set(null);
    } catch {
      this.error.set('Could not load your goals.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async toggleDropped(): Promise<void> {
    this.includeDropped.update(v => !v);
    await this.reload();
  }

  protected startAdd(parentId: number | null): void {
    this.addingUnder.set(parentId);
    this.draftTitle = '';
    // A child of a yearly goal is usually a quarter; of a quarter, a month.
    this.draftPeriod = parentId === null ? 'year' : 'month';
  }

  protected cancelAdd(): void {
    this.addingUnder.set(undefined);
  }

  protected async add(): Promise<void> {
    const title = this.draftTitle.trim();
    if (!title) return;

    try {
      await this.goals.create({
        title,
        periodType: this.draftPeriod,
        periodStart: this.draftStart,
        parentGoalId: this.addingUnder() ?? null,
      });
      this.addingUnder.set(undefined);
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.messageFrom(e, 'Could not add that goal.'));
    }
  }

  protected async setStatus(goal: GoalNode, status: string): Promise<void> {
    try {
      await this.goals.updateStatus(goal.id, status);
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.messageFrom(e, 'Could not update that goal.'));
    }
  }

  protected async setProgress(goal: GoalNode, value: string): Promise<void> {
    const progress = Number(value);
    if (Number.isNaN(progress)) return;
    try {
      await this.goals.updateProgress(goal.id, progress);
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.messageFrom(e, 'Could not update progress.'));
    }
  }

  protected async remove(goal: GoalNode): Promise<void> {
    const extra = goal.children.length > 0 ? ' and everything under it' : '';
    if (!confirm(`Delete "${goal.title}"${extra}?`)) return;

    try {
      await this.goals.delete(goal.id);
      await this.reload();
    } catch {
      this.error.set('Could not delete that goal.');
    }
  }

  /** Flattens the tree so the template can render it without recursion. */
  protected flatten(nodes: GoalNode[], depth = 0): { goal: GoalNode; depth: number }[] {
    return nodes.flatMap(goal => [
      { goal, depth },
      ...this.flatten(goal.children, depth + 1),
    ]);
  }

  private messageFrom(error: unknown, fallback: string): string {
    return (error as { error?: { error?: string } })?.error?.error ?? fallback;
  }
}
