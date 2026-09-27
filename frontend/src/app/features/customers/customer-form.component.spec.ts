import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
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

describe('CustomerFormComponent (add)', () => {
  let create: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup() {
    create = vi.fn();
    TestBed.configureTestingModule({
      imports: [CustomerFormComponent],
      providers: [
        provideRouter([]),
        { provide: CustomerService, useValue: { create } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(CustomerFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<CustomerFormComponent>) => fixture.nativeElement as HTMLElement;
  const text = (fixture: ComponentFixture<CustomerFormComponent>) => root(fixture).textContent ?? '';

  function set(fixture: ComponentFixture<CustomerFormComponent>, control: string, value: string) {
    const element = root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event(element.tagName === 'SELECT' ? 'change' : 'input'));
  }

  function fillValid(fixture: ComponentFixture<CustomerFormComponent>) {
    set(fixture, 'name', 'Acme Trading');
    set(fixture, 'type', 'Company');
    set(fixture, 'email', 'accounts@acme.example');
    set(fixture, 'phone', '0500000000');
    set(fixture, 'address', 'Somewhere');
    fixture.detectChanges();
  }

  function submit(fixture: ComponentFixture<CustomerFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  const value = (fixture: ComponentFixture<CustomerFormComponent>, control: string) =>
    (root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement).value;

  beforeEach(() => vi.restoreAllMocks());

  it('shows required-field errors and sends nothing when empty', () => {
    const fixture = setup();

    submit(fixture);

    expect(text(fixture)).toContain('Name is required.');
    expect(text(fixture)).toContain('Email is required.');
    expect(text(fixture)).toContain('Phone is required.');
    expect(text(fixture)).toContain('Address is required.');
    expect(create).not.toHaveBeenCalled();
  });

  it('does not require a VAT number and sends an empty string for it', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    submit(fixture);

    expect(create).toHaveBeenCalledWith({
      name: 'Acme Trading',
      type: 'Company',
      email: 'accounts@acme.example',
      phone: '0500000000',
      vatNumber: '',
      address: 'Somewhere',
    });
  });

  it('goes to the new customer\'s details after saving', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    submit(fixture);

    expect(navigate).toHaveBeenCalledWith(['/customers', saved.id], { replaceUrl: true });
  });

  it('puts the API message on the email field for a duplicate email and keeps every typed value', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'Email already in use', detail: 'Another customer already uses this email address.' },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('Another customer already uses this email address.');
    expect(value(fixture, 'name')).toBe('Acme Trading');
    expect(value(fixture, 'email')).toBe('accounts@acme.example');
    expect(value(fixture, 'phone')).toBe('0500000000');
    expect(value(fixture, 'address')).toBe('Somewhere');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('maps a 400 to the named fields', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { phone: ['Phone must be 7 to 20 characters.'] } } })),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('Phone must be 7 to 20 characters.');
    expect(value(fixture, 'phone')).toBe('0500000000');
  });

  it('shows a message at the top for a failure that belongs to no field', () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    fillValid(fixture);
    submit(fixture);

    const alert = root(fixture).querySelector('[data-testid="form-error"]');
    expect(alert?.textContent).toContain('Cannot reach the service');
    expect(value(fixture, 'name')).toBe('Acme Trading');
  });

  it('disables the save button while saving and ignores a second submit', () => {
    const fixture = setup();
    create.mockReturnValue(new Subject<Customer>());

    fillValid(fixture);
    submit(fixture);
    submit(fixture);

    const button = root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(create).toHaveBeenCalledTimes(1);
  });

  it('marks text controls as automatic direction so Arabic text aligns naturally', () => {
    const fixture = setup();

    for (const control of ['name', 'email', 'address']) {
      expect((root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLElement).getAttribute('dir')).toBe('auto');
    }
  });

  it('starts with Company selected', () => {
    const fixture = setup();

    expect(value(fixture, 'type')).toBe('Company');
  });
});
