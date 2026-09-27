import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { DashboardSummary } from './dashboard.models';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  const url = `${environment.apiBaseUrl}/api/invoices/summary`;

  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(DashboardService), http: TestBed.inject(HttpTestingController) };
  }

  it('requests GET {apiBaseUrl}/api/invoices/summary with no body and no query string, and returns the answer as received', () => {
    const { service, http } = create();
    const summary: DashboardSummary = { draftCount: 3, unpaidCount: 5, overdueCount: 2, paidThisMonthCount: 1, unpaidAmount: 1234.5 };
    let received: DashboardSummary | undefined;

    service.summary().subscribe((result) => (received = result));

    const request = http.expectOne(url);
    expect(request.request.method).toBe('GET');
    expect(request.request.body).toBeNull();
    expect(request.request.params.keys()).toEqual([]);
    request.flush(summary);

    expect(received).toEqual(summary);
    http.verify();
  });

  it('passes a failure on unchanged', () => {
    const { service, http } = create();
    let failure: unknown;

    service.summary().subscribe({ error: (error) => (failure = error) });

    http.expectOne(url).flush({}, { status: 500, statusText: 'Server Error' });
    expect(failure).toBeTruthy();
    http.verify();
  });
});
