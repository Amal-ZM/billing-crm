import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
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

describe('ProductFormComponent (edit)', () => {
  const id = saved.id;
  let get: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup(loaded: Product | HttpErrorResponse = saved) {
    get = vi.fn().mockReturnValue(loaded instanceof HttpErrorResponse ? throwError(() => loaded) : of(loaded));
    update = vi.fn();
    TestBed.configureTestingModule({
      imports: [ProductFormComponent],
      providers: [
        provideRouter([]),
        { provide: ProductService, useValue: { get, update } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(ProductFormComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<ProductFormComponent>) => fixture.nativeElement as HTMLElement;
  const value = (fixture: ComponentFixture<ProductFormComponent>, control: string) =>
    (root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement).value;

  function set(fixture: ComponentFixture<ProductFormComponent>, control: string, text: string) {
    const element = root(fixture).querySelector(`[formControlName="${control}"]`) as HTMLInputElement;
    element.value = text;
    element.dispatchEvent(new Event(element.tagName === 'SELECT' ? 'change' : 'input'));
  }

  function submit(fixture: ComponentFixture<ProductFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  beforeEach(() => vi.restoreAllMocks());

  it('loads the product and pre-fills every field, the price with two decimals', () => {
    const fixture = setup({ ...saved, unitPrice: 250.5, vatPercent: 12.5, unit: 'Service' });

    expect(get).toHaveBeenCalledWith(id);
    expect(root(fixture).querySelector('h1')?.textContent).toContain('Edit product');
    expect(value(fixture, 'name')).toBe('Consulting hour');
    expect(value(fixture, 'sku')).toBe('CNS-001');
    expect(value(fixture, 'unit')).toBe('Service');
    expect(value(fixture, 'unitPrice')).toBe('250.50');
    expect(value(fixture, 'vatPercent')).toBe('12.5');
  });

  it('tells assistive technology that the VAT percent is required when editing', () => {
    const fixture = setup();

    expect((root(fixture).querySelector('[formControlName="vatPercent"]') as HTMLElement).getAttribute('aria-required')).toBe('true');
  });

  it('pre-fills a VAT of zero as 0 and does not turn it into the default', () => {
    const fixture = setup({ ...saved, vatPercent: 0 });

    expect(value(fixture, 'vatPercent')).toBe('0');
  });

  it('saves with an update for this product, numbers as typed, and goes back to its details', () => {
    const fixture = setup();
    update.mockReturnValue(of(saved));

    set(fixture, 'unitPrice', '120.75');
    set(fixture, 'vatPercent', '0');
    fixture.detectChanges();
    submit(fixture);

    expect(update).toHaveBeenCalledWith(id, {
      name: 'Consulting hour',
      sku: 'CNS-001',
      unit: 'Hour',
      unitPrice: 120.75,
      vatPercent: 0,
    });
    expect(navigate).toHaveBeenCalledWith(['/products', id], { replaceUrl: true });
  });

  it('puts a duplicate-SKU message on the SKU field and keeps every value', () => {
    const fixture = setup();
    update.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'Another product already uses this SKU.' } })),
    );

    set(fixture, 'sku', 'web-9');
    set(fixture, 'name', 'Edited Name');
    submit(fixture);

    expect(root(fixture).textContent).toContain('Another product already uses this SKU.');
    expect(value(fixture, 'sku')).toBe('web-9');
    expect(value(fixture, 'name')).toBe('Edited Name');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('requires the VAT percent when editing, because there is no default on an edit', () => {
    const fixture = setup();

    set(fixture, 'vatPercent', '  ');
    submit(fixture);

    expect(root(fixture).textContent).toContain('VAT percent is required.');
    expect(update).not.toHaveBeenCalled();
  });

  it('says "Product not found" and shows no form when the product does not exist', () => {
    const fixture = setup(new HttpErrorResponse({ status: 404 }));

    expect(root(fixture).textContent).toContain('Product not found');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('shows an error when the product cannot be loaded', () => {
    const fixture = setup(new HttpErrorResponse({ status: 500 }));

    expect(root(fixture).querySelector('[data-testid="load-error"]')).not.toBeNull();
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('still refuses invalid values when editing', () => {
    const fixture = setup();

    set(fixture, 'name', '   ');
    set(fixture, 'unitPrice', '10.005');
    fixture.detectChanges();
    submit(fixture);

    expect(root(fixture).textContent).toContain('Name is required.');
    expect(root(fixture).textContent).toContain('Unit price can have at most two decimal places.');
    expect(update).not.toHaveBeenCalled();
  });

  it('cancels back to the product\'s details', () => {
    const fixture = setup();

    expect(root(fixture).querySelector(`a[href="/products/${id}"]`)?.textContent).toContain('Cancel');
  });
});
