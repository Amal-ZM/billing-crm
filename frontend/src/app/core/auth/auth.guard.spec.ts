import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Session } from './auth.models';
import { authGuard, roleGuard } from './auth.guard';
import { SESSION_STORAGE_KEY } from './auth.service';

const adminSession: Session = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'admin@crm.local',
  role: 'Admin',
};

function run(guard: CanActivateFn, url: string) {
  return TestBed.runInInjectionContext(() =>
    guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
  );
}

describe('authGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => sessionStorage.clear());

  it('sends a signed-out visitor to /login and remembers where they were going', () => {
    const result = run(authGuard, '/admin');

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fadmin');
  });

  it('lets a signed-in user through', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(adminSession));

    expect(run(authGuard, '/')).toBe(true);
  });

  it('sends an expired session to /login with the expired reason and clears it', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...adminSession, expiresAtUtc: '2000-01-01T00:00:00Z' }));

    const result = run(authGuard, '/admin');

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login?reason=expired&returnUrl=%2Fadmin');
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });
});

describe('roleGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => sessionStorage.clear());

  const urlOf = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('lets an Admin into an Admin route', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(adminSession));

    expect(run(roleGuard('Admin'), '/admin')).toBe(true);
  });

  it('sends an Agent to the access-denied page', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...adminSession, email: 'agent1@crm.local', role: 'Agent' }));

    const result = run(roleGuard('Admin'), '/admin');

    expect(result).toBeInstanceOf(UrlTree);
    expect(urlOf(result)).toBe('/access-denied');
  });

  it('sends a signed-out visitor to /login rather than access-denied', () => {
    const result = run(roleGuard('Admin'), '/admin');

    expect(urlOf(result)).toBe('/login?returnUrl=%2Fadmin');
  });

  it('sends an expired session to /login with the expired reason', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...adminSession, expiresAtUtc: '2000-01-01T00:00:00Z' }));

    expect(urlOf(run(roleGuard('Admin'), '/admin'))).toBe('/login?reason=expired&returnUrl=%2Fadmin');
  });
});
