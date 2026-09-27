import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { Customer } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { CustomerDetailComponent } from './customer-detail.component';

const acme: Customer = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Acme Trading',
  type: 'Company',
  email: 'Accounts@Acme.example',
  phone: '+966 11 555 0100',
  vatNumber: '300000000000003',
  address: '12 King Fahd Rd\nRiyadh',
  isActive: true,
  createdAtUtc: '2026-09-24T10:00:00Z',
  updatedAtUtc: '2026-09-25T11:30:00Z',
};

function setup(get: ReturnType<typeof vi.fn>) {
  TestBed.configureTestingModule({
    imports: [CustomerDetailComponent],
    providers: [
      provideRouter([]),
      { provide: CustomerService, useValue: { get } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: acme.id }) } } },
    ],
  });
  const fixture = TestBed.createComponent(CustomerDetailComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('CustomerDetailComponent', () => {
  it('shows a loading state first', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<Customer>()));

    expect(root.querySelector('[data-testid="detail-loading"]')).not.toBeNull();
  });

  it('shows every field, the active badge and the created and changed times', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)));
    fixture.detectChanges();

    const text = root.textContent ?? '';
    for (const expected of ['Acme Trading', 'Company', 'Accounts@Acme.example', '+966 11 555 0100', '300000000000003', '12 King Fahd Rd', 'Riyadh']) {
      expect(text).toContain(expected);
    }
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
    expect(root.querySelector('[data-testid="created-at"]')?.textContent?.trim()).not.toBe('');
    expect(root.querySelector('[data-testid="updated-at"]')?.textContent?.trim()).not.toBe('');
  });

  it('shows Inactive for an inactive customer', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...acme, isActive: false })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Inactive');
  });

  it('shows a dash when there is no VAT number', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...acme, vatNumber: null })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="vat"]')?.textContent?.trim()).toBe('—');
  });

  it('marks text as automatic direction so Arabic aligns naturally', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...acme, name: 'شركة الأمل' })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="name"]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[data-testid="name"]')?.textContent).toContain('شركة الأمل');
  });

  it('says "Customer not found" for an unknown customer', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));
    fixture.detectChanges();

    expect(root.textContent).toContain('Customer not found');
    expect(root.querySelector('[data-testid="status-badge"]')).toBeNull();
  });

  it('shows an error state for other failures', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="detail-error"]')).not.toBeNull();
  });

  it('links back to the list', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)));
    fixture.detectChanges();

    expect(root.querySelector('a[href="/customers"]')).not.toBeNull();
  });
});
