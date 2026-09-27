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
  email: 'accounts@acme.example',
  phone: '0500000000',
  vatNumber: null,
  address: 'Somewhere',
  isActive: true,
  createdAtUtc: '2026-09-24T10:00:00Z',
  updatedAtUtc: '2026-09-24T10:00:00Z',
};

function setup(customer: Customer, setActive: ReturnType<typeof vi.fn>) {
  TestBed.configureTestingModule({
    imports: [CustomerDetailComponent],
    providers: [
      provideRouter([]),
      { provide: CustomerService, useValue: { get: vi.fn().mockReturnValue(of(customer)), setActive } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: customer.id }) } } },
    ],
  });
  const fixture = TestBed.createComponent(CustomerDetailComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

const toggle = (root: HTMLElement) => root.querySelector('[data-testid="toggle-active"]') as HTMLButtonElement;

describe('CustomerDetailComponent edit and active state', () => {
  it('links to the edit page', () => {
    const { root } = setup(acme, vi.fn());

    expect(root.querySelector(`a[href="/customers/${acme.id}/edit"]`)?.textContent).toContain('Edit');
  });

  it('offers Deactivate for an active customer and, when pressed, shows it as Inactive with an Activate button', () => {
    const setActive = vi.fn().mockReturnValue(of({ ...acme, isActive: false }));
    const { fixture, root } = setup(acme, setActive);
    expect(toggle(root).textContent).toContain('Deactivate');

    toggle(root).click();
    fixture.detectChanges();

    expect(setActive).toHaveBeenCalledWith(acme.id, false);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Inactive');
    expect(toggle(root).textContent).toContain('Activate');
    expect(root.textContent).toContain('Acme Trading');
  });

  it('offers Activate for an inactive customer and makes it active again', () => {
    const setActive = vi.fn().mockReturnValue(of(acme));
    const { fixture, root } = setup({ ...acme, isActive: false }, setActive);
    expect(toggle(root).textContent).toContain('Activate');

    toggle(root).click();
    fixture.detectChanges();

    expect(setActive).toHaveBeenCalledWith(acme.id, true);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
    expect(toggle(root).textContent).toContain('Deactivate');
  });

  it('shows an error and keeps the previous state when the change fails', () => {
    const setActive = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const { fixture, root } = setup(acme, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-error"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
    expect(toggle(root).disabled).toBe(false);
  });

  it('disables the button while the change is in flight', () => {
    const setActive = vi.fn().mockReturnValue(new Subject<Customer>());
    const { fixture, root } = setup(acme, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(toggle(root).disabled).toBe(true);
  });

  it('tells the person when the customer no longer exists', () => {
    const setActive = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const { fixture, root } = setup(acme, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-error"]')?.textContent).toContain('no longer exists');
  });
});
