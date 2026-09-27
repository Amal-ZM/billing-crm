import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Product } from '../../core/products/product.models';
import { ProductService } from '../../core/products/product.service';
import { ProductFormComponent } from './product-form.component';

const saved: Product = {
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

describe('ProductFormComponent (add)', () => {
  let create: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup() {
    create = vi.fn();
    TestBed.configureTestingModule({
      imports: [ProductFormComponent],
      providers: [
        provideRouter([]),
        { provide: ProductService, useValue: { create } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(ProductFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<ProductFormComponent>) => fixture.nativeElement as HTMLElement;
  const text = (fixture: ComponentFixture<ProductFormComponent>) => root(fixture).textContent ?? '';

  function set(fixture: ComponentFixture<ProductFormComponent>, control: string, value: string) {
    const element = root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event(element.tagName === 'SELECT' ? 'change' : 'input'));
  }

  function fillValid(fixture: ComponentFixture<ProductFormComponent>) {
    set(fixture, 'name', 'Consulting hour');
    set(fixture, 'sku', 'CNS-001');
    set(fixture, 'unit', 'Hour');
    set(fixture, 'unitPrice', '100');
    set(fixture, 'vatPercent', '15');
    fixture.detectChanges();
  }

  function submit(fixture: ComponentFixture<ProductFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  const value = (fixture: ComponentFixture<ProductFormComponent>, control: string) =>
    (root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement).value;

  beforeEach(() => vi.restoreAllMocks());

  it('pre-fills the VAT percent with 15 and starts with the Hour unit', () => {
    const fixture = setup();

    expect(value(fixture, 'vatPercent')).toBe('15');
    expect(value(fixture, 'unit')).toBe('Hour');
  });

  it('offers exactly the three units', () => {
    const fixture = setup();

    const options = Array.from((root(fixture).querySelector('[formControlName="unit"]') as HTMLSelectElement).options);
    expect(options.map((o) => o.textContent?.trim())).toEqual(['Hour', 'Item', 'Service']);
  });

  it('shows required-field errors and sends nothing when empty', () => {
    const fixture = setup();

    submit(fixture);

    expect(text(fixture)).toContain('Name is required.');
    expect(text(fixture)).toContain('SKU is required.');
    expect(text(fixture)).toContain('Unit price is required.');
    expect(create).not.toHaveBeenCalled();
  });

  it.each([
    ['-1', 'Unit price cannot be negative.'],
    ['-0.01', 'Unit price cannot be negative.'],
    ['10.005', 'Unit price can have at most two decimal places.'],
    ['abc', 'Unit price must be a number.'],
    ['1000000000', 'Unit price cannot be more than 999,999,999.99.'],
  ])('refuses the price "%s" in the form and sends nothing', (price, message) => {
    const fixture = setup();
    fillValid(fixture);
    set(fixture, 'unitPrice', price);
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain(message);
    expect(create).not.toHaveBeenCalled();
    expect(value(fixture, 'unitPrice')).toBe(price);
  });

  it.each([
    ['101', 'VAT percent must be between 0 and 100.'],
    ['-1', 'VAT percent must be between 0 and 100.'],
    ['15.005', 'VAT percent can have at most two decimal places.'],
    ['abc', 'VAT percent must be a number.'],
  ])('refuses the VAT percent "%s" in the form and sends nothing', (vat, message) => {
    const fixture = setup();
    fillValid(fixture);
    set(fixture, 'vatPercent', vat);
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain(message);
    expect(create).not.toHaveBeenCalled();
  });

  it('sends no VAT percent when it is left empty, so the server applies 15', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    set(fixture, 'vatPercent', '  ');
    fixture.detectChanges();
    submit(fixture);

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][0]).toEqual({ name: 'Consulting hour', sku: 'CNS-001', unit: 'Hour', unitPrice: 100 });
    expect('vatPercent' in create.mock.calls[0][0]).toBe(false);
  });

  it('refuses a SKU with characters outside letters, digits, hyphen and underscore', () => {
    const fixture = setup();
    fillValid(fixture);
    set(fixture, 'sku', 'AB CD');
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain('SKU can only contain letters A-Z, digits, hyphens and underscores.');
    expect(create).not.toHaveBeenCalled();
  });

  it('sends the numbers exactly as typed and the trimmed text', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    set(fixture, 'name', '  Consulting hour  ');
    set(fixture, 'sku', '  cns-001 ');
    set(fixture, 'unitPrice', '100.10');
    set(fixture, 'vatPercent', '0');
    fixture.detectChanges();
    submit(fixture);

    expect(create).toHaveBeenCalledWith({
      name: 'Consulting hour',
      sku: 'cns-001',
      unit: 'Hour',
      unitPrice: 100.1,
      vatPercent: 0,
    });
  });

  it('goes to the new product\'s details after saving', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    submit(fixture);

    expect(navigate).toHaveBeenCalledWith(['/products', saved.id], { replaceUrl: true });
  });

  it('puts the API message on the SKU field for a duplicate SKU and keeps every typed value', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'SKU already in use', detail: 'Another product already uses this SKU.' },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('Another product already uses this SKU.');
    expect(value(fixture, 'name')).toBe('Consulting hour');
    expect(value(fixture, 'sku')).toBe('CNS-001');
    expect(value(fixture, 'unitPrice')).toBe('100');
    expect(value(fixture, 'vatPercent')).toBe('15');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('maps a 400 to the named fields', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { unitPrice: ['Unit price must not be negative.'] } } })),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('Unit price must not be negative.');
    expect(value(fixture, 'unitPrice')).toBe('100');
  });

  it('shows a message at the top for a failure that belongs to no field', () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    fillValid(fixture);
    submit(fixture);

    const alert = root(fixture).querySelector('[data-testid="form-error"]');
    expect(alert?.textContent).toContain('Cannot reach the service');
    expect(value(fixture, 'name')).toBe('Consulting hour');
  });

  it('disables the save button while saving and ignores a second submit', () => {
    const fixture = setup();
    create.mockReturnValue(new Subject<Product>());

    fillValid(fixture);
    submit(fixture);
    submit(fixture);

    const button = root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(create).toHaveBeenCalledTimes(1);
  });

  it('marks the name as automatic direction and the SKU as left-to-right', () => {
    const fixture = setup();

    expect((root(fixture).querySelector('[formControlName="name"]') as HTMLElement).getAttribute('dir')).toBe('auto');
    expect((root(fixture).querySelector('[formControlName="sku"]') as HTMLElement).getAttribute('dir')).toBe('ltr');
  });
});
