import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';

import { AuthService } from '../core/auth.service';
import { BrandingService } from '../core/branding.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  template: `
    <mat-card class="sign-in" appearance="outlined">
      <mat-card-header>
        <mat-card-title>Sign in</mat-card-title>
        <mat-card-subtitle>Use the admin account you created during setup.</mat-card-subtitle>
      </mat-card-header>

      <mat-card-content>
        <form (ngSubmit)="submit()">
          <mat-form-field appearance="outline">
            <mat-label>Username</mat-label>
            <input matInput name="username" [(ngModel)]="username" autocomplete="username" autofocus />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Password</mat-label>
            <input
              matInput
              name="password"
              type="password"
              [(ngModel)]="password"
              autocomplete="current-password"
            />
            @if (error(); as message) {
              <mat-error>{{ message }}</mat-error>
            }
          </mat-form-field>

          <button mat-flat-button type="submit" [disabled]="busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .sign-in {
      max-width: 24rem;
      margin: 3rem auto;
    }

    form {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      padding-top: 0.5rem;
    }
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
