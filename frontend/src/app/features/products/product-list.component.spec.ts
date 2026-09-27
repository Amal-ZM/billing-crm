import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { PagedResult, ProductListItem } from '../../core/products/product.models';
import { ProductService } from '../../core/products/product.service';
import { ProductListComponent } from './product-list.component';

const consulting: ProductListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Consulting hour',
  sku: 'CNS-001',
  unit: 'Hour',
  unitPrice: 100,
  vatPercent: 15,
  isActive: true,
};

const retired: ProductListItem = { ...consulting, id: '22222222-2222-2222-2222-222222222222', name: 'Retired item', sku: 'OLD-1', unit: 'Item', isActive: false };

const page = (items: ProductListItem[], totalCount = items.length): PagedResult<ProductListItem> => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount,
  totalPages: totalCount === 0 ? 0 : Math.ceil(totalCount / 20),
});

function setup(list: ReturnType<typeof vi.fn>, query: Record<string, string> = {}, role: 'Admin' | 'Agent' = 'Admin') {
  TestBed.configureTestingModule({
    imports: [ProductListComponent],
    providers: [
      provideRouter([]),
      { provide: ProductService, useValue: { list } },
      { provide: AuthService, useValue: { role: () => role } },
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(query)) } },
    ],
  });
  const fixture = TestBed.createComponent(ProductListComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('ProductListComponent', () => {
  it('shows a loading state while the request is in flight', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<PagedResult<ProductListItem>>()));

    expect(root.querySelector('[data-testid="list-loading"]')).not.toBeNull();
  });

  it('shows a row per product with name, SKU, unit, price in SAR with two decimals and VAT percent', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([consulting]))));
    fixture.detectChanges();

    const rows = root.querySelectorAll('[data-testid="product-row"]');
    expect(rows.length).toBe(1);
    const text = rows[0].textContent ?? '';
    for (const expected of ['Consulting hour', 'CNS-001', 'Hour', 'SAR', '100.00', '15%']) {
      expect(text).toContain(expected);
    }
    expect(rows[0].querySelector('[data-testid="inactive-badge"]')).toBeNull();
  });

  it('shows two decimals for a price that has one and groups thousands', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([{ ...consulting, unitPrice: 1234.5 }]))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="product-row"]')?.textContent).toContain('1,234.50');
  });

  it('marks an inactive product clearly when the list contains one', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([consulting, retired]))));
    fixture.detectChanges();

    const rows = Array.from(root.querySelectorAll('[data-testid="product-row"]'));
    const inactive = rows.find((row) => (row.textContent ?? '').includes('Retired item'));
    expect(inactive?.querySelector('[data-testid="inactive-badge"]')?.textContent).toContain('Inactive');
  });

  it('links each row to its details page', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([consulting]))));
    fixture.detectChanges();

    expect(root.querySelector('a[href="/products/11111111-1111-1111-1111-111111111111"]')).not.toBeNull();
  });

  it('shows the total number of products and a paginator', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([consulting], 41))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="total"]')?.textContent).toContain('41');
    expect(root.querySelector('mat-paginator')).not.toBeNull();
  });

  it('shows "No products match" instead of a blank screen when the list is empty', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([]))));
    fixture.detectChanges();

    expect(root.textContent).toContain('No products match');
    expect(root.querySelector('[data-testid="product-row"]')).toBeNull();
  });

  it('shows an error with a retry button, and retry asks again', () => {
    const list = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
      .mockReturnValueOnce(of(page([consulting])));
    const { fixture, root } = setup(list);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="list-error"]')).not.toBeNull();

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(list).toHaveBeenCalledTimes(2);
    expect(root.querySelectorAll('[data-testid="product-row"]').length).toBe(1);
  });

  it('offers an Add product button to an Admin', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(page([]))), {}, 'Admin');

    expect(root.querySelector('a[href="/products/new"]')?.textContent).toContain('Add product');
  });

  it('does not show an Add product button to an Agent', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(page([consulting]))), {}, 'Agent');

    expect(root.querySelector('a[href="/products/new"]')).toBeNull();
    expect(root.textContent).not.toContain('Add product');
  });

  it('requests the page and page size from the query string', () => {
    const list = vi.fn().mockReturnValue(of(page([consulting], 41)));

    setup(list, { page: '2', pageSize: '10' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ page: 2, pageSize: 10 }));
  });

  it('uses page 1 of 20 when the query string has none', () => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list);

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
  });

  it('clamps unsafe page and page size values in the address to safe defaults', () => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list, { page: '-3', pageSize: '100000' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
  });
});

