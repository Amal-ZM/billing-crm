import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { Invoice, InvoiceInput } from './invoice.models';
import { InvoiceService } from './invoice.service';

const base = `${environment.apiBaseUrl}/api/invoices`;
const id = '11111111-1111-1111-1111-111111111111';

describe('InvoiceService.update', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(InvoiceService), http: TestBed.inject(HttpTestingController) };
  }

  it('saves the whole Draft with PUT to the invoice\'s address, quantities untouched', () => {
    const { service, http } = create();
    const input: InvoiceInput = {
      customerId: '22222222-2222-2222-2222-222222222222',
      dueDate: '2030-07-01',
      notes: 'Net 30',
      discount: null,
      lines: [
        { id: '33333333-3333-3333-3333-333333333333', quantity: 3.25 },
        { productId: '44444444-4444-4444-4444-444444444444', quantity: 1 },
      ],
    };
    let received: Invoice | undefined;

    service.update(id, input).subscribe((result) => (received = result));
    const request = http.expectOne(`${base}/${id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    expect(request.request.body.lines[0].quantity).toBe(3.25);
    request.flush({ id, number: 'INV-000001' });

    expect(received?.number).toBe('INV-000001');
    http.verify();
  });

  it('encodes an id that is not a plain guid', () => {
    const { service, http } = create();

    service.update('a/b?c', { customerId: 'x', dueDate: '2030-01-01', notes: '', discount: null, lines: [] }).subscribe({ error: () => undefined });

    http.expectOne(`${base}/a%2Fb%3Fc`).flush({}, { status: 404, statusText: 'Not Found' });
    http.verify();
  });
});
