import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { InvoiceDetailComponent } from './invoice-detail.component';
import { InvoicePrintComponent } from './invoice-print.component';

const ID = '11111111-1111-1111-1111-111111111111';

/**
 * The printable page must say what the details say (spec 009, FR-005 and SC-002). Nothing here compares a screen with a hand-written
 * string: both screens are rendered from the SAME invoice and their text is compared. So if the details' wording or format ever
 * changes, the print page must change with it or this fails; and a page that recalculated anything would show a different figure.
 *
 * The figures are deliberately not what a client would work out: the grand total is not the sum of the parts, a line's VAT is not
 * its net times its percent, and an amount is large enough to show thousands separators.
 */
const base: Invoice = {
  id: ID,
  number: 'INV-000123',
  status: 'Draft',
  customer: { id: '22222222-2222-2222-2222-222222222222', name: 'شركة الأمل ABC', isActive: true },
  dueDate: '2030-07-01',
  notes: 'Net 30\nThank you',
  createdAtUtc: '2030-06-01T10:00:00Z',
  updatedAtUtc: '2030-06-02T11:30:00Z',
  isOverdue: false,
  sentAtUtc: null,
  cancelledAtUtc: null,
  allowedTransitions: ['Sent', 'Cancelled'],
  isEditable: true,
  discount: 0,
  totals: { totalNet: 111.11, discount: 0, netAfterDiscount: 111.11, totalVat: 22.22, grandTotal: 999.99 },
  lines: [
    {
      id: '33333333-3333-3333-3333-333333333333',
      position: 1,
      productId: '44444444-4444-4444-4444-444444444444',
      productName: 'Consulting hour',
      sku: 'CNS-001',
      unit: 'Hour',
      unitPrice: 1234567.5,
      vatPercent: 15,
      quantity: 1.5,
      net: 1234567.5,
      discountShare: 0,
      vat: 7.77,
    },
    {
      id: '55555555-5555-5555-5555-555555555555',
      position: 2,
      productId: '66666666-6666-6666-6666-666666666666',
      productName: 'ساعة استشارة',
      sku: 'AR-1',
      unit: 'Service',
      unitPrice: 250.5,
      vatPercent: 7.5,
      quantity: 2,
      net: 3.33,
      discountShare: 0,
      vat: 0.1,
    },
  ],
  paid: 0,
  remaining: 999.99,
  acceptsPayment: false,
  payments: [],
};

// Deliberately inconsistent, so a page that worked anything out would show a different figure: the net after discount is not
// 111.11 - 33.33 (it is 77.79), the shares (20.01 + 13.30) are not the discount (33.33), and the top-level `discount` (40.00) is not
// `totals.discount` (33.33), so a page that read the wrong one would show 40.00.
const withDiscount = (invoice: Invoice): Invoice => ({
  ...invoice,
  discount: 40,
  totals: { totalNet: 111.11, discount: 33.33, netAfterDiscount: 77.79, totalVat: 22.22, grandTotal: 888.88 },
  lines: [{ ...invoice.lines[0], discountShare: 20.01 }, { ...invoice.lines[1], discountShare: 13.3 }],
});

const kinds: [string, string, Invoice][] = [
  ['Draft', 'Draft', base],
  ['Sent', 'Sent', { ...base, status: 'Sent', sentAtUtc: '2030-06-02T09:00:00Z', allowedTransitions: ['Cancelled'], isEditable: false }],
  ['late Sent', 'Overdue', { ...base, status: 'Sent', isOverdue: true, sentAtUtc: '2030-06-02T09:00:00Z', allowedTransitions: ['Cancelled'], isEditable: false }],
  ['Paid', 'Paid', { ...base, status: 'Paid', sentAtUtc: '2030-06-02T09:00:00Z', allowedTransitions: [], isEditable: false, paid: 999.99, remaining: 0 }],
  ['Cancelled', 'Cancelled', { ...base, status: 'Cancelled', cancelledAtUtc: '2030-06-03T08:00:00Z', allowedTransitions: [], isEditable: false }],
];

