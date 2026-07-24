import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { AuthService } from '../core/auth.service';
import { BrandingService } from '../core/branding.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <section class="card">
      <h1>Sign in</h1>
      <p class="muted">
        Use the admin account you created during setup.
      </p>

      <form (ngSubmit)="submit()">
        <label>
          Username
          <input name="username" [(ngModel)]="username" autocomplete="username" autofocus />
        </label>
        <label>
          Password
          <input name="password" type="password" [(ngModel)]="password" autocomplete="current-password" />
        </label>

        @if (error(); as message) {
          <p class="error">{{ message }}</p>
        }

        <button type="submit" [disabled]="busy()">
          {{ busy() ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
    </section>
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly branding = inject(BrandingService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  username = '';
  password = '';

  async submit(): Promise<void> {
    if (!this.username.trim() || !this.password) {
      this.error.set('Enter your username and password.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const message = await this.auth.login(this.username.trim(), this.password);
    if (message) {
      this.error.set(message);
      this.busy.set(false);
      return;
    }

    await this.router.navigate(['/chat']);
  }
}
