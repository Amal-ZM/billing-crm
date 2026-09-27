import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { environment } from '../../../environments/environment';
import { Session } from './auth.models';
import { AuthService, SESSION_STORAGE_KEY } from './auth.service';

const adminSession: Session = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'admin@crm.local',
  role: 'Admin',
};

describe('AuthService', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(AuthService), http: TestBed.inject(HttpTestingController) };
  }

  it('posts the credentials and, on success, stores the session and exposes email and role', () => {
    const { service, http } = create();
    let received: Session | undefined;

    service.signIn('admin@crm.local', 'pw').subscribe((session) => (received = session));
    const request = http.expectOne(`${environment.apiBaseUrl}/api/auth/login`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'admin@crm.local', password: 'pw' });
    request.flush(adminSession);

    expect(received).toEqual(adminSession);
    expect(service.isSignedIn()).toBe(true);
    expect(service.email()).toBe('admin@crm.local');
    expect(service.role()).toBe('Admin');
    expect(JSON.parse(sessionStorage.getItem(SESSION_STORAGE_KEY)!)).toEqual(adminSession);
    http.verify();
  });

  it('leaves no session when the API answers 401', () => {
    const { service, http } = create();
    let failed = false;

    service.signIn('admin@crm.local', 'wrong').subscribe({ error: () => (failed = true) });
    http.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush(
      { title: 'Invalid credentials' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(failed).toBe(true);
    expect(service.isSignedIn()).toBe(false);
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    http.verify();
  });

  it('restores a stored session when created', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(adminSession));

    const { service } = create();

    expect(service.isSignedIn()).toBe(true);
    expect(service.role()).toBe('Admin');
  });

  it('treats a corrupted stored session as signed out without throwing', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, '{not json');

    const { service } = create();

    expect(service.isSignedIn()).toBe(false);
  });

  it('treats a stored session with an unknown role as signed out', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...adminSession, role: 'Superuser' }));

    const { service } = create();

    expect(service.isSignedIn()).toBe(false);
  });

  it('does not crash when reading storage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });

    const { service } = create();

    expect(service.isSignedIn()).toBe(false);
  });

  it('rejects a sign-in response with an unexpected shape or role and creates no session', () => {
    const { service, http } = create();
    let failed = false;

    service.signIn('admin@crm.local', 'pw').subscribe({ error: () => (failed = true) });
    http.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush({ ...adminSession, role: 'Superuser' });

    expect(failed).toBe(true);
    expect(service.isSignedIn()).toBe(false);
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    http.verify();
  });

  it('signOut clears the session and the stored copy', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(adminSession));
    const { service } = create();
    expect(service.isSignedIn()).toBe(true);

    service.signOut();

    expect(service.isSignedIn()).toBe(false);
    expect(service.email()).toBeNull();
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });

  it('checkSession reports active, none and expired, and clears an expired session', () => {
    const { service } = create();
    expect(service.checkSession()).toBe('none');

    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(adminSession));
    const restored = TestBed.runInInjectionContext(() => new AuthService());
    expect(restored.checkSession()).toBe('active');

    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...adminSession, expiresAtUtc: '2000-01-01T00:00:00Z' }));
    const expired = TestBed.runInInjectionContext(() => new AuthService());
    expect(expired.isSignedIn()).toBe(false);
    expect(expired.checkSession()).toBe('expired');
    expect(expired.checkSession()).toBe('none');
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });

  it('stays signed in for this page when writing storage throws', () => {
    const { service, http } = create();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });

    service.signIn('admin@crm.local', 'pw').subscribe();
    http.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush(adminSession);

    expect(service.isSignedIn()).toBe(true);
  });
});