describe('ProductListComponent search and filters', () => {
  const emptyPage = page([]);

  function setupWithRouter(
    query: Record<string, string> = {},
    role: 'Admin' | 'Agent' = 'Admin',
    list = vi.fn().mockReturnValue(of(emptyPage)),
  ) {
    const view = setup(list, query, role);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    return { ...view, list, navigate };
  }

  const searchBox = (root: HTMLElement) => root.querySelector('input[data-testid="search"]') as HTMLInputElement;
  const unitSelect = (root: HTMLElement) => root.querySelector('select[data-testid="unit-filter"]') as HTMLSelectElement;
  const statusSelect = (root: HTMLElement) => root.querySelector('select[data-testid="status-filter"]') as HTMLSelectElement;

  function type(input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function choose(select: HTMLSelectElement, value: string) {
    select.value = value;
    select.dispatchEvent(new Event('change'));
  }

  afterEach(() => vi.useRealTimers());

  it('waits for a pause in typing, then puts the search text in the URL and goes back to page 1', () => {
    vi.useFakeTimers();
    const { root, navigate } = setupWithRouter({ page: '3' });

    type(searchBox(root), 'cn');
    vi.advanceTimersByTime(299);
    expect(navigate).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate.mock.calls[0][1]).toEqual(
      expect.objectContaining({ queryParams: { q: 'cn', page: null }, queryParamsHandling: 'merge' }),
    );
  });

  it('sends only the final text when typing continues within the pause', () => {
    vi.useFakeTimers();
    const { root, navigate } = setupWithRouter();

    type(searchBox(root), 'c');
    vi.advanceTimersByTime(200);
    type(searchBox(root), 'cn');
    vi.advanceTimersByTime(200);
    type(searchBox(root), 'cns');
    vi.advanceTimersByTime(300);

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { q: 'cns', page: null } }));
  });

  it('clearing the search text removes it from the URL', () => {
    vi.useFakeTimers();
    const { root, navigate } = setupWithRouter({ q: 'cns' });

    type(searchBox(root), '');
    vi.advanceTimersByTime(300);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { q: null, page: null } }));
  });

  it.each([
    ['Hour', 'Hour'],
    ['Item', 'Item'],
    ['Service', 'Service'],
    ['', null],
  ])('choosing unit "%s" puts %s in the URL at once and goes back to page 1', (chosen, expected) => {
    const { root, navigate } = setupWithRouter({ page: '2' });

    choose(unitSelect(root), chosen);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { unit: expected, page: null } }));
  });

  it.each([
    ['active', null],
    ['inactive', 'inactive'],
    ['all', 'all'],
  ])('an Admin choosing status "%s" puts %s in the URL at once and goes back to page 1', (chosen, expected) => {
    const { root, navigate } = setupWithRouter({ page: '2' });

    choose(statusSelect(root), chosen);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { status: expected, page: null } }));
  });

  it('offers All, Hour, Item and Service for the unit and Active, Inactive and All for the status', () => {
    const { root } = setupWithRouter();

    expect(Array.from(unitSelect(root).options).map((o) => o.textContent?.trim())).toEqual(['All', 'Hour', 'Item', 'Service']);
    expect(Array.from(statusSelect(root).options).map((o) => o.textContent?.trim())).toEqual(['Active', 'Inactive', 'All']);
  });

  it('shows the status filter to an Admin and not to an Agent', () => {
    expect(statusSelect(setupWithRouter({}, 'Admin').root)).not.toBeNull();
    TestBed.resetTestingModule();
    expect(statusSelect(setupWithRouter({}, 'Agent').root)).toBeNull();
  });

  it('restores the controls and the request from the URL for an Admin', () => {
    const { fixture, root, list } = setupWithRouter({ q: 'cns', unit: 'Hour', status: 'all', page: '2' });
    fixture.detectChanges();

    expect(searchBox(root).value).toBe('cns');
    expect(unitSelect(root).value).toBe('Hour');
    expect(statusSelect(root).value).toBe('all');
    expect(list).toHaveBeenCalledWith({ q: 'cns', unit: 'Hour', status: 'all', page: 2, pageSize: 20 });
  });

  it('shows Active and sends no status when the URL has none', () => {
    const { root, list } = setupWithRouter();

    expect(statusSelect(root).value).toBe('active');
    expect(list).toHaveBeenCalledWith(expect.objectContaining({ status: undefined }));
  });

  it('never sends a status from the address for an Agent', () => {
    const { list } = setupWithRouter({ status: 'all', unit: 'Item' }, 'Agent');

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ status: undefined, unit: 'Item' }));
  });

  it('drops search text over 100 characters from the address instead of asking the API for it', () => {
    const { list, root } = setupWithRouter({ q: 'x'.repeat(101), unit: 'Item' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ q: undefined, unit: 'Item' }));
    expect(searchBox(root).value).toBe('');
  });

  it('keeps search text of exactly 100 characters and limits the box to 100', () => {
    const text = 'x'.repeat(100);
    const { list, root } = setupWithRouter({ q: text });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ q: text }));
    expect(searchBox(root).getAttribute('maxlength')).toBe('100');
  });

  it('ignores unknown unit and status values in the URL', () => {
    const { list } = setupWithRouter({ unit: 'Box', status: 'everything' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ unit: undefined, status: undefined }));
  });

  it('keeps the typed search text on screen when nothing matches', () => {
    const { fixture, root } = setupWithRouter({ q: 'zzz' });
    fixture.detectChanges();

    expect(root.textContent).toContain('No products match');
    expect(searchBox(root).value).toBe('zzz');
  });

  it('marks the search text as automatic direction for Arabic', () => {
    const { root } = setupWithRouter();

    expect(searchBox(root).getAttribute('dir')).toBe('auto');
  });

  it('gives the search box and the filters accessible labels', () => {
    const { root } = setupWithRouter();

    expect(searchBox(root).getAttribute('aria-label')).toBeTruthy();
    expect(unitSelect(root).getAttribute('aria-label')).toBeTruthy();
    expect(statusSelect(root).getAttribute('aria-label')).toBeTruthy();
  });
});
