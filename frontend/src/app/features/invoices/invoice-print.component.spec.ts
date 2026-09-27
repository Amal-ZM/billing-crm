import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { InvoicePrintComponent } from './invoice-print.component';

const ID = '11111111-1111-1111-1111-111111111111';

/** A Draft of two lines, with notes. The numbers are deliberately not what a client would work out (see the content tests). */
const draft: Invoice = {
  id: ID,
  number: 'INV-000123',
  status: 'Draft',
  customer: { id: '22222222-2222-2222-2222-222222222222', name: 'ABC', isActive: true },
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
  totals: { totalNet: 575.75, discount: 0, netAfterDiscount: 575.75, totalVat: 30, grandTotal: 605.75 },
  lines: [
    {
      id: '33333333-3333-3333-3333-333333333333',
      position: 1,
      productId: '44444444-4444-4444-4444-444444444444',
      productName: 'Consulting hour',
      sku: 'CNS-001',
      unit: 'Hour',
      unitPrice: 100,
      vatPercent: 15,
      quantity: 2,
      net: 200,
      discountShare: 0,
      vat: 30,
    },
    {
      id: '55555555-5555-5555-5555-555555555555',
      position: 2,
      productId: '66666666-6666-6666-6666-666666666666',
      productName: 'ساعة استشارة',
      sku: 'AR-1',
      unit: 'Service',
      unitPrice: 250.5,
      vatPercent: 0,
      quantity: 1.5,
      net: 375.75,
      discountShare: 0,
      vat: 0,
    },
  ],
  paid: 0,
  remaining: 605.75,
  acceptsPayment: false,
  payments: [],
};

function setup(get: ReturnType<typeof vi.fn>) {
  TestBed.configureTestingModule({
    imports: [InvoicePrintComponent],
    providers: [
      provideRouter([]),
      // Only `get` exists: any other call (a send, a cancel, a payment, an update) would throw, so the page provably only reads.
      { provide: InvoiceService, useValue: { get } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ID }) }, paramMap: of(convertToParamMap({ id: ID })) } },
    ],
  });
  const fixture: ComponentFixture<InvoicePrintComponent> = TestBed.createComponent(InvoicePrintComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;

describe('InvoicePrintComponent states', () => {
  it('asks for the invoice once, by the id in the address', () => {
    const get = vi.fn().mockReturnValue(of(draft));

    setup(get);

    expect(get).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledWith(ID);
  });

  it('shows a loading state first and no Print button while it waits', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<Invoice>()));

    expect(has(root, 'detail-loading')).toBe(true);
    expect(has(root, 'print-button')).toBe(false);
  });

  it('says the invoice was not found for a 404, links back to the invoices and offers no Print button', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));

    expect(root.querySelector('h1')?.textContent).toContain('Invoice not found');
    expect(root.querySelector('[role="alert"]')).not.toBeNull();
    expect(root.querySelector('a[href="/invoices"]')).not.toBeNull();
    expect(has(root, 'print-button')).toBe(false);
    expect(has(root, 'number')).toBe(false);
  });

  it('shows an error with the API message and a Retry for any other failure, and offers no Print button', () => {
    const { root } = setup(
      vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500, error: { detail: 'The database is busy.' } }))),
    );

    const message = root.querySelector('[data-testid="detail-error"]');
    expect(message?.textContent).toContain('The invoice could not be loaded.');
    expect(message?.textContent).toContain('The database is busy.');
    expect(message?.getAttribute('role')).toBe('alert');
    expect(has(root, 'retry')).toBe(true);
    expect(has(root, 'print-button')).toBe(false);
  });

  it('says "Try again." when the API said nothing usable', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 }))));

    expect(root.querySelector('[data-testid="detail-error"]')?.textContent).toContain('Try again.');
  });

  it('loads again on Retry and then shows the invoice', () => {
    const get = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })))
      .mockReturnValueOnce(of(draft));
    const { fixture, root } = setup(get);

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(2);
    expect(has(root, 'detail-error')).toBe(false);
    expect(has(root, 'detail-loading')).toBe(false);
  });
});

