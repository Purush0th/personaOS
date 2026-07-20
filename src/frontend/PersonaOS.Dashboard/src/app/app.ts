import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BrandingService } from './core/branding.service';
import { SetupWizard } from './setup/setup-wizard';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, SetupWizard],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  protected readonly brandingService = inject(BrandingService);

  ngOnInit(): void {
    void this.brandingService.load();
  }

  onSetupCompleted(): void {
    void this.brandingService.load();
  }
}
