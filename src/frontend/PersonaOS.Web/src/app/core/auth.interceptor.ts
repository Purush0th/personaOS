import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * Attaches the bearer token to API calls and signs the user out when the server
 * says the token is no longer good. Login and the unauthenticated branding /
 * setup endpoints are left alone.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken;

  const isAuthEndpoint = req.url.includes('/api/auth/login');
  const authed = token && !isAuthEndpoint
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authed).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthEndpoint) {
        auth.sessionExpired();
      }
      return throwError(() => error);
    })
  );
};