// ---- the page itself (spec 009, US1) ----

/** The same invoice with a discount: the totals carry it and each line has its share. */
const discounted: Invoice = {
  ...draft,
  // Deliberately inconsistent (see the parity spec): the net after discount is not 575.75 - 20, the shares do not add up to the discount,
  // and the top-level discount differs from the totals'.
  discount: 25,
  totals: { totalNet: 575.75, discount: 20, netAfterDiscount: 555.7, totalVat: 27, grandTotal: 582.75 },
  lines: [
    { ...draft.lines[0], discountShare: 7.6, vat: 28.85 },
    { ...draft.lines[1], discountShare: 12.35 },
  ],
};

const sent: Invoice = { ...draft, status: 'Sent', sentAtUtc: '2030-06-02T09:00:00Z', allowedTransitions: ['Cancelled'], isEditable: false };
const cancelled: Invoice = {
  ...draft,
  status: 'Cancelled',
  cancelledAtUtc: '2030-06-03T08:00:00Z',
  allowedTransitions: [],
  isEditable: false,
};
const paid: Invoice = {
  ...sent,
  status: 'Paid',
  allowedTransitions: [],
  acceptsPayment: false,
  paid: 605.75,
  remaining: 0,
  payments: [{ id: 'p1', date: '2030-06-05', amount: 605.75, method: 'Bank', note: 'INTERNAL-PAYMENT-NOTE', recordedAtUtc: '2030-06-05T09:00:00Z' }],
};

