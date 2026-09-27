import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { environment } from '../../../environments/environment';
import { Session } from './auth.models';
import { authInterceptor } from './auth.interceptor';
import { AuthService, SESSION_STORAGE_KEY } from './auth.service';

const session: Session = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'agent1@crm.local',
  role: 'Agent',
};

describe('authInterceptor', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('adds a Bearer header to requests to the API when signed in', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(HttpClient).get(`${environment.apiBaseUrl}/api/auth/me`).subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/api/auth/me`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-value');
    request.flush({});
  });

  it('never adds the token to requests to other hosts', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(HttpClient).get('https://other.example/data').subscribe();

    const request = http.expectOne('https://other.example/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('on a 401 from the API clears the session and sends the person to /login as expired', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    let failed = false;

    TestBed.inject(HttpClient)
      .get(`${environment.apiBaseUrl}/api/auth/me`)
      .subscribe({ error: () => (failed = true) });
    http.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toBe(true);
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { reason: 'expired', returnUrl: '/' } });
  });

  it('ignores a 401 to a request that carried no token, so a signed-out 401 is not reported as an expired session', () => {
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    TestBed.inject(HttpClient)
      .get(`${environment.apiBaseUrl}/api/auth/me`)
      .subscribe({ error: () => undefined });
    http.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores a late 401 for a token that is no longer the current session', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const auth = TestBed.inject(AuthService);

    TestBed.inject(HttpClient)
      .get(`${environment.apiBaseUrl}/api/auth/me`)
      .subscribe({ error: () => undefined });
    const pending = http.expectOne(`${environment.apiBaseUrl}/api/auth/me`);
    auth.signOut();
    pending.flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not navigate when the person is already on the sign-in page', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/login?returnUrl=%2F');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    TestBed.inject(HttpClient)
      .get(`${environment.apiBaseUrl}/api/auth/me`)
      .subscribe({ error: () => undefined });
    http.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('never adds the token to a host that merely starts with the API address', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);
    const lookalike = `${environment.apiBaseUrl}.evil.example/steal`;

    TestBed.inject(HttpClient).get(lookalike).subscribe();

    const request = http.expectOne(lookalike);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('does not treat a failed sign-in (401 from the login request) as an expired session', () => {
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    TestBed.inject(HttpClient)
      .post(`${environment.apiBaseUrl}/api/auth/login`, {})
      .subscribe({ error: () => undefined });
    http.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores a 401 from another host', () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    TestBed.inject(HttpClient)
      .get('https://other.example/data')
      .subscribe({ error: () => undefined });
    http.expectOne('https://other.example/data').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).not.toBeNull();
  });

  it('adds no header when signed out', () => {
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(HttpClient).get(`${environment.apiBaseUrl}/health`).subscribe();

    const request = http.expectOne(`${environment.apiBaseUrl}/health`);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
});
