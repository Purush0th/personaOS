import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IsActiveMatchOptions, RouterLink, RouterLinkActive } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';

/** A section's views, in tab order. */
const SECTIONS = {
  board: {
    title: 'Board',
    views: [
      { path: '/board', label: 'Sprint' },
      { path: '/board/backlog', label: 'Backlog' },
      { path: '/board/reports', label: 'Reports' },
    ],
  },
  goals: {
    title: 'Goals',
    views: [
      { path: '/goals', label: 'Goals' },
      { path: '/goals/timeline', label: 'Timeline' },
    ],
  },
} as const;

/**
 * A section's frame: its title and its views as tabs, the way Jira lists Backlog, Board and
 * Reports across the top of a project, around the view itself. Used by the board (Sprint,
 * Backlog, Reports) and by goals (Goals, Timeline). A page passes its header actions with the
 * `pageActions` attribute; everything else it passes is the view, which sits in the tab panel
 * the tabs belong to.
 */
@Component({
  selector: 'app-page-tabs',
  imports: [MatTabsModule, RouterLink, RouterLinkActive],
  template: `
    <header>
      <h1>{{ title() }}</h1>
      <nav mat-tab-nav-bar class="views" [attr.aria-label]="title() + ' views'" [tabPanel]="panel">
        @for (view of views(); track view.path) {
          <a
            mat-tab-link
            [routerLink]="view.path"
            routerLinkActive
            #active="routerLinkActive"
            [routerLinkActiveOptions]="match"
            [active]="active.isActive"
          >{{ view.label }}</a>
        }
      </nav>
      <span class="spacer"></span>
      <ng-content select="[pageActions]" />
    </header>
    <mat-tab-nav-panel #panel>
      <ng-content />
    </mat-tab-nav-panel>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }

    header {
      display: flex;
      align-items: center;
      gap: 0.5rem 1rem;
      flex-wrap: wrap;
    }

    h1 {
      font: var(--mat-sys-headline-small);
      margin: 0;
    }

    .views {
      border-bottom: none;
    }

    .spacer {
      flex: 1;
    }
  `,
})
export class PageTabs {
  readonly section = input.required<keyof typeof SECTIONS>();

  protected readonly title = computed(() => SECTIONS[this.section()].title);
  protected readonly views = computed(() => SECTIONS[this.section()].views);

  /** Exact paths, so Sprint (or Goals) is not also lit on the others; ?task= (an open dialog) is ignored. */
  protected readonly match: IsActiveMatchOptions = {
    paths: 'exact',
    queryParams: 'ignored',
    matrixParams: 'ignored',
    fragment: 'ignored',
  };
}
