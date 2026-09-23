import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

import { AuthService } from './core/auth.service';
import { BrandingService } from './core/branding.service';
import { ConversationSummary } from './core/chat.service';
import { ConversationsStore } from './core/conversations.store';
import { ThemeService } from './core/theme.service';
import { UpdatesService } from './core/updates.service';
import { SetupWizard } from './setup/setup-wizard';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  /** Module that must be enabled for the page to show. */
  feature: string;
}

/** Whether the chat history under Chat in the nav is expanded; remembered per browser. */
const HISTORY_KEY = 'personaos.nav.history';

/**
 * The module pages, in nav order. Chat is not here: it comes last, as its own group, because its
 * history grows and takes whatever height is left. Settings lives in the account menu.
 */
const NAV: NavItem[] = [
  { path: '/goals', label: 'Goals', icon: 'flag', feature: 'goals' },
  { path: '/board', label: 'Board', icon: 'view_kanban', feature: 'board' },
  { path: '/planner', label: 'Planner', icon: 'today', feature: 'planner' },
  { path: '/reminders', label: 'Reminders', icon: 'alarm', feature: 'reminders' },
  { path: '/documents', label: 'Documents', icon: 'description', feature: 'docs' },
];

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    SetupWizard,
    MatSidenavModule,
    MatListModule,
    MatMenuModule,
    MatIconModule,
    MatButtonModule,
    MatToolbarModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly brandingService = inject(BrandingService);
  protected readonly auth = inject(AuthService);
  protected readonly updates = inject(UpdatesService);
  protected readonly theme = inject(ThemeService);
  protected readonly conversations = inject(ConversationsStore);

  protected readonly signedIn = computed(
    () => !!this.brandingService.branding()?.isConfigured && this.auth.isLoggedIn()
  );

  protected readonly historyOpen = signal(readHistoryOpen());

  constructor() {
    // The history is in the nav, so it loads with the shell rather than with the chat page.
    effect(() => {
      if (this.signedIn()) void this.conversations.refresh();
    });
  }

  /**
   * True when the window is too narrow for a permanent rail, so the nav becomes a drawer over
   * the page. A width query, not Breakpoints.Handset: that counts any short landscape window as
   * a phone, and hid the rail on a 900px desktop browser.
   */
  protected readonly handset = toSignal(
    this.breakpoints.observe('(max-width: 839px)').pipe(map(result => result.matches)),
    { initialValue: false }
  );

  protected readonly navItems = computed(() =>
    NAV.filter(item => this.brandingService.isEnabled(item.feature))
  );

  async ngOnInit(): Promise<void> {
    await this.brandingService.load();
    void this.updates.check();
  }

  onSetupCompleted(): void {
    void this.brandingService.load();
  }

  /** On a phone the drawer covers the page, so following a link has to close it. */
  protected closeOnHandset(drawer: MatSidenav): void {
    if (this.handset()) void drawer.close();
  }

  /**
   * The expander and the delete buttons sit inside nav links, so their clicks must neither
   * reach the link's router handler nor let the browser follow the href.
   */
  protected toggleHistory(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    const open = !this.historyOpen();
    this.historyOpen.set(open);
    try {
      localStorage.setItem(HISTORY_KEY, String(open));
    } catch {
      // Storage refused (private window): the toggle still works for this visit.
    }
  }

  protected deleteConversation(conversation: ConversationSummary, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    void this.conversations.remove(conversation);
  }
}

function readHistoryOpen(): boolean {
  try {
    return localStorage.getItem(HISTORY_KEY) !== 'false';
  } catch {
    return true;
  }
}
