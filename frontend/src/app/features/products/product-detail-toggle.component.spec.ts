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
  updatedAtUtc: '2026-09-24T10:00:00Z',
};

function setup(product: Product, setActive: ReturnType<typeof vi.fn>, role: 'Admin' | 'Agent' = 'Admin') {
  TestBed.configureTestingModule({
    imports: [ProductDetailComponent],
    providers: [
      provideRouter([]),
      { provide: ProductService, useValue: { get: vi.fn().mockReturnValue(of(product)), setActive } },
      { provide: AuthService, useValue: { role: () => role } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: product.id }) } } },
    ],
  });
  const fixture = TestBed.createComponent(ProductDetailComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

const toggle = (root: HTMLElement) => root.querySelector('[data-testid="toggle-active"]') as HTMLButtonElement;

describe('ProductDetailComponent edit and active state (Admin)', () => {
  it('links to the edit page', () => {
    const { root } = setup(consulting, vi.fn());

    expect(root.querySelector(`a[href="/products/${consulting.id}/edit"]`)?.textContent).toContain('Edit');
  });

  it('offers Deactivate for an active product and, when pressed, shows it as Inactive with an Activate button', () => {
    const setActive = vi.fn().mockReturnValue(of({ ...consulting, isActive: false }));
    const { fixture, root } = setup(consulting, setActive);
    expect(toggle(root).textContent).toContain('Deactivate');

    toggle(root).click();
    fixture.detectChanges();

    expect(setActive).toHaveBeenCalledWith(consulting.id, false);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Inactive');
    expect(toggle(root).textContent).toContain('Activate');
    expect(root.textContent).toContain('Consulting hour');
    expect(root.querySelector('[data-testid="toggle-done"]')?.textContent).toContain('Product deactivated.');
  });

  it('offers Activate for an inactive product and makes it active again', () => {
    const setActive = vi.fn().mockReturnValue(of(consulting));
    const { fixture, root } = setup({ ...consulting, isActive: false }, setActive);
    expect(toggle(root).textContent).toContain('Activate');

    toggle(root).click();
    fixture.detectChanges();

    expect(setActive).toHaveBeenCalledWith(consulting.id, true);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
    expect(toggle(root).textContent).toContain('Deactivate');
    expect(root.querySelector('[data-testid="toggle-done"]')?.textContent).toContain('Product activated.');
  });

  it('shows an error and keeps the previous state when the change fails', () => {
    const setActive = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const { fixture, root } = setup(consulting, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-error"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
    expect(toggle(root).disabled).toBe(false);
  });

  it('shows a refusal from the server (for example a 403) and keeps the previous state', () => {
    const setActive = vi.fn().mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 403, error: { title: 'Forbidden', detail: 'You cannot do this.' } })),
    );
    const { fixture, root } = setup(consulting, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-error"]')?.textContent).toContain('You cannot do this.');
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Active');
  });

  it('disables the button while the change is in flight', () => {
    const setActive = vi.fn().mockReturnValue(new Subject<Product>());
    const { fixture, root } = setup(consulting, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(toggle(root).disabled).toBe(true);
  });

  it('tells the person when the product no longer exists', () => {
    const setActive = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const { fixture, root } = setup(consulting, setActive);

    toggle(root).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="toggle-error"]')?.textContent).toContain('no longer exists');
  });
});

describe('ProductDetailComponent for an Agent', () => {
  it('renders neither Edit nor Deactivate nor Activate, for an active or an inactive product', () => {
    for (const isActive of [true, false]) {
      TestBed.resetTestingModule();
      const { root } = setup({ ...consulting, isActive }, vi.fn(), 'Agent');

      expect(toggle(root)).toBeNull();
      expect(root.querySelector(`a[href="/products/${consulting.id}/edit"]`)).toBeNull();
    }
  });
});
