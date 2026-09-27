import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Session } from '../auth/auth.models';
import { AuthService, SESSION_STORAGE_KEY } from '../auth/auth.service';
import { ShellComponent } from './shell.component';

const agentSession: Session = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'agent1@crm.local',
  role: 'Agent',
};

describe('ShellComponent', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('renders a toolbar with the product name', async () => {
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const toolbar = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar');
    expect(toolbar).not.toBeNull();
    expect(toolbar?.textContent).toContain('BillingCRM');
  });

  it('renders a main content area with a router outlet', async () => {
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const main = (fixture.nativeElement as HTMLElement).querySelector('main');
    expect(main).not.toBeNull();
    expect(main?.querySelector('router-outlet')).not.toBeNull();
  });

  it('shows the signed-in email and role', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(agentSession));
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const info = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="user-info"]');
    expect(info?.textContent).toContain('agent1@crm.local');
    expect(info?.textContent).toContain('Agent');
  });

  it('signs the person out and goes to /login when Sign out is pressed', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(agentSession));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const button = (fixture.nativeElement as HTMLElement).querySelector('button[data-testid="sign-out"]') as HTMLButtonElement;
    expect(button.textContent).toContain('Sign out');
    button.click();

    expect(TestBed.inject(AuthService).isSignedIn()).toBe(false);
    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/login', { replaceUrl: true });
  });

  it.each(['Admin', 'Agent'] as const)('offers the Customers link to a signed-in %s', async (role) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...agentSession, role }));
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/customers"]');
    expect(link?.textContent).toContain('Customers');
  });

  it.each(['Admin', 'Agent'] as const)('offers the Products link to a signed-in %s', async (role) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...agentSession, role }));
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/products"]');
    expect(link?.textContent).toContain('Products');
  });

  it.each(['Admin', 'Agent'] as const)('offers the Invoices link to a signed-in %s', async (role) => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...agentSession, role }));
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/invoices"]');
    expect(link?.textContent).toContain('Invoices');
  });

  const adminLink = (element: HTMLElement) => element.querySelector('a[href="/admin"]');

  it('offers the Administration link to an Admin', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({ ...agentSession, email: 'admin@crm.local', role: 'Admin' }));
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    const link = adminLink(fixture.nativeElement as HTMLElement);
    expect(link).not.toBeNull();
    expect(link?.textContent).toContain('Administration');
  });

  it('offers no Administration link to an Agent', async () => {
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(agentSession));
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();

    expect(adminLink(fixture.nativeElement as HTMLElement)).toBeNull();
  });
});