const cases: [string, string, Invoice][] = kinds.flatMap(([name, word, invoice]): [string, string, Invoice][] => [
  [`${name} invoice`, word, invoice],
  [`${name} invoice with a discount`, word, withDiscount(invoice)],
]);

/** Everything the two screens share by id, in one place. */
const SHARED_IDS = [
  'number',
  'customer',
  'due-date',
  'created-at',
  'sent-at',
  'cancelled-at',
  'status-badge',
  'total-net',
  'discount',
  'net-after-discount',
  'total-vat',
  'grand-total',
];

const norm = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ').trim();

function render(invoice: Invoice) {
  const get = vi.fn().mockReturnValue(of(invoice));
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [InvoiceDetailComponent, InvoicePrintComponent],
    providers: [
      provideRouter([]),
      { provide: InvoiceService, useValue: { get } },
      { provide: AuthService, useValue: { role: () => 'Admin' } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ID }) }, paramMap: of(convertToParamMap({ id: ID })) } },
    ],
  });
  const detail = TestBed.createComponent(InvoiceDetailComponent);
  detail.detectChanges();
  detail.detectChanges();
  const print = TestBed.createComponent(InvoicePrintComponent);
  print.detectChanges();
  print.detectChanges();
  return { detail: detail.nativeElement as HTMLElement, print: print.nativeElement as HTMLElement };
}

