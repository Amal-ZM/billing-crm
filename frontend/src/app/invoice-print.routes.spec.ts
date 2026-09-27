import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Route, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { routes } from './app.routes';
import { authGuard } from './core/auth/auth.guard';
import { Role, Session } from './core/auth/auth.models';
import { SESSION_STORAGE_KEY } from './core/auth/auth.service';
import { ShellComponent } from './core/layout/shell.component';

const PRINT_PATH = 'invoices/:id/print';

function session(role: Role): Session {
  return { accessToken: 'token-value', expiresAtUtc: '2099-01-01T00:00:00Z', email: `${role.toLowerCase()}@crm.local`, role };
}

const printRoute: Route | undefined = routes.find((route) => route.path === PRINT_PATH);
const shellRoute: Route | undefined = routes.find((route) => route.component === ShellComponent);

function runGuards(route: Route | undefined, url: string): (boolean | UrlTree)[] {
  return TestBed.runInInjectionContext(() =>
    (route?.canActivate ?? []).map((guard) => (guard as CanActivateFn)({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot) as boolean | UrlTree),
  );
}

/** The printable invoice page (spec 009): outside the signed-in shell (so none of the application's chrome is in it) but behind the same guard. */
describe('invoice print route', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => sessionStorage.clear());

  it('exists as a lazy route', () => {
    expect(printRoute).toBeDefined();
    expect(typeof printRoute?.loadComponent).toBe('function');
    expect(printRoute?.component).toBeUndefined();
  });

  it('is a top-level route, not a child of the signed-in shell, so the toolbar and menus are not part of the page', () => {
    expect(shellRoute).toBeDefined();
    expect(routes).toContain(printRoute);
    expect(printRoute).not.toBe(shellRoute);
    expect((shellRoute?.children ?? []).some((child) => child.path === PRINT_PATH)).toBe(false);
    // Nothing under the shell can match the print address either (the details route has one segment after invoices).
    const printSegments = PRINT_PATH.split('/');
    for (const child of shellRoute?.children ?? []) {
      const segments = (child.path ?? '').split('/');
      const matches = segments.length === printSegments.length && segments.every((segment, i) => segment.startsWith(':') || segment === printSegments[i] || printSegments[i].startsWith(':'));
      expect(matches, `${child.path} must not also match ${PRINT_PATH}`).toBe(false);
    }
  });

  it('is behind the same guard as the other protected screens', () => {
    expect(printRoute?.canActivate).toEqual([authGuard]);
    expect(shellRoute?.canActivate).toEqual([authGuard]);
  });

  it('comes before the wildcard route, so it is matched', () => {
    const paths = routes.map((route) => route.path);

    expect(paths.indexOf(PRINT_PATH)).toBeGreaterThanOrEqual(0);
    expect(paths.indexOf(PRINT_PATH)).toBeLessThan(paths.indexOf('**'));
  });

  it('sends a signed-out visitor to sign-in, with the print address to come back to', () => {
    const url = '/invoices/11111111-1111-1111-1111-111111111111/print';

    const [signedOut] = runGuards(printRoute, url);

    expect(signedOut).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(signedOut as UrlTree)).toBe(`/login?returnUrl=${encodeURIComponent(url)}`);
  });

  it('lets a signed-in person through', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session('Agent')));

    expect(runGuards(printRoute, '/invoices/11111111-1111-1111-1111-111111111111/print')).toEqual([true]);
  });

  it('sends a person whose session has ended to sign-in saying so', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session('Admin'), expiresAtUtc: '2000-01-01T00:00:00Z' }));

    const [result] = runGuards(printRoute, '/invoices/x/print');

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toContain('reason=expired');
  });
});
