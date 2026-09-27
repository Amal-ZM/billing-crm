import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { Product, ProductInput } from './product.models';
import { ProductService } from './product.service';

const base = `${environment.apiBaseUrl}/api/products`;

const product: Product = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Consulting hour',
  sku: 'CNS-001',
  unit: 'Hour',
  unitPrice: 100,
  vatPercent: 15,
  isActive: true,
  createdAtUtc: '2026-09-24T10:00:00Z',
  updatedAtUtc: '2026-09-24T10:00:00Z',
};

describe('ProductService', () => {
  function create() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(ProductService), http: TestBed.inject(HttpTestingController) };
  }

  it('creates a product with POST and the input, numbers untouched, as the body', () => {
    const { service, http } = create();
    const input: ProductInput = { name: 'Consulting hour', sku: 'CNS-001', unit: 'Hour', unitPrice: 100.1, vatPercent: 15 };
    let received: Product | undefined;

    service.create(input).subscribe((result) => (received = result));
    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    expect(request.request.body.unitPrice).toBe(100.1);
    request.flush(product, { status: 201, statusText: 'Created' });

    expect(received).toEqual(product);
    http.verify();
  });

  it('gets one product by id', () => {
    const { service, http } = create();
    let received: Product | undefined;

    service.get(product.id).subscribe((result) => (received = result));
    const request = http.expectOne(`${base}/${product.id}`);
    expect(request.request.method).toBe('GET');
    request.flush(product);

    expect(received).toEqual(product);
    http.verify();
  });

  it('sends every filter that is set', () => {
    const { service, http } = create();

    service.list({ q: '  cns  ', unit: 'Hour', status: 'all', page: 3, pageSize: 50 }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.get('q')).toBe('cns');
    expect(request.request.params.get('unit')).toBe('Hour');
    expect(request.request.params.get('status')).toBe('all');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], page: 3, pageSize: 50, totalCount: 0, totalPages: 0 });
    http.verify();
  });

  it('leaves blank and missing values out of the request entirely', () => {
    const { service, http } = create();

    service.list({ q: '   ', unit: undefined, status: undefined }).subscribe();

    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.keys()).toEqual([]);
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

  it('updates a product with PUT to its address and the input, numbers untouched, as the body', () => {
    const { service, http } = create();
    const input: ProductInput = { name: 'Consulting day', sku: 'CNS-001', unit: 'Service', unitPrice: 750.5, vatPercent: 0 };
    let received: Product | undefined;

    service.update(product.id, input).subscribe((result) => (received = result));
    const request = http.expectOne(`${base}/${product.id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    expect(request.request.body.vatPercent).toBe(0);
    request.flush({ ...product, ...input });

    expect(received?.unitPrice).toBe(750.5);
    http.verify();
  });

  it('activates and deactivates with PUT to the active address', () => {
    const { service, http } = create();

    service.setActive(product.id, false).subscribe();
    const off = http.expectOne(`${base}/${product.id}/active`);
    expect(off.request.method).toBe('PUT');
    expect(off.request.body).toEqual({ isActive: false });
    off.flush({ ...product, isActive: false });

    service.setActive(product.id, true).subscribe();
    const on = http.expectOne(`${base}/${product.id}/active`);
    expect(on.request.body).toEqual({ isActive: true });
    on.flush(product);
    http.verify();
  });

  it('encodes the id on update and on set active too', () => {
    const { service, http } = create();

    service.update('a/b', { name: 'X', sku: 'X-1', unit: 'Item', unitPrice: 1 }).subscribe({ error: () => undefined });
    http.expectOne(`${base}/a%2Fb`).flush({}, { status: 404, statusText: 'Not Found' });
    service.setActive('a/b', true).subscribe({ error: () => undefined });
    http.expectOne(`${base}/a%2Fb/active`).flush({}, { status: 404, statusText: 'Not Found' });
    http.verify();
  });

  it('encodes an id that is not a plain guid so it cannot change the address', () => {
    const { service, http } = create();

    service.get('a/b?c').subscribe({ error: () => undefined });

    const request = http.expectOne(`${base}/a%2Fb%3Fc`);
    request.flush({}, { status: 404, statusText: 'Not Found' });
    http.verify();
  });
});