const loaded = (invoice: Invoice) => setup(vi.fn().mockReturnValue(of(invoice)));
const text = (root: HTMLElement, testId: string) => (root.querySelector(`[data-testid="${testId}"]`)?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('InvoicePrintComponent content', () => {
  it('has the application title BillingCRM as its page heading and the invoice number and status under it', () => {
    const { root } = loaded(draft);

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('BillingCRM');
    expect(text(root, 'number')).toBe('INV-000123');
    expect(text(root, 'status-badge')).toBe('Draft');
  });

  it('shows the customer, the created date and the due date', () => {
    const { root } = loaded(draft);

    expect(text(root, 'customer')).toBe('ABC');
    expect(text(root, 'created-at')).toContain('2030');
    expect(text(root, 'due-date')).toBe('Jul 1, 2030');
  });

  it('shows no sent or cancelled date for a Draft, the sent date for a Sent and a Paid invoice, and the cancelled date for a Cancelled one', () => {
    const draftPage = loaded(draft).root;
    expect(has(draftPage, 'sent-at')).toBe(false);
    expect(has(draftPage, 'cancelled-at')).toBe(false);
    TestBed.resetTestingModule();

    const sentPage = loaded(sent).root;
    expect(has(sentPage, 'sent-at')).toBe(true);
    expect(has(sentPage, 'cancelled-at')).toBe(false);
    TestBed.resetTestingModule();

    expect(has(loaded(paid).root, 'sent-at')).toBe(true);
    TestBed.resetTestingModule();

    const cancelledPage = loaded(cancelled).root;
    expect(has(cancelledPage, 'cancelled-at')).toBe(true);
    expect(has(cancelledPage, 'sent-at')).toBe(false);
  });

  it('shows the notes with their line break and, when there are none, no notes at all and no Notes heading', () => {
    const withNotes = loaded(draft).root;
    expect(text(withNotes, 'notes')).toContain('Net 30 Thank you');
    expect(withNotes.querySelector('[data-testid="notes"]')?.textContent).toContain('\n');
    TestBed.resetTestingModule();

    const without = loaded({ ...draft, notes: null }).root;
    expect(has(without, 'notes')).toBe(false);
    expect(without.textContent).not.toContain('Notes');
  });

  it('shows every line with product, SKU, unit, unit price, VAT percent, quantity, net and VAT amount, in the order returned', () => {
    const { root } = loaded(draft);

    const rows = Array.from(root.querySelectorAll('[data-testid="line-row"]')).map((row) => row.textContent ?? '');
    expect(rows.length).toBe(2);
    for (const expected of ['Consulting hour', 'CNS-001', 'Hour', 'SAR 100.00', '15%', '2.00', 'SAR 200.00', 'SAR 30.00']) {
      expect(rows[0]).toContain(expected);
    }
    for (const expected of ['ساعة استشارة', 'AR-1', 'Service', 'SAR 250.50', '0%', '1.50', 'SAR 375.75', 'SAR 0.00']) {
      expect(rows[1]).toContain(expected);
    }
    expect(Array.from(root.querySelectorAll('th')).map((cell) => cell.textContent?.trim())).toEqual([
      'Product', 'SKU', 'Unit', 'Unit price', 'VAT', 'Quantity', 'Net', 'VAT amount',
    ]);
  });

  it('shows the totals without any discount rows when there is no discount', () => {
    const { root } = loaded(draft);

    expect(text(root, 'total-net')).toContain('SAR 575.75');
    expect(text(root, 'total-vat')).toContain('SAR 30.00');
    expect(text(root, 'grand-total')).toContain('SAR 605.75');
    expect(has(root, 'discount')).toBe(false);
    expect(has(root, 'net-after-discount')).toBe(false);
    expect(Array.from(root.querySelectorAll('th')).map((cell) => cell.textContent?.trim())).not.toContain('Discount');
  });

  it('shows the discount, the net after discount and each line\'s discount share when there is a discount', () => {
    const { root } = loaded(discounted);

    expect(text(root, 'discount')).toContain('SAR 20.00');
    expect(text(root, 'net-after-discount')).toContain('SAR 555.70');
    expect(text(root, 'grand-total')).toContain('SAR 582.75');
    expect(Array.from(root.querySelectorAll('th')).map((cell) => cell.textContent?.trim())).toEqual([
      'Product', 'SKU', 'Unit', 'Unit price', 'VAT', 'Quantity', 'Net', 'Discount', 'VAT amount',
    ]);
    const rows = Array.from(root.querySelectorAll('[data-testid="line-row"]')).map((row) => row.textContent ?? '');
    expect(rows[0]).toContain('SAR 7.60');
    expect(rows[1]).toContain('SAR 12.35');
  });

  it.each([
    [draft, 'Draft'],
    [sent, 'Sent'],
    [{ ...sent, isOverdue: true }, 'Overdue'],
    [paid, 'Paid'],
    [cancelled, 'Cancelled'],
  ] as [Invoice, string][])('shows the status word for each kind of invoice (%#)', (invoice, expected) => {
    expect(text(loaded(invoice).root, 'status-badge')).toBe(expected);
  });

  // ---- what is not on the page (FR-012) ----

  it('shows no payments, no paid or remaining, no ids and no other internal field, even for a Paid invoice with payments', () => {
    const { root } = loaded(paid);
    const all = root.textContent ?? '';

    expect(all).not.toContain('INTERNAL-PAYMENT-NOTE');
    expect(all).not.toContain('Remaining');
    expect(all).not.toContain('Payments');
    expect(all).not.toContain('Bank');
    expect(has(root, 'paid')).toBe(false);
    expect(has(root, 'remaining')).toBe(false);
    expect(all).not.toContain('Last changed');
    expect(all).not.toContain('Inactive');
    // The only identifier anywhere in the page (text or attribute) is the invoice's own id in the address of the way back.
    const identifiers = root.innerHTML.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi) ?? [];
    expect(identifiers).toEqual([ID]);
  });

  it('prints an inactive customer by name with no Inactive mark', () => {
    const { root } = loaded({ ...draft, customer: { ...draft.customer, isActive: false } });

    expect(text(root, 'customer')).toBe('ABC');
    expect(root.textContent).not.toContain('Inactive');
  });

  // ---- the on-screen controls ----

  it('has an on-screen bar, marked so it does not print, holding exactly the Print button and the way back', () => {
    const { root } = loaded(draft);

    const bars = root.querySelectorAll('.no-print');
    expect(bars.length).toBe(1);
    const bar = bars[0];
    expect(Array.from(bar.querySelectorAll('button, a')).map((element) => element.getAttribute('data-testid'))).toEqual(['print-button', 'back']);
    expect(bar.querySelector('[data-testid="print-button"]')?.textContent?.trim()).toBe('Print');
    expect(bar.querySelector('[data-testid="back"]')?.getAttribute('href')).toBe(`/invoices/${ID}`);
    expect(bar.querySelector('[data-testid="back"]')?.textContent).toContain('Back to invoice');
    // None of the document is inside the bar, and no other button or link is anywhere on the page.
    expect(bar.querySelector('h1, table, dl')).toBeNull();
    expect(root.querySelectorAll('button, a').length).toBe(2);
  });

  it('does not open the browser\'s print dialog by itself, not even after a slow load', () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    try {
      const pending = new Subject<Invoice>();
      const { fixture } = setup(vi.fn().mockReturnValue(pending));
      expect(print).not.toHaveBeenCalled();

      pending.next(draft);
      pending.complete();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[data-testid="print-button"]')).not.toBeNull();
      expect(print).not.toHaveBeenCalled();
    } finally {
      print.mockRestore();
    }
  });

  it('opens the browser\'s print dialog once for each press of the Print button', () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    try {
      const { fixture, root } = loaded(draft);
      const button = root.querySelector('[data-testid="print-button"]') as HTMLButtonElement;

      button.click();
      fixture.detectChanges();
      expect(print).toHaveBeenCalledTimes(1);

      button.click();
      expect(print).toHaveBeenCalledTimes(2);
    } finally {
      print.mockRestore();
    }
  });
});