const one = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`);
const cells = (row: Element) => Array.from(row.querySelectorAll('td')).map((cell) => norm(cell.textContent));
const directions = (row: Element) => Array.from(row.querySelectorAll('td')).map((cell) => cell.getAttribute('dir'));

/** These must exist on every invoice: if one is renamed or dropped on BOTH screens, "equal presence" alone would still pass. */
const ALWAYS_PRESENT = ['number', 'customer', 'due-date', 'created-at', 'status-badge', 'total-net', 'total-vat', 'grand-total'];

/** Figures and names whose text direction is part of what the person reads (dates carry it on the printout only, so they are not compared). */
const DIRECTION_IDS = ['number', 'customer', 'total-net', 'discount', 'net-after-discount', 'total-vat', 'grand-total'];

describe('the printable invoice says what the details say', () => {
  it.each(cases)('%s: every shared figure, date and the status are identical on both screens', (_name, _word, invoice) => {
    const { detail, print } = render(invoice);

    for (const id of ALWAYS_PRESENT) {
      expect(one(detail, id), `${id} is missing from the details`).not.toBeNull();
      expect(one(print, id), `${id} is missing from the printable page`).not.toBeNull();
    }
    for (const id of SHARED_IDS) {
      const onDetail = one(detail, id);
      const onPrint = one(print, id);
      // An item is on both screens or on neither (no sent date on a Draft, no discount rows without a discount, ...).
      expect(onPrint === null, `${id} is present on one screen and missing on the other`).toBe(onDetail === null);
      if (onDetail && onPrint) {
        expect(norm(onPrint.textContent), id).toBe(norm(onDetail.textContent));
      }
    }
    // The discount rows are there exactly when the API's totals carry a discount (never from the top-level `discount`).
    expect(one(print, 'discount') !== null).toBe(invoice.totals.discount !== 0);
  });

  it.each(cases)('%s: names, numbers and amounts read in the same direction on both screens', (_name, _word, invoice) => {
    const { detail, print } = render(invoice);

    for (const id of DIRECTION_IDS) {
      const onDetail = one(detail, id);
      const onPrint = one(print, id);
      if (onDetail && onPrint) {
        expect(onPrint.getAttribute('dir'), id).toBe(onDetail.getAttribute('dir'));
      }
    }
    const detailRows = Array.from(detail.querySelectorAll('[data-testid="line-row"]'));
    Array.from(print.querySelectorAll('[data-testid="line-row"]')).forEach((row, index) =>
      expect(directions(row), `line ${index + 1}`).toEqual(directions(detailRows[index])),
    );
  });

  it.each(cases)('%s: every line shows the same cells in the same order', (_name, _word, invoice) => {
    const { detail, print } = render(invoice);

    const detailRows = Array.from(detail.querySelectorAll('[data-testid="line-row"]'));
    const printRows = Array.from(print.querySelectorAll('[data-testid="line-row"]'));
    expect(printRows.length).toBe(invoice.lines.length);
    expect(printRows.length).toBe(detailRows.length);
    printRows.forEach((row, index) => expect(cells(row), `line ${index + 1}`).toEqual(cells(detailRows[index])));
  });

  it.each(cases)('%s: the column headings are the same', (_name, _word, invoice) => {
    const { detail, print } = render(invoice);

    const headings = (root: HTMLElement) => Array.from(root.querySelectorAll('table th')).map((cell) => norm(cell.textContent));
    expect(headings(print)).toEqual(headings(detail));
  });

  it.each(cases)('%s: the notes are the same (or, with no notes, the printout has none while the details show a dash)', (_name, _word, invoice) => {
    const { detail, print } = render(invoice);

    // Compared as written, line breaks included (both screens keep them with "white-space: pre-line").
    expect(one(print, 'notes')?.textContent).toBe(one(detail, 'notes')?.textContent);
    expect(one(print, 'notes')?.textContent).toContain('\n');

    const without = render({ ...invoice, notes: null });
    expect(norm(one(without.detail, 'notes')?.textContent)).toBe('—');
    expect(one(without.print, 'notes')).toBeNull();
  });

  it.each(kinds)('the status word of a %s invoice is %s on both screens', (_name, word, invoice) => {
    const { detail, print } = render(invoice);

    expect(norm(one(detail, 'status-badge')?.textContent)).toBe(word);
    expect(norm(one(print, 'status-badge')?.textContent)).toBe(word);
  });

  it('shows the API\'s figures and none it could have worked out itself', () => {
    const { print } = render(base);

    // Not 111.11 + 22.22 = 133.33, and not 1,234,567.50 x 15% = 185,185.13: the page shows exactly what the API returned.
    expect(norm(one(print, 'grand-total')?.textContent)).toContain('999.99');
    expect(print.textContent).not.toContain('133.33');
    expect(print.textContent).not.toContain('185,185.13');
    expect(norm(one(print, 'total-net')?.textContent)).toBe('SAR 111.11');
    expect(cells(print.querySelector('[data-testid="line-row"]') as Element)).toContain('SAR 1,234,567.50');
  });

  it('with a discount, shows the totals block as the API gave it: not a sum of the shares, not net minus discount, not the top-level discount', () => {
    const { print } = render(withDiscount(base));

    expect(norm(one(print, 'discount')?.textContent)).toBe('SAR 33.33'); // not the top-level 40.00, not 20.01 + 13.30
    expect(norm(one(print, 'net-after-discount')?.textContent)).toBe('SAR 77.79'); // not 111.11 - 33.33 = 77.78
    expect(norm(one(print, 'grand-total')?.textContent)).toBe('SAR 888.88');
    expect(print.textContent).not.toContain('40.00');
    expect(print.textContent).not.toContain('77.78');
    const shares = Array.from(print.querySelectorAll('[data-testid="line-row"]')).map((row) => cells(row)[7]);
    expect(shares).toEqual(['SAR 20.01', 'SAR 13.30']);
  });

  it('a Sent invoice whose due date is past but which the API does not call overdue reads Sent on both screens', () => {
    const { detail, print } = render({ ...base, status: 'Sent', sentAtUtc: '2030-06-02T09:00:00Z', dueDate: '2001-01-01', isOverdue: false });

    expect(norm(one(detail, 'status-badge')?.textContent)).toBe('Sent');
    expect(norm(one(print, 'status-badge')?.textContent)).toBe('Sent');
  });
});
