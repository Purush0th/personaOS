import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

import { AuthService } from './core/auth.service';
import { BrandingService } from './core/branding.service';
import { UpdatesService } from './core/updates.service';
import { SetupWizard } from './setup/setup-wizard';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  /** Module that must be enabled, or null when the page is always available. */
  feature: string | null;
}

/** Every page in the app, in the order they appear in the nav. */
const NAV: NavItem[] = [
  { path: '/chat', label: 'Chat', icon: 'chat', feature: null },
  { path: '/goals', label: 'Goals', icon: 'flag', feature: 'goals' },
  { path: '/board', label: 'Board', icon: 'view_kanban', feature: 'board' },
  { path: '/planner', label: 'Planner', icon: 'today', feature: 'planner' },
  { path: '/reminders', label: 'Reminders', icon: 'alarm', feature: 'reminders' },
  { path: '/documents', label: 'Documents', icon: 'description', feature: 'docs' },
  { path: '/settings', label: 'Settings', icon: 'settings', feature: null },
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
    NAV.filter(item => item.feature === null || this.brandingService.isEnabled(item.feature))
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
}
