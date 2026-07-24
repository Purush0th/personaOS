import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  username: string;
}

const TOKEN_KEY = 'personaos.token';

/**
 * Holds the admin session. The token lives in localStorage so a refresh doesn't
 * log you out — acceptable for a single-user, self-hosted instance that is not
 * exposed to the internet by default.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly token = signal<string | null>(localStorage.getItem(TOKEN_KEY));

  readonly isLoggedIn = computed(() => this.token() !== null);

  get accessToken(): string | null {
    return this.token();
  }

  /** Returns null on success, or a message to show the user. */
  async login(username: string, password: string): Promise<string | null> {
    try {
      const response = await firstValueFrom(
        this.http.post<LoginResponse>('/api/auth/login', { username, password })
      );
      localStorage.setItem(TOKEN_KEY, response.accessToken);
      this.token.set(response.accessToken);
      return null;
    } catch (error: unknown) {
      const status = (error as { status?: number })?.status;
      if (status === 401) return 'Incorrect username or password.';
      return 'Could not reach the server.';
    }
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
    void this.router.navigate(['/login']);
  }

  /** Called by the interceptor when the server rejects the token. */
  sessionExpired(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
    void this.router.navigate(['/login']);
  }
}
