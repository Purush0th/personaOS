import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

import {
  BoardColumn,
  BoardTask,
  COLUMN_LABELS,
  PRIORITY_ICONS,
  PRIORITY_LABELS,
  Priority,
} from '../core/board.service';

type SortColumn = 'key' | 'title' | 'status' | 'priority' | 'points';

const STATUS_ORDER: BoardColumn[] = ['backlog', 'todo', 'in_progress', 'done'];
const PRIORITY_ORDER: Priority[] = ['highest', 'high', 'medium', 'low', 'lowest'];

/** How each column orders two tasks, ascending. Missing points sort after any estimate. */
const COMPARE: Record<SortColumn, (a: BoardTask, b: BoardTask) => number> = {
  key: (a, b) => a.id - b.id,
  title: (a, b) => a.title.localeCompare(b.title),
  status: (a, b) => STATUS_ORDER.indexOf(a.column) - STATUS_ORDER.indexOf(b.column),
  priority: (a, b) => PRIORITY_ORDER.indexOf(a.priority) - PRIORITY_ORDER.indexOf(b.priority),
  points: (a, b) => (a.points ?? Infinity) - (b.points ?? Infinity),
};

/**
 * A list of tasks as Jira's "Work item details" table: a search box over it, columns that sort
 * when their heading is clicked, and each task opening in the task dialog (`?task=`).
 */
@Component({
  selector: 'app-work-item-table',
  imports: [MatIconModule, RouterLink],
  templateUrl: './work-item-table.html',
  styleUrl: './work-item-table.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkItemTable {
  readonly tasks = input.required<readonly BoardTask[]>();

  protected readonly query = signal('');
  protected readonly sort = signal<{ column: SortColumn; descending: boolean }>({
    column: 'key',
    descending: false,
  });

  protected readonly columns: { id: SortColumn; label: string; numeric?: boolean }[] = [
    { id: 'key', label: 'Key' },
    { id: 'title', label: 'Summary' },
    { id: 'status', label: 'Status' },
    { id: 'priority', label: 'Priority' },
    { id: 'points', label: 'Points', numeric: true },
  ];

  protected readonly columnLabels = COLUMN_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly priorityIcons = PRIORITY_ICONS;

  /** The tasks that match the search, in the chosen order. */
  protected readonly rows = computed(() => {
    const words = this.query().trim().toLowerCase().split(/\s+/).filter(Boolean);
    const { column, descending } = this.sort();
    const compare = COMPARE[column];
    return this.tasks()
      .filter(t => {
        const text = `${t.key} ${t.title} ${t.goalTitle ?? ''}`.toLowerCase();
        return words.every(w => text.includes(w));
      })
      .sort((a, b) => (descending ? compare(b, a) : compare(a, b)));
  });

  /** A first click sorts by the column; another click on it reverses the order. */
  protected sortBy(column: SortColumn): void {
    this.sort.update(s => ({ column, descending: s.column === column && !s.descending }));
  }

  protected ariaSort(column: SortColumn): 'ascending' | 'descending' | 'none' {
    const s = this.sort();
    if (s.column !== column) return 'none';
    return s.descending ? 'descending' : 'ascending';
  }
}
