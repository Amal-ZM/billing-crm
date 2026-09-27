import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

const LOGIN_URL = `${environment.apiBaseUrl}/api/auth/login`;

/**
 * Adds the Bearer token to requests to our own API, and to nothing else. A 401 to a request that carried
 * the person's current token means that session is no longer valid (expired, or otherwise refused), so
 * they are sent back to sign in. A 401 to a request that carried no token, or an old one (a late reply
 * after signing out or in again), is ignored here so it cannot sign out the wrong session.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isApiRequest = request.url.startsWith(`${environment.apiBaseUrl}/`);
  const sentToken = isApiRequest ? auth.session()?.accessToken : undefined;

  if (sentToken) {
    request = request.clone({ setHeaders: { Authorization: `Bearer ${sentToken}` } });
  }

  return next(request).pipe(
    catchError((error: unknown) => {
      // A 401 from the login request itself just means wrong credentials, not an expired session.
      if (
        sentToken &&
        request.url !== LOGIN_URL &&
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        auth.session()?.accessToken === sentToken
      ) {
        auth.signOut();
        const returnUrl = router.url;
        if (!returnUrl.startsWith('/login')) {
          void router.navigate(['/login'], { queryParams: { reason: 'expired', returnUrl } });
        }
      }
      return throwError(() => error);
    }),
  );
};
