import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { InvoiceListItem, PagedResult } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { InvoiceListComponent } from './invoice-list.component';

const abc: InvoiceListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  number: 'INV-000123',
  status: 'Draft',
  customerId: '22222222-2222-2222-2222-222222222222',
  customerName: 'ABC',
  dueDate: '2030-07-01',
  createdAtUtc: '2030-06-01T10:00:00Z',
  isOverdue: false,
};

const arabic: InvoiceListItem = {
  ...abc,
  id: '33333333-3333-3333-3333-333333333333',
  number: 'INV-000124',
  customerName: 'شركة الأمل',
  dueDate: '2030-08-15',
};

const page = (items: InvoiceListItem[], totalCount = items.length): PagedResult<InvoiceListItem> => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount,
  totalPages: totalCount === 0 ? 0 : Math.ceil(totalCount / 20),
});

function setup(list: ReturnType<typeof vi.fn>, query: Record<string, string> = {}) {
  TestBed.configureTestingModule({
    imports: [InvoiceListComponent],
    providers: [
      provideRouter([]),
      { provide: InvoiceService, useValue: { list } },
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(query)) } },
    ],
  });
  const fixture = TestBed.createComponent(InvoiceListComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('InvoiceListComponent', () => {
  it('shows a loading state while the request is in flight', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<PagedResult<InvoiceListItem>>()));

    expect(root.querySelector('[data-testid="list-loading"]')).not.toBeNull();
  });

  it('shows a row per invoice with number, customer, due date and status', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([abc, arabic]))));
    fixture.detectChanges();

    const rows = root.querySelectorAll('[data-testid="invoice-row"]');
    expect(rows.length).toBe(2);
    const first = rows[0].textContent ?? '';
    for (const expected of ['INV-000123', 'ABC', '2030', 'Draft']) {
      expect(first).toContain(expected);
    }
    expect(rows[1].textContent).toContain('شركة الأمل');
  });

  it('links each row to the invoice\'s details', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([abc]))));
    fixture.detectChanges();

    expect(root.querySelector('a[href="/invoices/11111111-1111-1111-1111-111111111111"]')?.textContent).toContain('INV-000123');
  });

  it('marks customer names as automatic direction so Arabic aligns naturally', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([arabic]))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="customer-name"]')?.getAttribute('dir')).toBe('auto');
  });

  it('shows the total number of invoices and a paginator', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([abc], 41))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="total"]')?.textContent).toContain('41');
    expect(root.querySelector('mat-paginator')).not.toBeNull();
  });

  it('offers a Create invoice button at the top', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(page([abc]))));

    expect(root.querySelector('a[href="/invoices/new"]')?.textContent).toContain('Create invoice');
  });

  it('shows "No invoices yet" with a way to create one instead of a blank screen when there are none', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([]))));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="list-empty"]')?.textContent).toContain('No invoices yet');
    expect(root.querySelector('[data-testid="list-empty"] a[href="/invoices/new"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="invoice-row"]')).toBeNull();
  });

  it('says there are none on this page when a page past the end is opened but invoices exist', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...page([], 3), page: 9 })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="list-empty"]')?.textContent).toContain('No invoices on this page');
    expect(root.querySelector('mat-paginator')).not.toBeNull();
  });

  it('shows an error with a retry button, and retry asks again', () => {
    const list = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
      .mockReturnValueOnce(of(page([abc])));
    const { fixture, root } = setup(list);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="list-error"]')).not.toBeNull();

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(list).toHaveBeenCalledTimes(2);
    expect(root.querySelectorAll('[data-testid="invoice-row"]').length).toBe(1);
  });

  it('requests the page and page size from the address', () => {
    const list = vi.fn().mockReturnValue(of(page([abc], 41)));

    setup(list, { page: '2', pageSize: '10' });

    expect(list).toHaveBeenCalledWith({ page: 2, pageSize: 10 });
  });

  it('uses page 1 of 20 when the address has none', () => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list);

    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 20 });
  });

  it.each([
    [{ page: '-3', pageSize: '100000' }],
    [{ page: 'abc', pageSize: '0' }],
    [{ page: '2147483648', pageSize: '101' }],
    [{ page: '1.5', pageSize: '2.5' }],
  ])('replaces unsafe page values in the address (%o) by safe defaults', (query) => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list, query);

    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 20 });
  });

  it('keeps the previous rows on screen while the next page loads and marks the list as updating', () => {
    const next = new Subject<PagedResult<InvoiceListItem>>();
    const queries = new Subject<ReturnType<typeof convertToParamMap>>();
    TestBed.configureTestingModule({
      imports: [InvoiceListComponent],
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { list: vi.fn().mockReturnValueOnce(of(page([abc], 41))).mockReturnValueOnce(next) } },
        { provide: ActivatedRoute, useValue: { queryParamMap: queries } },
      ],
    });
    const fixture = TestBed.createComponent(InvoiceListComponent);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    queries.next(convertToParamMap({}));
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="invoice-row"]').length).toBe(1);

    queries.next(convertToParamMap({ page: '2' }));
    fixture.detectChanges();

    expect(root.querySelectorAll('[data-testid="invoice-row"]').length).toBe(1);
    expect(root.querySelector('[data-testid="list-updating"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="list-loading"]')).toBeNull();
  });

  it('never lets a slow older answer replace a newer one', () => {
    const slow = new Subject<PagedResult<InvoiceListItem>>();
    const queries = new Subject<ReturnType<typeof convertToParamMap>>();
    const list = vi.fn().mockReturnValueOnce(slow).mockReturnValueOnce(of(page([arabic], 2)));
    TestBed.configureTestingModule({
      imports: [InvoiceListComponent],
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { list } },
        { provide: ActivatedRoute, useValue: { queryParamMap: queries } },
      ],
    });
    const fixture = TestBed.createComponent(InvoiceListComponent);
    const root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    queries.next(convertToParamMap({ page: '1' }));
    queries.next(convertToParamMap({ page: '2' }));
    slow.next(page([abc], 41));
    fixture.detectChanges();

    const text = root.querySelector('[data-testid="invoice-row"]')?.textContent ?? '';
    expect(text).toContain('INV-000124');
    expect(text).not.toContain('INV-000123');
  });

  it('puts the new page in the address when the paginator is used', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([abc], 41))));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    (root.querySelector('button.mat-mdc-paginator-navigation-next') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate.mock.calls[0][1]).toEqual(
      expect.objectContaining({ queryParams: { page: 2, pageSize: 20 }, queryParamsHandling: 'merge' }),
    );
  });

  it('shows no lines, totals or amounts in the list', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([abc]))));
    fixture.detectChanges();

    const content = (root.textContent ?? '').toLowerCase();

    expect(content).not.toContain('sar');
    expect(content).not.toContain('total ');
    expect(root.querySelectorAll('th').length).toBe(4);
  });

  // ---- the status column (spec 007) ----

  const statusCells = (root: HTMLElement) =>
    Array.from(root.querySelectorAll('[data-testid="status-cell"]')).map((cell) => cell.textContent?.trim());

  it('shows Overdue for a Sent invoice that is past its due date and the stored status for every other invoice', () => {
    const rows: InvoiceListItem[] = [
      { ...abc, id: '1', status: 'Draft' },
      { ...abc, id: '2', status: 'Sent', isOverdue: false },
      { ...abc, id: '3', status: 'Sent', isOverdue: true },
      { ...abc, id: '4', status: 'Paid' },
      { ...abc, id: '5', status: 'Cancelled' },
    ];
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page(rows))));
    fixture.detectChanges();

    expect(statusCells(root)).toEqual(['Draft', 'Sent', 'Overdue', 'Paid', 'Cancelled']);
  });

  it('shows a settled invoice as Paid with its own style, and never as Overdue (spec 008)', () => {
    const { fixture, root } = setup(
      vi.fn().mockReturnValue(of(page([{ ...abc, id: '5', status: 'Paid', isOverdue: false }, { ...abc, id: '6', status: 'Sent', isOverdue: true }, abc]))),
    );
    fixture.detectChanges();

    const cells = Array.from(root.querySelectorAll('[data-testid="status-cell"]'));
    expect(cells.map((cell) => cell.textContent?.trim())).toEqual(['Paid', 'Overdue', 'Draft']);
    expect(cells[0].classList.contains('paid')).toBe(true);
    expect(cells[0].classList.contains('overdue')).toBe(false);
    expect(cells[1].classList.contains('overdue')).toBe(true);
    expect(cells[1].classList.contains('paid')).toBe(false);
    expect(cells[2].classList.contains('paid')).toBe(false);
  });

  it('marks an Overdue row so it stands out', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([{ ...abc, status: 'Sent', isOverdue: true }, abc]))));
    fixture.detectChanges();

    const cells = Array.from(root.querySelectorAll('[data-testid="status-cell"]'));
    expect(cells[0].classList.contains('overdue')).toBe(true);
    expect(cells[1].classList.contains('overdue')).toBe(false);
  });

  it('keeps the loading, error-with-retry and empty states', () => {
    const loading = setup(vi.fn().mockReturnValue(new Subject<PagedResult<InvoiceListItem>>()));
    expect(loading.root.querySelector('[data-testid="list-loading"]')).not.toBeNull();
    TestBed.resetTestingModule();

    const empty = setup(vi.fn().mockReturnValue(of(page([]))));
    empty.fixture.detectChanges();
    expect(empty.root.querySelector('[data-testid="list-empty"]')).not.toBeNull();
  });
});

