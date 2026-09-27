import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Role } from './auth.models';
import { AuthService } from './auth.service';

/** Sends the person to /login, saying why when their session ran out, and remembering where they were going. */
function toLogin(router: Router, expired: boolean, returnUrl: string): UrlTree {
  return router.createUrlTree(['/login'], {
    queryParams: expired ? { reason: 'expired', returnUrl } : { returnUrl },
  });
}

/** Signed-out visitors go to /login and come back to the page they asked for afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const check = inject(AuthService).checkSession();

  return check === 'active' ? true : toLogin(router, check === 'expired', state.url);
};

/**
 * Only the given role may open the route. A signed-out or expired visitor goes to /login; a signed-in
 * person with the wrong role sees "access denied". The API enforces the same rule; this only keeps the
 * UI honest.
 */
export const roleGuard =
  (role: Role): CanActivateFn =>
  (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    const check = auth.checkSession();
    if (check !== 'active') {
      return toLogin(router, check === 'expired', state.url);
    }

    return auth.role() === role ? true : router.createUrlTree(['/access-denied']);
  };
