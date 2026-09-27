import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Route, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Role, Session } from './core/auth/auth.models';
import { SESSION_STORAGE_KEY } from './core/auth/auth.service';
import { routes } from './app.routes';

function session(role: Role): Session {
  return { accessToken: 'token-value', expiresAtUtc: '2099-01-01T00:00:00Z', email: `${role.toLowerCase()}@crm.local`, role };
}

/** The product routes only an Admin may open. */
const ADMIN_ONLY = ['products/new', 'products/:id/edit'];

/** The routes under the signed-in shell. */
const shellChildren: Route[] = routes.find((route) => route.path === '')?.children ?? [];
const routeFor = (path: string) => shellChildren.find((route) => route.path === path);

function runGuards(route: Route | undefined, url: string): (boolean | UrlTree)[] {
  return TestBed.runInInjectionContext(() =>
    (route?.canActivate ?? []).map((guard) => (guard as CanActivateFn)({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot) as boolean | UrlTree),
  );
}

describe('product routes', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => sessionStorage.clear());

  it('has the product routes under the signed-in shell, with "new" before ":id"', () => {
    const paths = shellChildren.map((route) => route.path).filter((path) => path?.startsWith('products'));

    expect(paths).toEqual(expect.arrayContaining(['products', 'products/new', 'products/:id', 'products/:id/edit']));
    expect(paths.indexOf('products/new')).toBeLessThan(paths.indexOf('products/:id'));
  });

  it.each(ADMIN_ONLY)('%s sends an Agent to the access-denied page', (path) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session('Agent')));

    const results = runGuards(routeFor(path), `/${path}`);

    expect(results.length).toBeGreaterThan(0);
    const denied = results.find((result) => result instanceof UrlTree) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(denied)).toBe('/access-denied');
  });

  it.each(ADMIN_ONLY)('%s lets an Admin in', (path) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session('Admin')));

    const results = runGuards(routeFor(path), `/${path}`);

    expect(results.length).toBeGreaterThan(0);
    expect(results.every((result) => result === true)).toBe(true);
  });

  it.each(['products', 'products/:id'])('%s has no role guard, so an Agent can read', (path) => {
    expect(routeFor(path)).toBeDefined();
    expect(routeFor(path)?.canActivate ?? []).toEqual([]);
  });
});
