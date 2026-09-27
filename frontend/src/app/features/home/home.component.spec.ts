import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { DashboardSummary } from '../../core/dashboard/dashboard.models';
import { DashboardService } from '../../core/dashboard/dashboard.service';
import { HealthResponse, HealthService } from '../../core/health/health.service';
import { HomeComponent } from './home.component';

function statusElement(element: HTMLElement): HTMLElement {
  return element.querySelector('[data-testid="api-status"]') as HTMLElement;
}

const summary: DashboardSummary = { draftCount: 3, unpaidCount: 5, overdueCount: 2, paidThisMonthCount: 1, unpaidAmount: 1234.5 };

function setup(getHealth: () => unknown, summaryCall: ReturnType<typeof vi.fn> = vi.fn().mockReturnValue(of(summary))) {
  TestBed.configureTestingModule({
    imports: [HomeComponent],
    providers: [
      provideRouter([]),
      { provide: HealthService, useValue: { getHealth } },
      { provide: DashboardService, useValue: { summary: summaryCall } },
    ],
  });
  const fixture = TestBed.createComponent(HomeComponent);
  return { fixture, element: fixture.nativeElement as HTMLElement };
}

describe('HomeComponent', () => {

  it('shows a loading state, then Healthy', () => {
    const response = new Subject<HealthResponse>();
    const { fixture, element } = setup(() => response);

    fixture.detectChanges();
    expect(statusElement(element).textContent).toContain('Checking');
    expect(statusElement(element).getAttribute('role')).toBe('status');

    response.next({ status: 'Healthy', checks: { database: 'Healthy' } });
    response.complete();
    fixture.detectChanges();
    expect(statusElement(element).textContent?.trim()).toBe('API status: Healthy');
    expect(statusElement(element).classList.contains('problem')).toBe(false);
  });

  it('shows the unhealthy status the API reports, styled as a problem', () => {
    const response = new Subject<HealthResponse>();
    const { fixture, element } = setup(() => response);

    fixture.detectChanges();
    response.next({ status: 'Unhealthy', checks: { database: 'Unhealthy' } });
    response.complete();
    fixture.detectChanges();

    expect(statusElement(element).textContent?.trim()).toBe('API status: Unhealthy');
    expect(statusElement(element).classList.contains('problem')).toBe(true);
  });

  it('shows "API unreachable" when the request fails without an HTTP response', () => {
    const { fixture, element } = setup(() => throwError(() => new Error('network down')));

    fixture.detectChanges();

    expect(statusElement(element).textContent).toContain('API unreachable');
  });

  it('shows the HTTP status when the API answers with an unexpected error', () => {
    const { fixture, element } = setup(() =>
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );

    fixture.detectChanges();

    expect(statusElement(element).textContent).toContain('HTTP 500');
    expect(statusElement(element).textContent).not.toContain('unreachable');
  });
});

// ---- the dashboard cards (spec 011) ----

describe('HomeComponent dashboard cards', () => {
  const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;
  const text = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim();

  it('shows the five figures the service returns', () => {
    const { fixture, element } = setup(() => new Subject<HealthResponse>());

    fixture.detectChanges();

    expect(text(element, 'dashboard-draft')).toContain('3');
    expect(text(element, 'dashboard-unpaid')).toContain('5');
    expect(text(element, 'dashboard-overdue')).toContain('2');
    expect(text(element, 'dashboard-paid-month')).toContain('1');
    expect(text(element, 'dashboard-unpaid-amount')).toContain('SAR');
    expect(text(element, 'dashboard-unpaid-amount')).toContain('1,234.50');
  });

  it('shows every card as 0, and SAR 0.00, for an empty database', () => {
    const empty: DashboardSummary = { draftCount: 0, unpaidCount: 0, overdueCount: 0, paidThisMonthCount: 0, unpaidAmount: 0 };
    const { fixture, element } = setup(() => new Subject<HealthResponse>(), vi.fn().mockReturnValue(of(empty)));

    fixture.detectChanges();

    expect(text(element, 'dashboard-draft')).toContain('0');
    expect(text(element, 'dashboard-unpaid')).toContain('0');
    expect(text(element, 'dashboard-overdue')).toContain('0');
    expect(text(element, 'dashboard-paid-month')).toContain('0');
    expect(text(element, 'dashboard-unpaid-amount')).toContain('SAR 0.00');
  });

  it('shows a quiet loading state that does not block or hide the existing health card', () => {
    const { fixture, element } = setup(() => new Subject<HealthResponse>(), vi.fn().mockReturnValue(new Subject<DashboardSummary>()));

    fixture.detectChanges();

    expect(has(element, 'dashboard-loading')).toBe(true);
    expect(has(element, 'dashboard-draft')).toBe(false);
    expect(statusElement(element)).not.toBeNull(); // the health card is unaffected
  });

  it('shows a quiet unavailable message on failure, with no error banner and no Retry button', () => {
    const { fixture, element } = setup(
      () => new Subject<HealthResponse>(),
      vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))),
    );

    fixture.detectChanges();

    expect(text(element, 'dashboard-unavailable')).toBe('Dashboard unavailable.');
    expect(has(element, 'dashboard-draft')).toBe(false);
    expect(element.querySelectorAll('[data-testid="retry"]')).toHaveLength(0);
    expect(element.querySelector('[role="alert"]')).toBeNull();
  });

  it('requests the summary exactly once', () => {
    const summaryCall = vi.fn().mockReturnValue(of(summary));

    const { fixture } = setup(() => new Subject<HealthResponse>(), summaryCall);
    fixture.detectChanges();

    expect(summaryCall).toHaveBeenCalledTimes(1);
  });
});

// ---- each card links to the matching filtered invoice list (spec 011) ----

describe('HomeComponent dashboard card links', () => {
  it('links each card to /invoices with the matching queryParams', () => {
    const { fixture, element } = setup(() => new Subject<HealthResponse>());
    fixture.detectChanges();

    const linkOf = (testId: string) => element.querySelector(`[data-testid="${testId}"]`)?.closest('a') ?? element.querySelector(`a[data-testid="${testId}"]`);

    expect(linkOf('dashboard-draft')?.getAttribute('href')).toBe('/invoices?status=Draft');
    expect(linkOf('dashboard-unpaid')?.getAttribute('href')).toBe('/invoices?status=Sent');
    expect(linkOf('dashboard-overdue')?.getAttribute('href')).toBe('/invoices?status=Sent&overdue=true');
    expect(linkOf('dashboard-paid-month')?.getAttribute('href')).toBe('/invoices?status=Paid&paidMonth=true');
    expect(linkOf('dashboard-unpaid-amount')?.getAttribute('href')).toBe('/invoices?status=Sent');
  });

  it('opens every card link in the same tab', () => {
    const { fixture, element } = setup(() => new Subject<HealthResponse>());
    fixture.detectChanges();

    const links = ['dashboard-draft', 'dashboard-unpaid', 'dashboard-overdue', 'dashboard-paid-month', 'dashboard-unpaid-amount'].map(
      (testId) => element.querySelector(`[data-testid="${testId}"]`)?.closest('a') ?? element.querySelector(`a[data-testid="${testId}"]`),
    );

    expect(links.every((link) => link !== null && !link.hasAttribute('target'))).toBe(true);
  });
});
