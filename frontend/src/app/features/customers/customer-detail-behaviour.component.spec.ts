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
  updatedAtUtc: '2026-09-25T11:30:00Z',
};

function setup(get: ReturnType<typeof vi.fn>, setActive: ReturnType<typeof vi.fn> = vi.fn()) {
  TestBed.configureTestingModule({
    imports: [CustomerDetailComponent],
    providers: [
      provideRouter([]),
      { provide: CustomerService, useValue: { get, setActive } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: acme.id }) } } },
    ],
  });
  const fixture = TestBed.createComponent(CustomerDetailComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('CustomerDetailComponent behaviour', () => {
  it('shows the created and last-changed times as real dates', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="created-at"]')?.textContent).toMatch(/2026/);
    expect(root.querySelector('[data-testid="updated-at"]')?.textContent).toMatch(/2026/);
    expect(root.querySelector('[data-testid="created-at"]')?.textContent).not.toBe(root.querySelector('[data-testid="updated-at"]')?.textContent);
  });

  it('offers Retry when the customer cannot be loaded, and shows the customer after it works', () => {
    const get = vi.fn().mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 }))).mockReturnValueOnce(of(acme));
    const { fixture, root } = setup(get);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="detail-error"]')).not.toBeNull();

    (Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Retry')) as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[data-testid="name"]')?.textContent).toContain('Acme Trading');
  });

  it('shows what the API said when the customer cannot be loaded', () => {
    const { fixture, root } = setup(
      vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500, error: { detail: 'The database is busy.' } }))),
    );
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="detail-error"]')?.textContent).toContain('The database is busy.');
  });

  it('ignores a second click while the change is still in flight', () => {
    const setActive = vi.fn().mockReturnValue(new Subject<Customer>());
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)), setActive);
    const button = root.querySelector('[data-testid="toggle-active"]') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();
    button.click();

    expect(setActive).toHaveBeenCalledTimes(1);
    expect(root.querySelector('[data-testid="toggle-pending"]')?.textContent).toContain('Saving');
  });

  it('announces the result of deactivating and of activating', () => {
    const setActive = vi.fn().mockReturnValueOnce(of({ ...acme, isActive: false })).mockReturnValueOnce(of(acme));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)), setActive);
    const button = () => root.querySelector('[data-testid="toggle-active"]') as HTMLButtonElement;

    button().click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="toggle-done"]')?.textContent).toContain('Customer deactivated.');
    expect(root.querySelector('[data-testid="toggle-done"]')?.getAttribute('role')).toBe('status');

    button().click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="toggle-done"]')?.textContent).toContain('Customer activated.');
  });

  it('clears the previous announcement when a new change starts', () => {
    const setActive = vi.fn().mockReturnValueOnce(of({ ...acme, isActive: false })).mockReturnValueOnce(new Subject<Customer>());
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)), setActive);
    const button = () => root.querySelector('[data-testid="toggle-active"]') as HTMLButtonElement;

    button().click();
    fixture.detectChanges();
    button().click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-done"]')).toBeNull();
  });

  it('shows the API detail when the change is refused', () => {
    const setActive = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'Cannot change this customer right now.' } })));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(acme)), setActive);

    (root.querySelector('[data-testid="toggle-active"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-error"]')?.textContent).toContain('Cannot change this customer right now.');
  });
});
