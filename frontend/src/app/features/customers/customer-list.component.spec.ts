import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CustomerListItem, PagedResult } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { CustomerListComponent } from './customer-list.component';

const acme: CustomerListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Acme Trading',
  type: 'Company',
  email: 'accounts@acme.example',
  phone: '0500000000',
  vatNumber: '300000000000003',
  isActive: true,
};

const dormant: CustomerListItem = { ...acme, id: '22222222-2222-2222-2222-222222222222', name: 'Dormant Ltd', email: 'd@x.test', isActive: false };

const page = (items: CustomerListItem[], totalCount = items.length): PagedResult<CustomerListItem> => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount,
  totalPages: totalCount === 0 ? 0 : Math.ceil(totalCount / 20),
});

function setup(list: ReturnType<typeof vi.fn>, query: Record<string, string> = {}) {
  TestBed.configureTestingModule({
    imports: [CustomerListComponent],
    providers: [
      provideRouter([]),
      { provide: CustomerService, useValue: { list } },
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(query)) } },
    ],
  });
  const fixture = TestBed.createComponent(CustomerListComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('CustomerListComponent', () => {
  it('shows a loading state while the request is in flight', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<PagedResult<CustomerListItem>>()));

    expect(root.querySelector('[data-testid="list-loading"]')).not.toBeNull();
  });

  it('shows a row per customer with name, type, email, phone and VAT number', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([acme]))));
    fixture.detectChanges();

    const rows = root.querySelectorAll('[data-testid="customer-row"]');
    expect(rows.length).toBe(1);
    const text = rows[0].textContent ?? '';
    for (const expected of ['Acme Trading', 'Company', 'accounts@acme.example', '0500000000', '300000000000003']) {
      expect(text).toContain(expected);
    }
    expect(rows[0].querySelector('[data-testid="inactive-badge"]')).toBeNull();
  });

  it('marks inactive customers clearly and still lists them', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([acme, dormant]))));
    fixture.detectChanges();

    const rows = Array.from(root.querySelectorAll('[data-testid="customer-row"]'));
    expect(rows.length).toBe(2);
    const inactive = rows.find((row) => (row.textContent ?? '').includes('Dormant Ltd'));
    expect(inactive?.querySelector('[data-testid="inactive-badge"]')?.textContent).toContain('Inactive');
  });

  it('links each row to its details page', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([acme]))));
    fixture.detectChanges();

    expect(root.querySelector('a[href="/customers/11111111-1111-1111-1111-111111111111"]')).not.toBeNull();
  });

  it('shows the total number of customers', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([acme], 41))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="total"]')?.textContent).toContain('41');
    expect(root.querySelector('mat-paginator')).not.toBeNull();
  });

  it('shows "No customers match" instead of a blank screen when the list is empty', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([]))));
    fixture.detectChanges();

    expect(root.textContent).toContain('No customers match');
    expect(root.querySelector('[data-testid="customer-row"]')).toBeNull();
  });

  it('shows an error with a retry button, and retry asks again', () => {
    const list = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
      .mockReturnValueOnce(of(page([acme])));
    const { fixture, root } = setup(list);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="list-error"]')).not.toBeNull();

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(list).toHaveBeenCalledTimes(2);
    expect(root.querySelectorAll('[data-testid="customer-row"]').length).toBe(1);
  });

  it('offers an Add customer button', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(page([]))));

    expect(root.querySelector('a[href="/customers/new"]')?.textContent).toContain('Add customer');
  });

  it('requests the page and page size from the query string', () => {
    const list = vi.fn().mockReturnValue(of(page([acme], 41)));

    setup(list, { page: '2', pageSize: '10' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ page: 2, pageSize: 10 }));
  });

  it('uses page 1 of 20 when the query string has none', () => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list);

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
  });
});

