import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { Invoice, InvoiceHistory, InvoiceInput, PaymentInput } from './invoice.models';
import { InvoiceService } from './invoice.service';

const base = `${environment.apiBaseUrl}/api/invoices`;

const invoice: Invoice = {
  id: '11111111-1111-1111-1111-111111111111',
  number: 'INV-000001',
  status: 'Draft',
  customer: { id: '22222222-2222-2222-2222-222222222222', name: 'ABC', isActive: true },
  dueDate: '2030-07-01',
  notes: 'Net 30',
  createdAtUtc: '2030-06-01T10:00:00Z',
  updatedAtUtc: '2030-06-01T10:00:00Z',
  isOverdue: false,
  sentAtUtc: null,
  cancelledAtUtc: null,
  allowedTransitions: ['Sent', 'Cancelled'],
  isEditable: true,
  paid: 0,
  remaining: 230,
  acceptsPayment: false,
  payments: [],
  discount: 0,
  totals: { totalNet: 200, discount: 0, netAfterDiscount: 200, totalVat: 30, grandTotal: 230 },
  lines: [
    {
      id: '33333333-3333-3333-3333-333333333333',
      position: 1,
      productId: '44444444-4444-4444-4444-444444444444',
      productName: 'Consulting hour',
      sku: 'CNS-001',
      unit: 'Hour',
      unitPrice: 100,
      vatPercent: 15,
      quantity: 2,
      net: 200,
      discountShare: 0,
      vat: 30,
    },
  ],
};

