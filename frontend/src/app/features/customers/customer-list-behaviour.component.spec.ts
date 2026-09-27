import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CustomerListItem, PagedResult } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { CustomerListComponent } from './customer-list.component';

const item = (name: string, isActive = true): CustomerListItem => ({
  id: `id-${name}`,
  name,
  type: 'Company',
  email: `${name.toLowerCase()}@x.test`,
  phone: '0500000000',
  vatNumber: null,
  isActive,
});

const page = (items: CustomerListItem[], totalCount = items.length, pageNumber = 1): PagedResult<CustomerListItem> => ({
  items,
  page: pageNumber,
  pageSize: 20,
  totalCount,
  totalPages: totalCount === 0 ? 0 : Math.ceil(totalCount / 20),
});

/** The list with a URL the test can change, and one controllable response per request. */
function setup(initial: Record<string, string> = {}) {
  const params = new BehaviorSubject<ParamMap>(convertToParamMap(initial));
  const responses: Subject<PagedResult<CustomerListItem>>[] = [];
  const list = vi.fn().mockImplementation(() => {
    const response = new Subject<PagedResult<CustomerListItem>>();
    responses.push(response);
    return response;
  });
  TestBed.configureTestingModule({
    imports: [CustomerListComponent],
    providers: [
      provideRouter([]),
      { provide: CustomerService, useValue: { list } },
      { provide: ActivatedRoute, useValue: { queryParamMap: params } },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(CustomerListComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(root.querySelectorAll('[data-testid="customer-row"]')).map((r) => r.textContent ?? '');
  const searchBox = () => root.querySelector('input[data-testid="search"]') as HTMLInputElement;
  const url = (values: Record<string, string>) => {
    params.next(convertToParamMap(values));
    fixture.detectChanges();
  };
  const answer = (index: number, result: PagedResult<CustomerListItem>) => {
    responses[index].next(result);
    responses[index].complete();
    fixture.detectChanges();
  };
  return { fixture, root, list, navigate, rows, searchBox, url, answer, responses };
}

describe('CustomerListComponent behaviour', () => {
  afterEach(() => vi.useRealTimers());

  it('a slow older response never replaces a newer one', () => {
    const { rows, url, answer, list } = setup({ q: 'a' });

    url({ q: 'ab' }); // a second, newer request starts while the first is still pending
    expect(list).toHaveBeenCalledTimes(2);
    answer(1, page([item('Newer')])); // the newer answer arrives first
    answer(0, page([item('Older')])); // then the stale one

    expect(rows().length).toBe(1);
    expect(rows()[0]).toContain('Newer');
    expect(rows()[0]).not.toContain('Older');
  });

  it('a slow older error never replaces a newer success', () => {
    const { rows, url, answer, responses, root, fixture } = setup({ q: 'a' });

    url({ q: 'ab' });
    answer(1, page([item('Newer')]));
    responses[0].error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(rows()[0]).toContain('Newer');
    expect(root.querySelector('[data-testid="list-error"]')).toBeNull();
  });

  it('keeps the previous rows on screen, marked busy, while the next page loads', () => {
    const { root, rows, url, answer } = setup({ page: '1' });
    answer(0, page([item('First')], 41));

    url({ page: '2' });

    expect(rows()[0]).toContain('First');
    expect(root.querySelector('table')?.getAttribute('aria-busy')).toBe('true');
    expect(root.querySelector('[data-testid="list-updating"]')).not.toBeNull();
    expect(root.querySelector('mat-paginator')).not.toBeNull();

    answer(1, page([item('Second')], 41, 2));
    expect(rows()[0]).toContain('Second');
    expect(root.querySelector('table')?.getAttribute('aria-busy')).toBe('false');
  });

  it('keeps the paginator (and so keyboard focus) when the URL changes', () => {
    const { root, url, answer } = setup();
    answer(0, page([item('A')], 41));
    const paginatorBefore = root.querySelector('mat-paginator');

    url({ page: '2' });

    expect(root.querySelector('mat-paginator')).toBe(paginatorBefore);
  });

  it('asks for the next page when the paginator Next button is used', () => {
    const { root, fixture, answer, navigate } = setup();
    answer(0, page([item('A')], 41));

    (root.querySelector('button.mat-mdc-paginator-navigation-next') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(navigate.mock.calls[0][1]).toEqual(
      expect.objectContaining({ queryParams: { page: 2, pageSize: 20 }, queryParamsHandling: 'merge' }),
    );
  });

  it('overwrites the search box when the URL changes by itself, as with the Back button', () => {
    const { searchBox, url } = setup({ q: 'first' });
    expect(searchBox().value).toBe('first');

    url({ q: 'second' });

    expect(searchBox().value).toBe('second');
  });

  it('does not overwrite what the person is typing when the URL catches up with their own search', () => {
    vi.useFakeTimers();
    const { searchBox, url, fixture, navigate } = setup();

    searchBox().value = 'ac';
    searchBox().dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(300); // debounced navigation for "ac"
    expect(navigate).toHaveBeenCalledTimes(1);

    searchBox().value = 'acm'; // they keep typing before the URL catches up
    searchBox().dispatchEvent(new Event('input'));
    url({ q: 'ac' }); // the router commits the URL for "ac"
    fixture.detectChanges();

    expect(searchBox().value).toBe('acm');
  });

  it('puts debounced search text in the URL by replacing the history entry, but filter changes add one', () => {
    vi.useFakeTimers();
    const { searchBox, root, navigate } = setup();

    searchBox().value = 'ac';
    searchBox().dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(300);
    const type = root.querySelector('select[data-testid="type-filter"]') as HTMLSelectElement;
    type.value = 'Company';
    type.dispatchEvent(new Event('change'));

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ replaceUrl: true }));
    expect(navigate.mock.calls[1][1]).toEqual(expect.objectContaining({ replaceUrl: false }));
  });

  it('treats spaces-only typing as no search', () => {
    vi.useFakeTimers();
    const { searchBox, navigate } = setup({ q: 'acme' });

    searchBox().value = '   ';
    searchBox().dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(300);

    expect(navigate.mock.calls[0][1]).toEqual(expect.objectContaining({ queryParams: { q: null, page: null } }));
  });

  it.each([
    [{ pageSize: '500' }, { page: 1, pageSize: 20 }],
    [{ pageSize: '0' }, { page: 1, pageSize: 20 }],
    [{ pageSize: '100' }, { page: 1, pageSize: 100 }],
    [{ page: 'abc' }, { page: 1, pageSize: 20 }],
    [{ page: '0' }, { page: 1, pageSize: 20 }],
    [{ page: '-3' }, { page: 1, pageSize: 20 }],
    [{ page: '1e21' }, { page: 1, pageSize: 20 }],
    [{ page: '2000000' }, { page: 1, pageSize: 20 }],
    [{ page: '7', pageSize: '50' }, { page: 7, pageSize: 50 }],
  ])('turns the URL values %j into the safe request %j', (query, expected) => {
    const { list } = setup(query);

    expect(list).toHaveBeenCalledWith(expect.objectContaining(expected));
  });

  it('says "1 customer" for one and "N customers" for more', () => {
    const { root, answer, url } = setup();
    answer(0, page([item('Only')], 1));
    expect(root.querySelector('[data-testid="total"]')?.textContent).toMatch(/1 customer(?!s)/);

    url({ page: '2' });
    answer(1, page([item('A'), item('B')], 2));
    expect(root.querySelector('[data-testid="total"]')?.textContent).toContain('2 customers');
  });

  it('says a page past the end has no customers, while keeping the paginator so the person can go back', () => {
    const { root, answer } = setup({ page: '9' });

    answer(0, page([], 5, 9));

    expect(root.querySelector('[data-testid="list-empty"]')?.textContent).toContain('No customers on this page.');
    expect(root.querySelector('[data-testid="list-empty"]')?.getAttribute('role')).toBe('status');
    expect(root.querySelector('mat-paginator')).not.toBeNull();
  });

  it('says nothing matches when the whole result is empty', () => {
    const { root, answer } = setup({ q: 'zzz' });

    answer(0, page([], 0));

    expect(root.querySelector('[data-testid="list-empty"]')?.textContent).toContain('No customers match.');
  });

  it('shows what the API said when the list cannot be loaded', () => {
    const { root, fixture, responses } = setup();

    responses[0].error(
      new HttpErrorResponse({ status: 400, error: { errors: { pageSize: ['Page size must be a whole number from 1 to 100.'] } } }),
    );
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="list-error"]')?.textContent).toContain('Page size must be a whole number from 1 to 100.');
  });

  it('retries the current request from the error state', () => {
    const { root, fixture, responses, list, answer } = setup({ q: 'x' });
    responses[0].error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(list).toHaveBeenCalledTimes(2);
    expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ q: 'x' }));
    answer(1, page([item('Back')]));
    expect(root.querySelectorAll('[data-testid="customer-row"]').length).toBe(1);
  });

  it('gives the table an accessible name', () => {
    const { root, answer } = setup();
    answer(0, page([item('A')]));

    expect(root.querySelector('table')?.getAttribute('aria-label')).toBe('Customers');
  });

  it('uses a plain error fallback when the failure carries no detail', () => {
    const list = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    TestBed.configureTestingModule({
      imports: [CustomerListComponent],
      providers: [
        provideRouter([]),
        { provide: CustomerService, useValue: { list } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } },
      ],
    });
    const fixture = TestBed.createComponent(CustomerListComponent);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="list-error"]')?.textContent).toContain(
      'The customers could not be loaded.',
    );
  });
});