describe('CustomerListComponent search and filters', () => {
  const emptyPage = page([]);

  function setupWithRouter(query: Record<string, string> = {}, list = vi.fn().mockReturnValue(of(emptyPage))) {
    const view = setup(list, query);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    return { ...view, list, navigate };
  }

  const searchBox = (root: HTMLElement) => root.querySelector('input[data-testid="search"]') as HTMLInputElement;
  const typeSelect = (root: HTMLElement) => root.querySelector('select[data-testid="type-filter"]') as HTMLSelectElement;
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

    type(searchBox(root), 'ac');
    vi.advanceTimersByTime(299);
    expect(navigate).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate.mock.calls[0][1]).toEqual(
      expect.objectContaining({ queryParams: { q: 'ac', page: null }, queryParamsHandling: 'merge' }),
    );
  });

  it('sends only the final text when typing continues within the pause', () => {
    vi.useFakeTimers();
    const { root, navigate } = setupWithRouter();

    type(searchBox(root), 'a');
    vi.advanceTimersByTime(200);
    type(searchBox(root), 'ac');
    vi.advanceTimersByTime(200);
    type(searchBox(root), 'acm');
    vi.advanceTimersByTime(300);

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { q: 'acm', page: null } }));
  });

  it('clearing the search text removes it from the URL', () => {
    vi.useFakeTimers();
    const { root, navigate } = setupWithRouter({ q: 'acme' });

    type(searchBox(root), '');
    vi.advanceTimersByTime(300);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { q: null, page: null } }));
  });

  it.each([
    ['Company', 'Company'],
    ['Individual', 'Individual'],
    ['', null],
  ])('choosing type "%s" puts %s in the URL at once and goes back to page 1', (chosen, expected) => {
    const { root, navigate } = setupWithRouter({ page: '2' });

    choose(typeSelect(root), chosen);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { type: expected, page: null } }));
  });

  it.each([
    ['true', 'true'],
    ['false', 'false'],
    ['', null],
  ])('choosing status "%s" puts %s in the URL at once and goes back to page 1', (chosen, expected) => {
    const { root, navigate } = setupWithRouter({ page: '2' });

    choose(statusSelect(root), chosen);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { active: expected, page: null } }));
  });

  it('offers Both, Active and Inactive for status and All, Company and Individual for type', () => {
    const { root } = setupWithRouter();

    expect(Array.from(statusSelect(root).options).map((o) => o.textContent?.trim())).toEqual(['Both', 'Active', 'Inactive']);
    expect(Array.from(typeSelect(root).options).map((o) => o.textContent?.trim())).toEqual(['All', 'Company', 'Individual']);
  });

  it('restores the controls and the request from the URL', () => {
    const { fixture, root, list } = setupWithRouter({ q: 'ac', type: 'Company', active: 'true', page: '2' });
    fixture.detectChanges();

    expect(searchBox(root).value).toBe('ac');
    expect(typeSelect(root).value).toBe('Company');
    expect(statusSelect(root).value).toBe('true');
    expect(list).toHaveBeenCalledWith({ q: 'ac', type: 'Company', active: true, page: 2, pageSize: 20 });
  });

  it('ignores unknown type and status values in the URL', () => {
    const { list } = setupWithRouter({ type: 'Partnership', active: 'maybe' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ type: undefined, active: undefined }));
  });

  it('keeps the typed search text on screen when nothing matches', () => {
    const { fixture, root } = setupWithRouter({ q: 'zzz' });
    fixture.detectChanges();

    expect(root.textContent).toContain('No customers match');
    expect(searchBox(root).value).toBe('zzz');
  });

  it('marks the search text and table cells as automatic direction for Arabic', () => {
    const { root } = setupWithRouter();

    expect(searchBox(root).getAttribute('dir')).toBe('auto');
  });

  it('gives the search box and both filters accessible labels', () => {
    const { root } = setupWithRouter();

    expect(searchBox(root).getAttribute('aria-label')).toBeTruthy();
    expect(typeSelect(root).getAttribute('aria-label')).toBeTruthy();
    expect(statusSelect(root).getAttribute('aria-label')).toBeTruthy();
  });
});
