import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from './core/auth.service';
import { BrandingService } from './core/branding.service';
import { SetupWizard } from './setup/setup-wizard';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SetupWizard],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  protected readonly brandingService = inject(BrandingService);
  protected readonly auth = inject(AuthService);

  ngOnInit(): void {
    void this.brandingService.load();
  }

  onSetupCompleted(): void {
    void this.brandingService.load();
  }

  protected isEnabled(feature: string): boolean {
    return this.brandingService.isEnabled(feature);
  }
}