// ---- the print stylesheet (spec 009, FR-008 and FR-009) ----

/** The text of every stylesheet this component put on the page. */
function printSheet(): string {
  return Array.from(document.head.querySelectorAll('style'))
    .map((style) => style.textContent ?? '')
    .filter((sheet) => sheet.includes('.invoice-print'))
    .join('\n');
}

/** Splits the stylesheet into what is inside `@media print { ... }` and everything else, by matching braces. */
function splitPrint(css: string): { inside: string; outside: string } {
  const start = css.indexOf('@media print');
  expect(start, 'the stylesheet has an @media print block').toBeGreaterThanOrEqual(0);
  const open = css.indexOf('{', start);
  let depth = 0;
  let end = open;
  for (let i = open; i < css.length; i++) {
    depth += css[i] === '{' ? 1 : css[i] === '}' ? -1 : 0;
    if (depth === 0) {
      end = i;
      break;
    }
  }
  return { inside: css.slice(open + 1, end), outside: css.slice(0, start) + css.slice(end + 1) };
}

describe('InvoicePrintComponent print stylesheet', () => {
  it('hides the on-screen bar in print and only in print', () => {
    loaded(draft);

    const { inside, outside } = splitPrint(printSheet());

    expect(inside).toMatch(/\.invoice-print \.no-print\s*\{[^}]*display:\s*none\s*!important/);
    // Never on screen: nothing outside the print block hides it.
    expect(outside).not.toMatch(/\.no-print[^{]*\{[^}]*display:\s*none/);
  });

  it('puts the pagination rules in the print block, on the elements they are meant for', () => {
    loaded(draft);

    const { inside } = splitPrint(printSheet());

    expect(inside).toMatch(/\.invoice-print thead\s*\{[^}]*display:\s*table-header-group/);
    expect(inside).toMatch(/\.invoice-print tr\s*\{[^}]*break-inside:\s*avoid/);
    expect(inside).toMatch(/\.invoice-print \.totals-block[^{]*\{[^}]*break-inside:\s*avoid/);
    expect(inside).toMatch(/\.invoice-print \.totals-block\s*\{[^}]*break-before:\s*avoid/);
  });

  it('prints black on white inside the print block, fills the page width and needs no background', () => {
    loaded(draft);

    const { inside } = splitPrint(printSheet());

    expect(inside).toMatch(/\.invoice-print\s*\{[^}]*color:\s*#000/);
    expect(inside).toMatch(/\.invoice-print\s*\{[^}]*background:\s*#fff/);
    expect(inside).toMatch(/\.invoice-print\s*\{[^}]*max-width:\s*100%/);
    expect(printSheet()).not.toMatch(/background-image|background-color/);
  });

  it('sets a page margin and leaves the paper size to the browser', () => {
    loaded(draft);

    const css = printSheet();

    expect(css).toMatch(/@page\s*\{[^}]*margin:\s*15mm/);
    expect(css).not.toMatch(/@page\s*\{[^}]*size/);
  });

  it('is scoped: every rule outside @page and @media is under .invoice-print', () => {
    loaded(draft);

    const selectors = printSheet()
      .replace(/@page\s*\{[^}]*\}/g, '')
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .match(/[^{}]+(?=\{)/g)!
      .map((selector) => selector.trim())
      .filter((selector) => selector.length > 0 && !selector.startsWith('@media'));

    expect(selectors.length).toBeGreaterThan(10);
    for (const selector of selectors) {
      for (const part of selector.split(',')) {
        expect(part.trim().startsWith('.invoice-print'), `"${part.trim()}" must be under .invoice-print`).toBe(true);
      }
    }
  });

  it('right-aligns and never wraps the amount cells, with a selector at least as specific as the cell rule that sets the default', () => {
    loaded(draft);

    const css = printSheet();

    // The default rule for every cell is "table.lines th, table.lines td { text-align: start }" (specificity 0,2,2): the amount rule must
    // not be a bare ".amount" (0,2,0), which would lose and leave the amounts left-aligned.
    expect(css).toMatch(/\.invoice-print table\.lines th\.amount,\s*\.invoice-print table\.lines td\.amount\s*\{[^}]*text-align:\s*end[^}]*white-space:\s*nowrap/);
    expect(css).not.toMatch(/\.invoice-print \.amount\s*\{/);
  });

  it('draws the status as written, in the same case as the details, not in capitals', () => {
    loaded(draft);

    expect(printSheet()).not.toMatch(/text-transform/);
  });

  it('keeps the notes\' line breaks and wraps long text inside its column', () => {
    loaded(draft);

    const css = printSheet();

    expect(css).toMatch(/\.invoice-print \.notes\s*\{[^}]*white-space:\s*pre-line/);
    expect(css).toMatch(/table\.lines td[^{]*\{[^}]*overflow-wrap:\s*anywhere/);
  });

  it('is removed with the page, so its page margin cannot affect another screen that is printed', () => {
    const { fixture } = loaded(draft);
    expect(printSheet()).toContain('@page');

    fixture.destroy();
    TestBed.resetTestingModule();

    expect(printSheet()).not.toContain('@page');
  });
});

// ---- the printout says what the details say, and nothing else (spec 009, US2) ----

describe('InvoicePrintComponent figures and text', () => {
  it('calculates nothing: figures that are not what a client would work out are shown exactly as the API gave them', () => {
    const odd: Invoice = {
      ...draft,
      totals: { totalNet: 111.11, discount: 0, netAfterDiscount: 111.11, totalVat: 22.22, grandTotal: 999.99 },
      lines: [{ ...draft.lines[0], unitPrice: 100, quantity: 2, vatPercent: 15, net: 199.99, vat: 7.77 }],
    };
    const { root } = loaded(odd);

    // Not 111.11 + 22.22 = 133.33, not 100 x 2 = 200.00, not 199.99 x 15% = 30.00.
    expect(text(root, 'grand-total')).toBe('SAR 999.99');
    expect(root.textContent).not.toContain('133.33');
    const row = root.querySelector('[data-testid="line-row"]')?.textContent ?? '';
    expect(row).toContain('SAR 199.99');
    expect(row).toContain('SAR 7.77');
    expect(row).not.toContain('SAR 200.00');
    expect(row).not.toContain('SAR 30.00');
  });

  it('shows the status the API implies and never works one out from the due date', () => {
    // A Sent invoice whose due date is long past but which the API does not call overdue reads Sent.
    const { root } = loaded({ ...sent, dueDate: '2001-01-01', isOverdue: false });

    expect(text(root, 'status-badge')).toBe('Sent');
  });

  it('keeps names and notes as written, reading in the right direction, and numbers, dates and SKUs left to right', () => {
    const { root } = loaded({ ...discounted, customer: { ...discounted.customer, name: 'شركة الأمل' }, notes: 'ملاحظة\nNote' });

    expect(text(root, 'customer')).toBe('شركة الأمل');
    expect(root.querySelector('[data-testid="customer"]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[data-testid="notes"]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[data-testid="notes"]')?.textContent).toContain('ملاحظة');
    expect(root.querySelector('[data-testid="number"]')?.getAttribute('dir')).toBe('ltr');
    for (const id of ['total-net', 'discount', 'net-after-discount', 'total-vat', 'grand-total']) {
      expect(root.querySelector(`[data-testid="${id}"]`)?.getAttribute('dir'), id).toBe('ltr');
    }
    const row = root.querySelectorAll('[data-testid="line-row"]')[1];
    const dirs = Array.from(row.querySelectorAll('td')).map((cell) => cell.getAttribute('dir'));
    // product name auto, SKU ltr, unit (plain word) none, then the six amount cells ltr.
    expect(dirs).toEqual(['auto', 'ltr', null, 'ltr', 'ltr', 'ltr', 'ltr', 'ltr', 'ltr']);
    expect(row.textContent).toContain('ساعة استشارة');
  });

  it('shows markup in a note or a name as text, never as markup', () => {
    const { root } = loaded({
      ...draft,
      notes: 'Pay <b>now</b> & <script>alert(1)</script>',
      lines: [{ ...draft.lines[0], productName: '<img src=x onerror=alert(1)> & co' }],
    });

    expect(root.querySelector('[data-testid="notes"]')?.textContent).toBe('Pay <b>now</b> & <script>alert(1)</script>');
    expect(root.querySelector('[data-testid="line-row"] td')?.textContent).toBe('<img src=x onerror=alert(1)> & co');
    expect(root.querySelector('[data-testid="notes"] b')).toBeNull();
    expect(root.querySelector('script')).toBeNull();
    expect(root.querySelector('img')).toBeNull();
  });

  it('carries long text in full and gives it a column that wraps it, and gives every amount a cell that never wraps', () => {
    const longNote = 'N'.repeat(300);
    const longName = 'P'.repeat(200);
    const { root } = loaded({ ...draft, notes: longNote, lines: [{ ...draft.lines[0], productName: longName, unitPrice: 9999999999999.99, net: 9999999999999.99 }] });

    expect(root.querySelector('[data-testid="notes"]')?.textContent).toBe(longNote);
    expect(root.querySelector('[data-testid="line-row"] td')?.textContent).toBe(longName);
    const row = root.querySelector('[data-testid="line-row"]') as Element;
    expect(row.textContent).toContain('SAR 9,999,999,999,999.99');
    const amountCells = Array.from(row.querySelectorAll('td.amount'));
    expect(amountCells.length).toBe(5); // unit price, VAT %, quantity, net, VAT amount (no discount column here)
    // (That they never wrap, and that long text does, is asserted on the stylesheet in the print stylesheet tests.)
  });

  it('prints an invoice of the largest allowed size: 100 lines, each on its own row', () => {
    const many: Invoice = { ...draft, lines: Array.from({ length: 100 }, (_, i) => ({ ...draft.lines[0], id: `line-${i}`, position: i + 1, productName: `Item ${i + 1}` })) };
    const { root } = loaded(many);

    const rows = Array.from(root.querySelectorAll('[data-testid="line-row"]'));
    expect(rows.length).toBe(100);
    expect(rows[0].textContent).toContain('Item 1');
    expect(rows[99].textContent).toContain('Item 100');
    // The heading row is a real header group, so the browser can repeat it on every page.
    expect(root.querySelector('table.lines thead tr')).not.toBeNull();
    expect(root.querySelectorAll('table.lines thead').length).toBe(1);
  });

  it('is one table with a heading group and a body group, and labelled sections for the notes and the totals', () => {
    const { root } = loaded(draft);

    expect(root.querySelectorAll('table').length).toBe(1);
    expect(root.querySelector('table')?.getAttribute('aria-label')).toBe('Invoice lines');
    expect(root.querySelector('section[aria-label="Totals"]')).not.toBeNull();
    expect(root.querySelector('section[aria-label="Notes"]')).not.toBeNull();
    expect(root.querySelectorAll('h1').length).toBe(1);
    expect(root.querySelectorAll('h2').length).toBe(1);
  });

  it('opens and prints without changing anything: the invoice is read once, and the stub offers nothing that writes', () => {
    const get = vi.fn().mockReturnValue(of(draft));
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    try {
      const { fixture, root } = setup(get);

      (root.querySelector('[data-testid="print-button"]') as HTMLButtonElement).click();
      fixture.detectChanges();

      // The stub offers only `get`, so any send, cancel, update or payment call would have thrown while the page was used.
      expect(get).toHaveBeenCalledTimes(1);
    } finally {
      print.mockRestore();
    }
  });

  it('writes nothing to storage and leaves the document title as the application\'s', () => {
    sessionStorage.clear();
    const titleBefore = document.title;

    loaded(draft);

    expect(sessionStorage.length).toBe(0);
    expect(document.title).toBe(titleBefore);
  });

  it('carries dates as left-to-right text, like the other figures', () => {
    const { root } = loaded({ ...sent, ...{ cancelledAtUtc: '2030-06-03T08:00:00Z' } });

    for (const id of ['created-at', 'due-date', 'sent-at', 'cancelled-at']) {
      expect(root.querySelector(`[data-testid="${id}"]`)?.getAttribute('dir'), id).toBe('ltr');
    }
  });

  it('labels the status for a screen reader and for the reader of the paper', () => {
    const { root } = loaded(draft);

    expect(root.querySelector('.status-line .status-label')?.textContent?.trim()).toBe('Status');
    expect(root.querySelector('.status-line [data-testid="status-badge"]')?.textContent?.trim()).toBe('Draft');
  });

  it('marks every control of the loading, not-found and error states as screen-only, so none prints', () => {
    const notFound = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })))).root;
    for (const control of Array.from(notFound.querySelectorAll('button, a'))) {
      expect(control.closest('.no-print'), control.textContent ?? '').not.toBeNull();
    }
    TestBed.resetTestingModule();

    const failed = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })))).root;
    expect(failed.querySelectorAll('button, a').length).toBe(2);
    for (const control of Array.from(failed.querySelectorAll('button, a'))) {
      expect(control.closest('.no-print'), control.textContent ?? '').not.toBeNull();
    }
  });

  it('follows the address: when it changes to another invoice the page reads that invoice, and a slow older answer cannot replace it', () => {
    const address = new BehaviorSubject(convertToParamMap({ id: ID }));
    const first = new Subject<Invoice>();
    const get = vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(of({ ...draft, id: 'other', number: 'INV-000999' }));
    TestBed.configureTestingModule({
      imports: [InvoicePrintComponent],
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { get } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: address.value }, paramMap: address } },
      ],
    });
    const fixture = TestBed.createComponent(InvoicePrintComponent);
    fixture.detectChanges();
    expect(get).toHaveBeenLastCalledWith(ID);

    address.next(convertToParamMap({ id: 'other' }));
    fixture.detectChanges();
    first.next(draft); // the older answer arrives late
    fixture.detectChanges();

    expect(get).toHaveBeenLastCalledWith('other');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="number"]')?.textContent).toBe('INV-000999');
  });
});

