import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from './app';
import { routes } from './app.routes';
import { SESSION_STORAGE_KEY } from './core/auth/auth.service';

const agent = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'agent1@crm.local',
  role: 'Agent',
};

describe('customer routes', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => sessionStorage.clear());

  it.each(['/customers/new', '/customers/22222222-2222-2222-2222-222222222222/edit'])(
    'sends a signed-out visitor who opens %s to the sign-in page',
    async (url) => {
      const fixture = TestBed.createComponent(App);
      const router = TestBed.inject(Router);

      await router.navigateByUrl(url);
      fixture.detectChanges();

      expect(router.url).toBe(`/login?returnUrl=${encodeURIComponent(url)}`);
    },
  );

  it('opens the edit form for a signed-in staff member', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(agent));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/customers/22222222-2222-2222-2222-222222222222/edit');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/customers/22222222-2222-2222-2222-222222222222/edit');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-customer-form')).not.toBeNull();
  });

  it('shows no Customers link to a signed-out visitor (the sign-in page has no shell)', async () => {
    const fixture = TestBed.createComponent(App);

    await TestBed.inject(Router).navigateByUrl('/login');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/customers"]')).toBeNull();
  });
});