describe('InvoiceService', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(InvoiceService), http: TestBed.inject(HttpTestingController) };
  }

  it('creates an invoice with POST and the whole input, quantities untouched, as the body', () => {
    const { service, http } = create();
    const input: InvoiceInput = {
      customerId: invoice.customer.id,
      dueDate: '2030-07-01',
      notes: 'Net 30',
      discount: null,
      lines: [{ productId: '44444444-4444-4444-4444-444444444444', quantity: 1.5 }],
    };
    let received: Invoice | undefined;

    service.create(input).subscribe((result) => (received = result));
    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    expect(request.request.body.lines[0].quantity).toBe(1.5);
    request.flush(invoice, { status: 201, statusText: 'Created' });

    expect(received).toEqual(invoice);
    http.verify();
  });

  it('gets one invoice by id', () => {
    const { service, http } = create();
    let received: Invoice | undefined;

    service.get(invoice.id).subscribe((result) => (received = result));
    const request = http.expectOne(`${base}/${invoice.id}`);
    expect(request.request.method).toBe('GET');
    request.flush(invoice);

    expect(received).toEqual(invoice);
    http.verify();
  });

  it('lists with page and page size', () => {
    const { service, http } = create();

    service.list({ page: 2, pageSize: 50 }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('leaves missing values out of the list request entirely', () => {
    const { service, http } = create();

    service.list({}).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  // ---- the dashboard's four filter values (spec 011) ----

  it('adds status, overdue and paidMonth to the query string only when given', () => {
    const { service, http } = create();

    service.list({ status: 'Sent', overdue: true, paidMonth: true }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.get('status')).toBe('Sent');
    expect(request.request.params.get('overdue')).toBe('true');
    expect(request.request.params.get('paidMonth')).toBe('true');
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('leaves status, overdue and paidMonth out of the query string when not given, exactly like page and pageSize', () => {
    const { service, http } = create();

    service.list({ status: 'Draft' }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.keys()).toEqual(['status']);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('encodes an id that is not a plain guid so it cannot change the address', () => {
    const { service, http } = create();

    service.get('a/b?c').subscribe({ error: () => undefined });

    http.expectOne(`${base}/a%2Fb%3Fc`).flush({}, { status: 404, statusText: 'Not Found' });
    http.verify();
  });

  it('sends a Draft with POST to its send address and no body, and returns the invoice', () => {
    const { service, http } = create();
    let result: Invoice | undefined;

    service.send(invoice.id).subscribe((sent) => (result = sent));

    const request = http.expectOne(`${base}/${invoice.id}/send`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush({ ...invoice, status: 'Sent' });
    expect(result?.status).toBe('Sent');
    http.verify();
  });

  it('cancels with POST to its cancel address and no body, and returns the invoice', () => {
    const { service, http } = create();
    let result: Invoice | undefined;

    service.cancel(invoice.id).subscribe((cancelled) => (result = cancelled));

    const request = http.expectOne(`${base}/${invoice.id}/cancel`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    request.flush({ ...invoice, status: 'Cancelled' });
    expect(result?.status).toBe('Cancelled');
    http.verify();
  });

  it('records a payment with POST to the payments address with the body exactly as given, and returns the invoice', () => {
    const { service, http } = create();
    const input: PaymentInput = { date: '2030-06-02', amount: 100.1, method: 'Cash', note: 'First half' };
    let result: Invoice | undefined;

    service.recordPayment(invoice.id, input).subscribe((recorded) => (result = recorded));

    const request = http.expectOne(`${base}/${invoice.id}/payments`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    expect(request.request.body.amount).toBe(100.1);
    request.flush({ ...invoice, status: 'Sent', paid: 100.1 }, { status: 201, statusText: 'Created' });
    expect(result?.paid).toBe(100.1);
    http.verify();
  });

  it('encodes the id in the payments address and passes an error on', () => {
    const { service, http } = create();
    let failure: unknown;

    service.recordPayment('a/b?c', { date: '2030-06-02', amount: 1, method: 'Bank', note: '' }).subscribe({ error: (error) => (failure = error) });

    http.expectOne(`${base}/a%2Fb%3Fc/payments`).flush({ title: 'Payment not allowed' }, { status: 409, statusText: 'Conflict' });
    expect(failure).toBeTruthy();
    http.verify();
  });

  it('encodes the id in the cancel address too', () => {
    const { service, http } = create();

    service.cancel('a/b?c').subscribe({ error: () => undefined });

    http.expectOne(`${base}/a%2Fb%3Fc/cancel`).flush({}, { status: 404, statusText: 'Not Found' });
    http.verify();
  });

  it('encodes the id in the send address too', () => {
    const { service, http } = create();

    service.send('a/b?c').subscribe({ error: () => undefined });

    http.expectOne(`${base}/a%2Fb%3Fc/send`).flush({}, { status: 404, statusText: 'Not Found' });
    http.verify();
  });

  // ---- history (spec 010): a read, nothing else ----

  it('reads an invoice history with a GET to its history address, no body and no query string, and returns the answer as received', () => {
    const { service, http } = create();
    const history: InvoiceHistory = {
      number: 'INV-000001',
      startsWithCreation: true,
      entries: [
        {
          id: 'e1',
          kind: 'Sent',
          occurredAtUtc: '2030-06-02T09:00:00Z',
          actorEmail: 'agent1@crm.local',
          actorRole: 'Agent',
          changes: [{ field: 'Status', before: 'Draft', after: 'Sent' }],
        },
      ],
    };
    let received: InvoiceHistory | undefined;

    service.history(invoice.id).subscribe((result) => (received = result));

    const request = http.expectOne(`${base}/${invoice.id}/history`);
    expect(request.request.method).toBe('GET');
    expect(request.request.body).toBeNull();
    expect(request.request.params.keys()).toEqual([]);
    request.flush(history);
    expect(received).toEqual(history);
    http.verify();
  });

  it('encodes the id in the history address and passes an error on', () => {
    const { service, http } = create();
    let failure: unknown;

    service.history('a/b?c').subscribe({ error: (error) => (failure = error) });

    http.expectOne(`${base}/a%2Fb%3Fc/history`).flush({ title: 'Invoice not found' }, { status: 404, statusText: 'Not Found' });
    expect(failure).toBeTruthy();
    http.verify();
  });

  it('offers no way to write history: the only method about history is the read', () => {
    const { service } = create();

    const names = Object.getOwnPropertyNames(Object.getPrototypeOf(service)).filter((name) => /history/i.test(name));

    expect(names).toEqual(['history']);
  });
});