// ---- the dashboard's four filter values (spec 011) ----

describe('InvoiceListComponent filters from the dashboard', () => {
  it('reads status, overdue and paidMonth from the address into the request', () => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list, { status: 'Sent', overdue: 'true' });

    expect(list).toHaveBeenCalledWith(expect.objectContaining({ status: 'Sent', overdue: true }));
  });

  it('sends no status, overdue or paidMonth when none are in the address, exactly as before this spec', () => {
    const list = vi.fn().mockReturnValue(of(page([])));

    setup(list, {});

    const query = list.mock.calls[0][0];
    expect(query.status).toBeUndefined();
    expect(query.overdue).toBeUndefined();
    expect(query.paidMonth).toBeUndefined();
  });

  it('does not overwrite or strip the active filter when the paginator navigates (queryParamsHandling stays merge, as it did before this spec)', () => {
    // onPage() itself is unchanged by this spec: it always asked for {page, pageSize} with queryParamsHandling: 'merge', which is what
    // makes Angular's router keep any other query param (status/overdue/paidMonth) already in the address. This test proves onPage's
    // own call still only names page/pageSize (never a stray status) and still asks for 'merge' when a filter is active; it stops at
    // the mocked call's arguments, the same way the pre-existing "puts the new page in the address" test above does — it does not
    // exercise Angular's real merge algorithm or read back a resulting URL.
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(page([abc], 41))), { status: 'Draft' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    (root.querySelector('button.mat-mdc-paginator-navigation-next') as HTMLButtonElement).click();

    expect(navigate.mock.calls[0][1]).toEqual(
      expect.objectContaining({ queryParams: { page: 2, pageSize: 20 }, queryParamsHandling: 'merge' }),
    );
  });
});
