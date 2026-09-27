import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from './app';
import { routes } from './app.routes';
import { Session } from './core/auth/auth.models';
import { SESSION_STORAGE_KEY } from './core/auth/auth.service';

const session: Session = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'admin@crm.local',
  role: 'Admin',
};

describe('App', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => sessionStorage.clear());

  it('renders the shell with the home page when signed in', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('mat-toolbar')?.textContent).toContain('BillingCRM');
    expect(element.querySelector('app-home')).not.toBeNull();
  });

  it.each([
    ['/customers', 'app-customer-list'],
    ['/customers/new', 'app-customer-form'],
    ['/customers/11111111-1111-1111-1111-111111111111', 'app-customer-detail'],
  ])('lets any signed-in staff member open %s', async (url, selector) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, email: 'agent1@crm.local', role: 'Agent' }));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe(url);
    expect((fixture.nativeElement as HTMLElement).querySelector(selector)).not.toBeNull();
  });

  it.each([
    ['/products', 'app-product-list'],
    ['/products/11111111-1111-1111-1111-111111111111', 'app-product-detail'],
  ])('lets an Agent open %s (products are readable by anyone signed in)', async (url, selector) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, email: 'agent1@crm.local', role: 'Agent' }));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe(url);
    expect((fixture.nativeElement as HTMLElement).querySelector(selector)).not.toBeNull();
  });

  it('sends an Agent who opens the add-product page to the access-denied page', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, email: 'agent1@crm.local', role: 'Agent' }));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/products/new');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/access-denied');
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('app-access-denied')).not.toBeNull();
    expect(element.querySelector('app-product-form')).toBeNull();
  });

  it('lets an Admin open the add-product page', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/products/new');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/products/new');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-product-form')).not.toBeNull();
  });

  it('sends a signed-out visitor who opens /products to the sign-in page', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/products');
    fixture.detectChanges();

    expect(router.url).toBe('/login?returnUrl=%2Fproducts');
  });

  it.each([
    ['Admin', 'admin@crm.local'],
    ['Agent', 'agent1@crm.local'],
  ] as const)('lets a signed-in %s open the create-invoice page and an invoice\'s details', async (role, email) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, email, role }));
    for (const [url, selector] of [
      ['/invoices/new', 'app-invoice-form'],
      ['/invoices/11111111-1111-1111-1111-111111111111', 'app-invoice-detail'],
      ['/invoices/11111111-1111-1111-1111-111111111111/edit', 'app-invoice-form'],
    ]) {
      const fixture = TestBed.createComponent(App);
      const router = TestBed.inject(Router);

      await router.navigateByUrl(url);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(router.url).toBe(url);
      expect((fixture.nativeElement as HTMLElement).querySelector(selector)).not.toBeNull();
    }
  });

  it.each([
    ['Admin', 'admin@crm.local'],
    ['Agent', 'agent1@crm.local'],
  ] as const)('lets a signed-in %s open the invoice list', async (role, email) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, email, role }));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/invoices');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/invoices');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-invoice-list')).not.toBeNull();
  });

  it('sends a signed-out visitor who opens /invoices to the sign-in page', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/invoices');
    fixture.detectChanges();

    expect(router.url).toBe('/login?returnUrl=%2Finvoices');
  });

  it('sends a signed-out visitor who opens /invoices/new to the sign-in page', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/invoices/new');
    fixture.detectChanges();

    expect(router.url).toBe('/login?returnUrl=%2Finvoices%2Fnew');
  });

  it('sends a signed-out visitor who opens /customers to the sign-in page', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/customers');
    fixture.detectChanges();

    expect(router.url).toBe('/login?returnUrl=%2Fcustomers');
  });

  it('sends an Agent who types /admin to the access-denied page', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, email: 'agent1@crm.local', role: 'Agent' }));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/admin');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/access-denied');
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('app-access-denied')).not.toBeNull();
    expect(element.querySelector('app-admin')).toBeNull();
  });

  it('lets an Admin open /admin', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/admin');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/admin');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-admin')).not.toBeNull();
  });

  it('re-checks expiry when moving between pages inside the shell, not only when the shell first opens', async () => {
    const expiresSoon = new Date(Date.now() + 150).toISOString();
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...session, expiresAtUtc: expiresSoon }));
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/');
    fixture.detectChanges();
    expect(router.url).toBe('/');

    await new Promise((resolve) => setTimeout(resolve, 250));
    await router.navigateByUrl('/access-denied');
    fixture.detectChanges();

    expect(router.url).toBe('/login?reason=expired&returnUrl=%2Faccess-denied');
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
  });

  it('sends a signed-out visitor to the sign-in page instead of the shell', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/login?returnUrl=%2F');
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('app-login')).not.toBeNull();
    expect(element.querySelector('app-home')).toBeNull();
  });
});
