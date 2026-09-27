import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { CustomerService } from './customer.service';

const base = `${environment.apiBaseUrl}/api/customers`;

describe('CustomerService id encoding', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(CustomerService), http: TestBed.inject(HttpTestingController) };
  }

  // A route value such as "../../admin" (from a crafted link) must stay a single path segment.
  it.each(['../../admin', 'a/b', 'a?x=1', 'a#b', 'a b'])('keeps %s inside one path segment for get, update and setActive', (id) => {
    const { service, http } = create();
    const encoded = encodeURIComponent(id);

    service.get(id).subscribe({ error: () => undefined });
    http.expectOne(`${base}/${encoded}`).flush({}, { status: 404, statusText: 'Not Found' });

    service.update(id, { name: 'x', type: 'Company', email: 'x@x.co', phone: '0500000000', vatNumber: '', address: 'x' }).subscribe({ error: () => undefined });
    http.expectOne(`${base}/${encoded}`).flush({}, { status: 404, statusText: 'Not Found' });

    service.setActive(id, true).subscribe({ error: () => undefined });
    http.expectOne(`${base}/${encoded}/active`).flush({}, { status: 404, statusText: 'Not Found' });

    http.verify();
  });
});
