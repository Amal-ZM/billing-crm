import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { Customer, CustomerInput } from './customer.models';
import { CustomerService } from './customer.service';

const base = `${environment.apiBaseUrl}/api/customers`;

const customer: Customer = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Acme Trading',
  type: 'Company',
  email: 'accounts@acme.example',
  phone: '0500000000',
  vatNumber: null,
  address: 'Somewhere',
  isActive: true,
  createdAtUtc: '2026-09-24T10:00:00Z',
  updatedAtUtc: '2026-09-24T10:00:00Z',
};

describe('CustomerService', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(CustomerService), http: TestBed.inject(HttpTestingController) };
  }

  it('creates a customer with POST and the input as the body', () => {
    const { service, http } = create();
    const input: CustomerInput = {
      name: 'Acme Trading',
      type: 'Company',
      email: 'accounts@acme.example',
      phone: '0500000000',
      vatNumber: '',
      address: 'Somewhere',
    };
    let received: Customer | undefined;

    service.create(input).subscribe((result) => (received = result));
    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    request.flush(customer, { status: 201, statusText: 'Created' });

    expect(received).toEqual(customer);
    http.verify();
  });

  it('gets one customer by id', () => {
    const { service, http } = create();
    let received: Customer | undefined;

    service.get(customer.id).subscribe((result) => (received = result));
    const request = http.expectOne(`${base}/${customer.id}`);
    expect(request.request.method).toBe('GET');
    request.flush(customer);

    expect(received).toEqual(customer);
    http.verify();
  });

  it('sends every filter that is set', () => {
    const { service, http } = create();

    service.list({ q: '  acme  ', type: 'Company', active: false, page: 3, pageSize: 50 }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.get('q')).toBe('acme');
    expect(request.request.params.get('type')).toBe('Company');
    expect(request.request.params.get('active')).toBe('false');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], page: 3, pageSize: 50, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('leaves blank and missing values out of the request entirely', () => {
    const { service, http } = create();

    service.list({ q: '   ', type: undefined, active: undefined }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('sends active=true when the filter is Active', () => {
    const { service, http } = create();

    service.list({ active: true }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.get('active')).toBe('true');
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('lists with page and page size', () => {
    const { service, http } = create();

    service.list({ page: 2, pageSize: 20 }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
  });
});
