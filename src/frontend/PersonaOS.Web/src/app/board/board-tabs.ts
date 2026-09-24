import { ChangeDetectionStrategy, Component } from '@angular/core';
import { IsActiveMatchOptions, RouterLink, RouterLinkActive } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';

/**
 * The board's frame: its title and its views as tabs, the way Jira lists Backlog, Board and
 * Reports across the top of a project, around the view itself. A page passes its header actions
 * with the `boardActions` attribute; everything else it passes is the view, which sits in the
 * tab panel the tabs belong to.
 */
@Component({
  selector: 'app-board-tabs',
  imports: [MatTabsModule, RouterLink, RouterLinkActive],
  template: `
    <header>
      <h1>Board</h1>
      <nav mat-tab-nav-bar class="views" aria-label="Board views" [tabPanel]="panel">
        @for (view of views; track view.path) {
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
      <ng-content select="[boardActions]" />
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
export class BoardTabs {
  protected readonly views = [
    { path: '/board', label: 'Sprint' },
    { path: '/board/backlog', label: 'Backlog' },
    { path: '/board/reports', label: 'Reports' },
  ];

  /** Exact paths, so Sprint is not also lit on the others; ?task= (an open dialog) is ignored. */
  protected readonly match: IsActiveMatchOptions = {
    paths: 'exact',
    queryParams: 'ignored',
    matrixParams: 'ignored',
    fragment: 'ignored',
  };
}
