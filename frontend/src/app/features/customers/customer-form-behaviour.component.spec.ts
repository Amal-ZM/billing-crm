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

describe('CustomerFormComponent behaviour', () => {
  let create: ReturnType<typeof vi.fn>;
  let get: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup(routeParams: Record<string, string> = {}) {
    create = vi.fn();
    get = vi.fn().mockReturnValue(new Subject<Customer>()); // edit mode loads on start; never answers unless a test says so
    TestBed.configureTestingModule({
      imports: [CustomerFormComponent],
      providers: [
        provideRouter([]),
        { provide: CustomerService, useValue: { create, get, update: vi.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(routeParams) } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(CustomerFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<CustomerFormComponent>) => fixture.nativeElement as HTMLElement;
  const field = (fixture: ComponentFixture<CustomerFormComponent>, control: string) =>
    root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement;

  function set(fixture: ComponentFixture<CustomerFormComponent>, control: string, text: string) {
    field(fixture, control).value = text;
    field(fixture, control).dispatchEvent(new Event(field(fixture, control).tagName === 'SELECT' ? 'change' : 'input'));
  }

  function fill(fixture: ComponentFixture<CustomerFormComponent>, overrides: Record<string, string> = {}) {
    const values = { name: 'Acme Trading', email: 'accounts@acme.example', phone: '0500000000', address: 'Somewhere', ...overrides };
    for (const [control, text] of Object.entries(values)) {
      set(fixture, control, text);
    }
    fixture.detectChanges();
  }

  function submit(fixture: ComponentFixture<CustomerFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

  beforeEach(() => vi.restoreAllMocks());

  it('sends every text field without its surrounding spaces', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fill(fixture, { name: '  Acme  ', email: '  A@B.CO ', phone: ' 0500000000 ', address: '  Somewhere  ' });
    set(fixture, 'vatNumber', '  V-1  ');
    submit(fixture);

    expect(create).toHaveBeenCalledWith({
      name: 'Acme',
      type: 'Company',
      email: 'A@B.CO',
      phone: '0500000000',
      vatNumber: 'V-1',
      address: 'Somewhere',
    });
  });

  it('counts limits on the trimmed text, like the server: padding is fine, one character too many is not', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fill(fixture, { name: `  ${'a'.repeat(200)}  ` });
    submit(fixture);
    expect(create).toHaveBeenCalledTimes(1);

    fill(fixture, { name: 'a'.repeat(201) });
    submit(fixture);
    expect(create).toHaveBeenCalledTimes(1);
    expect(root(fixture).textContent).toContain('Name must be at most 200 characters.');
  });

  it('says how long the other limited fields may be', () => {
    const fixture = setup();

    fill(fixture, { email: `${'a'.repeat(252)}@x.co`, address: 'a'.repeat(501), phone: '1'.repeat(21) });
    set(fixture, 'vatNumber', '9'.repeat(31));
    submit(fixture);

    const text = root(fixture).textContent ?? '';
    expect(text).toContain('Email must be at most 256 characters.');
    expect(text).toContain('Address must be at most 500 characters.');
    expect(text).toContain('VAT number must be at most 30 characters.');
    expect(text).toContain('Phone must be at most 20 characters.');
    expect(create).not.toHaveBeenCalled();
  });

  it('marks the required fields as required for assistive technology, and not the VAT number', () => {
    const fixture = setup();

    for (const control of ['name', 'type', 'email', 'phone', 'address']) {
      expect(field(fixture, control).hasAttribute('required')).toBe(true);
    }
    expect(field(fixture, 'vatNumber').hasAttribute('required')).toBe(false);
  });

  it('moves focus to the first invalid field after an invalid submit', async () => {
    const fixture = setup();
    fill(fixture, { name: 'Acme', email: 'not-an-email' });

    submit(fixture);
    await tick();

    expect(document.activeElement).toBe(field(fixture, 'email'));
  });

  it('moves focus to the email field after a duplicate-email refusal', async () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'Another customer already uses this email address.' } })));
    fill(fixture);

    submit(fixture);
    fixture.detectChanges();
    await tick();

    expect(document.activeElement).toBe(field(fixture, 'email'));
  });

  it('will not resubmit an unchanged refused form but does say where the problem is', async () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'Another customer already uses this email address.' } })));
    fill(fixture);
    submit(fixture);
    await tick();
    (document.activeElement as HTMLElement).blur();

    submit(fixture);
    await tick();

    expect(create).toHaveBeenCalledTimes(1);
    expect(document.activeElement).toBe(field(fixture, 'email'));
    expect(root(fixture).textContent).toContain('Another customer already uses this email address.');
  });

  it('clears the API message on a field as soon as it is edited', () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'Another customer already uses this email address.' } })));
    fill(fixture);
    submit(fixture);
    expect(root(fixture).textContent).toContain('Another customer already uses this email address.');

    set(fixture, 'email', 'someone.else@example.com');
    fixture.detectChanges();

    expect(root(fixture).textContent).not.toContain('Another customer already uses this email address.');
  });

  it('still tells the person something went wrong when a 400 names nothing usable', () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errors: {} } })));
    fill(fixture);

    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="form-error"]')?.textContent).toContain('could not be saved');
  });

  it('does not navigate if the person leaves the page while the save is still in flight', () => {
    const fixture = setup();
    const response = new Subject<Customer>();
    create.mockReturnValue(response);
    fill(fixture);
    submit(fixture);

    fixture.destroy();
    response.next(saved);

    expect(navigate).not.toHaveBeenCalled();
  });

  it('offers Retry when the customer to edit cannot be loaded, and shows the form after a successful retry', () => {
    const fixture = setup({ id: saved.id });
    // setup() runs ngOnInit with get() returning undefined; drive the failure and retry explicitly
    get.mockReset();
    get.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 }))).mockReturnValueOnce(of(saved));
    (fixture.componentInstance as unknown as { load(): void }).load();
    fixture.detectChanges();
    expect(root(fixture).querySelector('[data-testid="load-error"]')).not.toBeNull();

    const retry = Array.from(root(fixture).querySelectorAll('button')).find((b) => b.textContent?.includes('Retry')) as HTMLButtonElement;
    retry.click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('form')).not.toBeNull();
    expect(field(fixture, 'name').value).toBe('Acme Trading');
  });

  it('cancels back to the customer list when adding and to the details when editing', () => {
    const adding = setup();
    expect(root(adding).querySelector('a[href="/customers"]')?.textContent).toContain('Cancel');
    TestBed.resetTestingModule();

    const editing = setup({ id: saved.id });
    get.mockReturnValue(of(saved));
    (editing.componentInstance as unknown as { load(): void }).load();
    editing.detectChanges();
    expect(root(editing).querySelector(`a[href="/customers/${saved.id}"]`)?.textContent).toContain('Cancel');
  });
});
