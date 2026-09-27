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

describe('CustomerService edit and active state', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(CustomerService), http: TestBed.inject(HttpTestingController) };
  }

  it('updates a customer with PUT to its address', () => {
    const { service, http } = create();
    const input: CustomerInput = {
      name: 'Acme Group',
      type: 'Company',
      email: 'accounts@acme.example',
      phone: '0500000000',
      vatNumber: '',
      address: 'Somewhere',
    };
    let received: Customer | undefined;

    service.update(customer.id, input).subscribe((result) => (received = result));
    const request = http.expectOne(`${base}/${customer.id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    request.flush({ ...customer, name: 'Acme Group' });

    expect(received?.name).toBe('Acme Group');
    http.verify();
  });

  it.each([true, false])('sets the active state to %s with PUT to /active', (isActive) => {
    const { service, http } = create();

    service.setActive(customer.id, isActive).subscribe();

    const request = http.expectOne(`${base}/${customer.id}/active`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ isActive });
    request.flush({ ...customer, isActive });
    http.verify();
  });
});
