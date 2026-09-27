import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { Product } from '../../core/products/product.models';
import { ProductService } from '../../core/products/product.service';
import { ProductDetailComponent } from './product-detail.component';

const consulting: Product = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Consulting hour',
  sku: 'CNS-001',
  unit: 'Hour',
  unitPrice: 100,
  vatPercent: 15,
  isActive: true,
  createdAtUtc: '2026-09-24T10:00:00Z',
  updatedAtUtc: '2026-09-25T11:30:00Z',
};

function setup(get: ReturnType<typeof vi.fn>, role: 'Admin' | 'Agent' = 'Admin') {
  TestBed.configureTestingModule({
    imports: [ProductDetailComponent],
    providers: [
      provideRouter([]),
      { provide: ProductService, useValue: { get } },
      { provide: AuthService, useValue: { role: () => role } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: consulting.id }) } } },
    ],
  });
  const fixture = TestBed.createComponent(ProductDetailComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('ProductDetailComponent', () => {
  it('shows a loading state first', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<Product>()));

    expect(root.querySelector('[data-testid="detail-loading"]')).not.toBeNull();
  });

  it('shows every field, the active badge and the created and changed times', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(consulting)));
    fixture.detectChanges();

    const text = root.textContent ?? '';
    for (const expected of ['Consulting hour', 'CNS-001', 'Hour']) {
      expect(text).toContain(expected);
    }
    expect(root.querySelector('[data-testid="unit-price"]')?.textContent).toContain('SAR');
    expect(root.querySelector('[data-testid="unit-price"]')?.textContent).toContain('100.00');
    expect(root.querySelector('[data-testid="vat-percent"]')?.textContent).toContain('15%');
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
    expect(root.querySelector('[data-testid="created-at"]')?.textContent?.trim()).not.toBe('');
    expect(root.querySelector('[data-testid="updated-at"]')?.textContent?.trim()).not.toBe('');
  });

  it('shows a price with one decimal as two decimals', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...consulting, unitPrice: 250.5, vatPercent: 0 })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="unit-price"]')?.textContent).toContain('250.50');
    expect(root.querySelector('[data-testid="vat-percent"]')?.textContent).toContain('0%');
  });

  it('shows Inactive for an inactive product', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...consulting, isActive: false })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Inactive');
  });

  it('keeps the SKU, price and VAT left-to-right so punctuation and the percent sign stay in place', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(consulting)));
    fixture.detectChanges();

    for (const id of ['sku', 'unit-price', 'vat-percent']) {
      expect(root.querySelector(`[data-testid="${id}"]`)?.getAttribute('dir')).toBe('ltr');
    }
  });

  it('marks text as automatic direction so an Arabic name aligns naturally', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...consulting, name: 'ساعة استشارة' })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="name"]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[data-testid="name"]')?.textContent).toContain('ساعة استشارة');
  });

  it('says "Product not found" for an unknown product', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));
    fixture.detectChanges();

    expect(root.textContent).toContain('Product not found');
    expect(root.querySelector('[data-testid="status-badge"]')).toBeNull();
  });

  it('shows an error state for other failures', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="detail-error"]')).not.toBeNull();
  });

  it('shows no Edit or Deactivate control to an Agent', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(consulting)), 'Agent');
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-active"]')).toBeNull();
    expect(root.querySelector('a[href$="/edit"]')).toBeNull();
    expect(root.textContent).not.toContain('Deactivate');
    expect(root.textContent).not.toContain('Edit');
  });

  it('links back to the list', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(consulting)));
    fixture.detectChanges();

    expect(root.querySelector('a[href="/products"]')).not.toBeNull();
  });
});
