import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Customer } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { CustomerFormComponent } from './customer-form.component';

const saved: Customer = {
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

describe('CustomerFormComponent (edit)', () => {
  const id = saved.id;
  let get: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup(loaded: Customer | HttpErrorResponse = { ...saved, vatNumber: 'VAT-1' }) {
    get = vi.fn().mockReturnValue(loaded instanceof HttpErrorResponse ? throwError(() => loaded) : of(loaded));
    update = vi.fn();
    TestBed.configureTestingModule({
      imports: [CustomerFormComponent],
      providers: [
        provideRouter([]),
        { provide: CustomerService, useValue: { get, update } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(CustomerFormComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<CustomerFormComponent>) => fixture.nativeElement as HTMLElement;
  const value = (fixture: ComponentFixture<CustomerFormComponent>, control: string) =>
    (root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement).value;

  function set(fixture: ComponentFixture<CustomerFormComponent>, control: string, text: string) {
    const element = root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement;
    element.value = text;
    element.dispatchEvent(new Event('input'));
  }

  function submit(fixture: ComponentFixture<CustomerFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  beforeEach(() => vi.restoreAllMocks());

  it('loads the customer and pre-fills every field', () => {
    const fixture = setup();

    expect(get).toHaveBeenCalledWith(id);
    expect(root(fixture).querySelector('h1')?.textContent).toContain('Edit customer');
    expect(value(fixture, 'name')).toBe('Acme Trading');
    expect(value(fixture, 'type')).toBe('Company');
    expect(value(fixture, 'email')).toBe('accounts@acme.example');
    expect(value(fixture, 'phone')).toBe('0500000000');
    expect(value(fixture, 'vatNumber')).toBe('VAT-1');
    expect(value(fixture, 'address')).toBe('Somewhere');
  });

  it('shows an empty VAT field for a customer without one', () => {
    const fixture = setup({ ...saved, vatNumber: null });

    expect(value(fixture, 'vatNumber')).toBe('');
  });

  it('saves with an update for this customer and goes back to its details', () => {
    const fixture = setup();
    update.mockReturnValue(of(saved));

    set(fixture, 'phone', '0599999999');
    submit(fixture);

    expect(update).toHaveBeenCalledWith(id, {
      name: 'Acme Trading',
      type: 'Company',
      email: 'accounts@acme.example',
      phone: '0599999999',
      vatNumber: 'VAT-1',
      address: 'Somewhere',
    });
    expect(navigate).toHaveBeenCalledWith(['/customers', id], { replaceUrl: true });
  });

  it('puts a duplicate-email message on the email field and keeps every value', () => {
    const fixture = setup();
    update.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'Another customer already uses this email address.' } })),
    );

    set(fixture, 'email', 'taken@example.com');
    set(fixture, 'name', 'Edited Name');
    submit(fixture);

    expect(root(fixture).textContent).toContain('Another customer already uses this email address.');
    expect(value(fixture, 'email')).toBe('taken@example.com');
    expect(value(fixture, 'name')).toBe('Edited Name');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('says "Customer not found" and shows no form when the customer does not exist', () => {
    const fixture = setup(new HttpErrorResponse({ status: 404 }));

    expect(root(fixture).textContent).toContain('Customer not found');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('shows an error when the customer cannot be loaded', () => {
    const fixture = setup(new HttpErrorResponse({ status: 500 }));

    expect(root(fixture).querySelector('[data-testid="load-error"]')).not.toBeNull();
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('still requires the required fields when editing', () => {
    const fixture = setup();

    set(fixture, 'name', '   ');
    submit(fixture);

    expect(root(fixture).textContent).toContain('Name is required.');
    expect(update).not.toHaveBeenCalled();
  });
});
