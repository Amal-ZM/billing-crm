import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { InvoiceDetailComponent } from './invoice-detail.component';

const draft: Invoice = {
  id: '11111111-1111-1111-1111-111111111111',
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
  paid: 0,
  remaining: 605.75,
  acceptsPayment: false,
  payments: [],
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
};

/** The same invoice after it was sent: read-only, and only Cancel is left in what its status permits. */
const sent: Invoice = {
  ...draft,
  status: 'Sent',
  sentAtUtc: '2030-06-02T09:00:00Z',
  allowedTransitions: ['Cancelled'],
  isEditable: false,
};

interface SetupOptions {
  role?: 'Admin' | 'Agent';
  send?: ReturnType<typeof vi.fn>;
  cancel?: ReturnType<typeof vi.fn>;
}

function setup(get: ReturnType<typeof vi.fn>, options: SetupOptions = {}) {
  TestBed.configureTestingModule({
    imports: [InvoiceDetailComponent],
    providers: [
      provideRouter([]),
      { provide: InvoiceService, useValue: { get, send: options.send ?? vi.fn(), cancel: options.cancel ?? vi.fn() } },
      { provide: AuthService, useValue: { role: () => options.role ?? 'Admin' } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: draft.id }) } } },
    ],
  });
  const fixture = TestBed.createComponent(InvoiceDetailComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

describe('InvoiceDetailComponent', () => {
  it('shows a loading state first', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<Invoice>()));

    expect(root.querySelector('[data-testid="detail-loading"]')).not.toBeNull();
  });

  it('shows the number, the Draft badge, the customer, the due date, the notes and the created and changed times', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="number"]')?.textContent).toContain('INV-000123');
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Draft');
    expect(root.querySelector('[data-testid="customer"]')?.textContent).toContain('ABC');
    expect(root.querySelector('[data-testid="due-date"]')?.textContent).toContain('2030');
    expect(root.querySelector('[data-testid="notes"]')?.textContent).toContain('Net 30');
    expect(root.querySelector('[data-testid="notes"]')?.textContent).toContain('Thank you');
    expect(root.querySelector('[data-testid="created-at"]')?.textContent?.trim()).not.toBe('');
    expect(root.querySelector('[data-testid="updated-at"]')?.textContent?.trim()).not.toBe('');
  });

  it('shows every line as saved: name, SKU, unit, unit price in SAR, VAT percent and quantity, in order', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)));
    fixture.detectChanges();

    const rows = Array.from(root.querySelectorAll('[data-testid="line-row"]'));
    expect(rows.length).toBe(2);
    const first = rows[0].textContent ?? '';
    for (const expected of ['Consulting hour', 'CNS-001', 'Hour', 'SAR', '100.00', '15%', '2.00']) {
      expect(first).toContain(expected);
    }
    const second = rows[1].textContent ?? '';
    for (const expected of ['ساعة استشارة', 'AR-1', 'Service', '250.50', '0%', '1.50']) {
      expect(second).toContain(expected);
    }
  });

  it('shows the totals exactly as the API returned them, in SAR with two decimals, and calculates nothing itself', () => {
    // Numbers that are NOT what a client would get by adding up (grand total 999.99, not 111.11 + 22.22): the screen must show what it is given.
    const given: Invoice = {
      ...draft,
      totals: { totalNet: 111.11, discount: 0, netAfterDiscount: 111.11, totalVat: 22.22, grandTotal: 999.99 },
    };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(given)));
    fixture.detectChanges();

    const totals = root.querySelector('[data-testid="totals"]');
    expect(totals?.querySelector('[data-testid="total-net"]')?.textContent).toContain('SAR 111.11');
    expect(totals?.querySelector('[data-testid="total-vat"]')?.textContent).toContain('SAR 22.22');
    expect(totals?.querySelector('[data-testid="grand-total"]')?.textContent).toContain('SAR 999.99');
    expect(totals?.textContent).not.toContain('133.33');
  });

  it('shows the acceptance amounts: line net 200.00 and VAT 30.00, total net 200.00, total VAT 30.00, grand total 230.00', () => {
    const accepted: Invoice = {
      ...draft,
      lines: [draft.lines[0]],
      totals: { totalNet: 200, discount: 0, netAfterDiscount: 200, totalVat: 30, grandTotal: 230 },
    };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(accepted)));
    fixture.detectChanges();

    const row = root.querySelector('[data-testid="line-row"]')?.textContent ?? '';
    expect(row).toContain('200.00');
    expect(row).toContain('30.00');
    expect(root.querySelector('[data-testid="total-net"]')?.textContent).toContain('200.00');
    expect(root.querySelector('[data-testid="total-vat"]')?.textContent).toContain('30.00');
    expect(root.querySelector('[data-testid="grand-total"]')?.textContent).toContain('230.00');
  });

  it('shows each line\'s net and VAT amount beside its saved price and percent', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)));
    fixture.detectChanges();

    const rows = Array.from(root.querySelectorAll('[data-testid="line-row"]'));
    expect(rows[0].textContent).toContain('200.00');
    expect(rows[0].textContent).toContain('30.00');
    expect(rows[1].textContent).toContain('375.75');
    expect(Array.from(root.querySelectorAll('th')).map((cell) => cell.textContent?.trim())).toEqual([
      'Product', 'SKU', 'Unit', 'Unit price', 'VAT', 'Quantity', 'Net', 'VAT amount',
    ]);
  });

  it('shows each line\'s net and VAT in their own columns exactly as given, even when they differ from what the raw fields would give', () => {
    // Line 1 is honest (1.25 x 10.55 = 13.19, VAT 1.98). Line 2 is deliberately NOT quantity x price: a component that calculated would show 200.00 and 30.00.
    const given: Invoice = {
      ...draft,
      lines: [
        { ...draft.lines[0], quantity: 1.25, unitPrice: 10.55, vatPercent: 15, net: 13.19, discountShare: 0, vat: 1.98 },
        { ...draft.lines[1], quantity: 2, unitPrice: 100, vatPercent: 15, net: 7.77, discountShare: 0, vat: 3.33 },
      ],
    };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(given)));
    fixture.detectChanges();

    const cells = (row: number) => Array.from(root.querySelectorAll('[data-testid="line-row"]')[row].querySelectorAll('td')).map((td) => td.textContent?.trim());
    expect(cells(0)[6]).toBe('SAR 13.19');
    expect(cells(0)[7]).toBe('SAR 1.98');
    expect(cells(1)[6]).toBe('SAR 7.77');
    expect(cells(1)[7]).toBe('SAR 3.33');
    expect(root.querySelectorAll('[data-testid="line-row"]')[1].textContent).not.toContain('200.00');
    expect(root.querySelectorAll('[data-testid="line-row"]')[1].textContent).not.toContain('30.00');
  });

  it('shows the discount share in its own column when there is a discount, even when the shares are not what a client would split', () => {
    // Nets 100 and 50 with a 30 discount would split 20 / 10; the API says 15 / 15, and the screen must show that.
    const given: Invoice = {
      ...draft,
      discount: 30,
      totals: { totalNet: 150, discount: 30, netAfterDiscount: 120, totalVat: 12, grandTotal: 132 },
      lines: [
        { ...draft.lines[0], net: 100, discountShare: 15, vat: 12.75 },
        { ...draft.lines[1], net: 50, discountShare: 15, vat: 0 },
      ],
    };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(given)));
    fixture.detectChanges();

    const cells = (row: number) => Array.from(root.querySelectorAll('[data-testid="line-row"]')[row].querySelectorAll('td')).map((td) => td.textContent?.trim());
    expect(cells(0).slice(6)).toEqual(['SAR 100.00', 'SAR 15.00', 'SAR 12.75']);
    expect(cells(1).slice(6)).toEqual(['SAR 50.00', 'SAR 15.00', 'SAR 0.00']);
  });

  it('gates the discount rows on the discount it shows (the totals), so what is hidden and what is shown always agree', () => {
    const given: Invoice = { ...draft, discount: 0, totals: { ...draft.totals, discount: 20, netAfterDiscount: 555.75 } };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(given)));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="discount"]')?.textContent).toContain('SAR 20.00');
  });

  it('shows no discount rows and no discount column when the invoice has no discount', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="discount"]')).toBeNull();
    expect(root.querySelector('[data-testid="net-after-discount"]')).toBeNull();
    expect(root.querySelectorAll('th').length).toBe(8);
  });

  it('shows the discount, the net after it and each line\'s share of it when the invoice has a discount', () => {
    const discounted: Invoice = {
      ...draft,
      discount: 20,
      totals: { totalNet: 200, discount: 20, netAfterDiscount: 180, totalVat: 27, grandTotal: 207 },
      lines: [{ ...draft.lines[0], discountShare: 20, vat: 27 }],
    };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(discounted)));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="discount"]')?.textContent).toContain('SAR 20.00');
    expect(root.querySelector('[data-testid="net-after-discount"]')?.textContent).toContain('SAR 180.00');
    expect(root.querySelector('[data-testid="total-vat"]')?.textContent).toContain('SAR 27.00');
    expect(root.querySelector('[data-testid="grand-total"]')?.textContent).toContain('SAR 207.00');
    expect(Array.from(root.querySelectorAll('th')).map((cell) => cell.textContent?.trim())).toContain('Discount');
    expect(root.querySelector('[data-testid="line-row"]')?.textContent).toContain('20.00');
  });

  it('shows no amounts while loading, when the invoice is not found, or when it could not be loaded', () => {
    const loading = setup(vi.fn().mockReturnValue(new Subject<Invoice>()));
    expect(loading.root.querySelector('[data-testid="totals"]')).toBeNull();
    expect(loading.root.textContent).not.toContain('SAR');
    TestBed.resetTestingModule();

    const missing = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));
    missing.fixture.detectChanges();
    expect(missing.root.querySelector('[data-testid="totals"]')).toBeNull();
    expect(missing.root.textContent).not.toContain('SAR');
    expect(missing.root.querySelector('table')).toBeNull();
    TestBed.resetTestingModule();

    const failed = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));
    failed.fixture.detectChanges();
    expect(failed.root.querySelector('[data-testid="totals"]')).toBeNull();
    expect(failed.root.textContent).not.toContain('SAR');
    expect(failed.root.querySelector('table')).toBeNull();
  });

  it('shows "No notes" when there are none', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...draft, notes: null })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="notes"]')?.textContent?.trim()).toBe('—');
  });

  it('marks an inactive customer with Inactive, and shows an active one without the mark', () => {
    const inactive = setup(vi.fn().mockReturnValue(of({ ...draft, customer: { ...draft.customer, isActive: false } })));
    inactive.fixture.detectChanges();
    expect(inactive.root.querySelector('[data-testid="customer-inactive"]')?.textContent).toContain('Inactive');

    TestBed.resetTestingModule();
    const active = setup(vi.fn().mockReturnValue(of(draft)));
    active.fixture.detectChanges();
    expect(active.root.querySelector('[data-testid="customer-inactive"]')).toBeNull();
  });

  it('marks text as automatic direction so Arabic names align naturally', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...draft, customer: { ...draft.customer, name: 'شركة الأمل' } })));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="customer"]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[data-testid="customer"]')?.textContent).toContain('شركة الأمل');
  });

  it('links to the edit page for a Draft', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)));
    fixture.detectChanges();

    expect(root.querySelector(`a[href="/invoices/${draft.id}/edit"]`)?.textContent).toContain('Edit');
  });

  it('says "Invoice not found" for an unknown invoice', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));
    fixture.detectChanges();

    expect(root.textContent).toContain('Invoice not found');
    expect(root.querySelector('[data-testid="status-badge"]')).toBeNull();
  });

  it('shows an error state with retry for other failures', () => {
    const get = vi.fn().mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 }))).mockReturnValueOnce(of(draft));
    const { fixture, root } = setup(get);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="detail-error"]')).not.toBeNull();

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[data-testid="number"]')?.textContent).toContain('INV-000123');
  });

  // ---- send (spec 007) ----

  const click = (fixture: ComponentFixture<InvoiceDetailComponent>, root: HTMLElement, testId: string) => {
    (root.querySelector(`[data-testid="${testId}"]`) as HTMLElement).click();
    fixture.detectChanges();
  };
  const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;

  it('offers Send and Edit for a Draft, and no Cancel to an Agent', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { role: 'Agent' });
    fixture.detectChanges();

    expect(has(root, 'send')).toBe(true);
    expect(has(root, 'edit')).toBe(true);
    expect(has(root, 'cancel')).toBe(false);
  });

  it('asks before sending and calls nothing until "Yes, send"; "Keep it" closes the question', () => {
    const send = vi.fn().mockReturnValue(of(sent));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    expect(root.querySelector('[data-testid="confirm"]')?.textContent).toContain('Send this invoice?');
    expect(send).not.toHaveBeenCalled();

    click(fixture, root, 'confirm-keep');
    expect(has(root, 'confirm')).toBe(false);
    expect(send).not.toHaveBeenCalled();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith(draft.id);
  });

  it('shows the invoice as Sent with the time it was sent, and no Send or Edit, once it is sent', () => {
    const send = vi.fn().mockReturnValue(of(sent));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Sent');
    expect(root.querySelector('[data-testid="sent-at"]')?.textContent?.trim()).not.toBe('');
    expect(has(root, 'send')).toBe(false);
    expect(has(root, 'edit')).toBe(false);
    expect(root.querySelector('[data-testid="grand-total"]')?.textContent).toContain('605.75');
  });

  it('disables the actions while sending and ignores a second click', () => {
    const pending = new Subject<Invoice>();
    const send = vi.fn().mockReturnValue(pending);
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');
    click(fixture, root, 'confirm-yes');

    expect(send).toHaveBeenCalledTimes(1);
    expect((root.querySelector('[data-testid="send"]') as HTMLButtonElement).disabled).toBe(true);
    pending.next(sent);
    pending.complete();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Sent');
  });

  it('shows the API\'s message for a refused send and reloads the invoice so the actions match reality', () => {
    const send = vi
      .fn()
      .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { title: 'Illegal status change', detail: 'An invoice that is Sent cannot be sent.' } })));
    const get = vi.fn().mockReturnValueOnce(of(draft)).mockReturnValueOnce(of(sent));
    const { fixture, root } = setup(get, { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="action-error"]')?.textContent).toContain('An invoice that is Sent cannot be sent.');
    expect(get).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Sent');
    expect(has(root, 'send')).toBe(false);
  });

  it('shows a refused past due date with a way to fix it: the Edit link stays', () => {
    const send = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { dueDate: ['Due date is in the past. Change the due date before sending.'] } },
          }),
      ),
    );
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="action-error"]')?.textContent).toContain('Due date is in the past.');
    expect(has(root, 'edit')).toBe(true);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Draft');
  });

  it('says the invoice was not found when the send comes back 404', () => {
    const send = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const get = vi.fn().mockReturnValueOnce(of(draft)).mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 404 })));
    const { fixture, root } = setup(get, { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');

    expect(root.textContent).toContain('Invoice not found');
  });

  it('a network failure while sending shows a generic message and keeps the invoice', () => {
    const send = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { send });
    fixture.detectChanges();

    click(fixture, root, 'send');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="action-error"]')?.textContent?.trim()).not.toBe('');
    expect(root.querySelector('[data-testid="number"]')?.textContent).toContain('INV-000123');
  });

  it('a Sent invoice offers no Send and no Edit, whatever the role', () => {
    for (const role of ['Admin', 'Agent'] as const) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(sent)), { role });
      fixture.detectChanges();

      expect(has(root, 'send')).toBe(false);
      expect(has(root, 'edit')).toBe(false);
      TestBed.resetTestingModule();
    }
  });

  // ---- cancel (spec 007: Admin only) ----

  const cancelled: Invoice = {
    ...draft,
    status: 'Cancelled',
    cancelledAtUtc: '2030-06-03T08:00:00Z',
    allowedTransitions: [],
    isEditable: false,
  };
  const overdue: Invoice = { ...sent, isOverdue: true };

  it('offers Cancel to an Admin for a Draft and for a Sent invoice', () => {
    for (const invoice of [draft, sent]) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)), { role: 'Admin' });
      fixture.detectChanges();

      expect(has(root, 'cancel')).toBe(true);
      TestBed.resetTestingModule();
    }
  });

  it('offers Cancel and no Send for an Overdue invoice, which is still a Sent invoice', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(overdue)), { role: 'Admin' });
    fixture.detectChanges();

    expect(has(root, 'cancel')).toBe(true);
    expect(has(root, 'send')).toBe(false);
    expect(has(root, 'edit')).toBe(false);
  });

  it('never offers Cancel to an Agent, for any invoice', () => {
    for (const invoice of [draft, sent, overdue]) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)), { role: 'Agent' });
      fixture.detectChanges();

      expect(has(root, 'cancel')).toBe(false);
      TestBed.resetTestingModule();
    }
  });

  it('offers no Cancel, even to an Admin, for a Paid or a Cancelled invoice', () => {
    for (const invoice of [cancelled, { ...cancelled, status: 'Paid' as const, cancelledAtUtc: null }]) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)), { role: 'Admin' });
      fixture.detectChanges();

      expect(has(root, 'cancel')).toBe(false);
      expect(has(root, 'send')).toBe(false);
      expect(has(root, 'edit')).toBe(false);
      TestBed.resetTestingModule();
    }
  });

  it('asks before cancelling and calls nothing until "Yes, cancel"', () => {
    const cancel = vi.fn().mockReturnValue(of(cancelled));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { cancel, role: 'Admin' });
    fixture.detectChanges();

    click(fixture, root, 'cancel');
    expect(root.querySelector('[data-testid="confirm"]')?.textContent).toContain('Cancel this invoice?');
    expect(root.querySelector('[data-testid="confirm"]')?.textContent).toContain('cannot be undone');
    expect(cancel).not.toHaveBeenCalled();

    click(fixture, root, 'confirm-keep');
    expect(has(root, 'confirm')).toBe(false);
    expect(cancel).not.toHaveBeenCalled();

    click(fixture, root, 'cancel');
    click(fixture, root, 'confirm-yes');
    expect(cancel).toHaveBeenCalledTimes(1);
    expect(cancel).toHaveBeenCalledWith(draft.id);
  });

  it('shows the invoice as Cancelled with the time and no Send, Cancel or Edit once it is cancelled', () => {
    const cancel = vi.fn().mockReturnValue(of(cancelled));
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(sent)), { cancel, role: 'Admin' });
    fixture.detectChanges();

    click(fixture, root, 'cancel');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Cancelled');
    expect(root.querySelector('[data-testid="cancelled-at"]')?.textContent?.trim()).not.toBe('');
    expect(has(root, 'cancel')).toBe(false);
    expect(has(root, 'send')).toBe(false);
    expect(has(root, 'edit')).toBe(false);
    expect(root.querySelector('[data-testid="grand-total"]')?.textContent).toContain('605.75');
  });

  it('shows the API\'s message for a cancel refused with 409 and reloads the invoice', () => {
    const detail = 'An invoice that is Cancelled cannot be cancelled.';
    const cancel = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail } })));
    const get = vi.fn().mockReturnValueOnce(of(sent)).mockReturnValueOnce(of(cancelled));
    const { fixture, root } = setup(get, { cancel, role: 'Admin' });
    fixture.detectChanges();

    click(fixture, root, 'cancel');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="action-error"]')?.textContent).toContain(detail);
    expect(get).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Cancelled');
    expect(has(root, 'cancel')).toBe(false);
  });

  it('after a 403 on cancel (the role changed) shows the message, reloads the same Sent invoice and stops offering Cancel', () => {
    const detail = 'You are not allowed to do this.';
    const cancel = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403, error: { detail } })));
    const get = vi.fn().mockReturnValue(of(sent));
    const { fixture, root } = setup(get, { cancel, role: 'Admin' });
    fixture.detectChanges();

    click(fixture, root, 'cancel');
    click(fixture, root, 'confirm-yes');

    expect(root.querySelector('[data-testid="action-error"]')?.textContent).toContain(detail);
    expect(get).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Sent');
    expect(has(root, 'cancel')).toBe(false);
  });

  it('says the invoice was not found when the cancel comes back 404', () => {
    const cancel = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const get = vi.fn().mockReturnValueOnce(of(draft)).mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 404 })));
    const { fixture, root } = setup(get, { cancel, role: 'Admin' });
    fixture.detectChanges();

    click(fixture, root, 'cancel');
    click(fixture, root, 'confirm-yes');

    expect(root.textContent).toContain('Invoice not found');
  });

  // ---- the status badge (US3) ----

  it.each([
    [draft, 'Draft'],
    [sent, 'Sent'],
    [overdue, 'Overdue'],
    [{ ...sent, status: 'Paid' as const, allowedTransitions: [] }, 'Paid'],
    [cancelled, 'Cancelled'],
  ])('shows the status badge as the right word for each kind of invoice (%#)', (invoice, expected) => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)));
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="status-badge"]')?.textContent?.trim()).toBe(expected);
  });

  it('styles the Overdue badge differently from Sent (the word itself is checked above)', () => {
    const late = setup(vi.fn().mockReturnValue(of(overdue)));
    late.fixture.detectChanges();
    const lateBadge = late.root.querySelector('[data-testid="status-badge"]') as HTMLElement;
    TestBed.resetTestingModule();
    const ontime = setup(vi.fn().mockReturnValue(of(sent)));
    ontime.fixture.detectChanges();
    const ontimeBadge = ontime.root.querySelector('[data-testid="status-badge"]') as HTMLElement;

    expect(lateBadge.classList.contains('overdue')).toBe(true);
    expect(ontimeBadge.classList.contains('overdue')).toBe(false);
  });

  it('disables the actions while cancelling and ignores a second click', () => {
    const pending = new Subject<Invoice>();
    const cancel = vi.fn().mockReturnValue(pending);
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(sent)), { cancel, role: 'Admin' });
    fixture.detectChanges();

    click(fixture, root, 'cancel');
    click(fixture, root, 'confirm-yes');
    click(fixture, root, 'confirm-yes');

    expect(cancel).toHaveBeenCalledTimes(1);
    expect((root.querySelector('[data-testid="cancel"]') as HTMLButtonElement).disabled).toBe(true);
    pending.next(cancelled);
    pending.complete();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="status-badge"]')?.textContent).toContain('Cancelled');
  });
});

// ---- payments (spec 008) ----

describe('InvoiceDetailComponent payments', () => {
  const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;
  const text = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`)?.textContent ?? '';

  /**
   * A Sent invoice whose figures are deliberately NOT what a client would work out (grand total 605.75, paid 100, so a browser
   * subtracting would show 505.75): the screen must show what the API returned, never calculate.
   */
  const withPayments: Invoice = {
    ...sent,
    acceptsPayment: true,
    allowedTransitions: [],
    paid: 100,
    remaining: 130,
    payments: [
      { id: 'p1', date: '2030-06-03', amount: 40.25, method: 'Cash', note: 'First part', recordedAtUtc: '2030-06-03T09:00:00Z' },
      { id: 'p2', date: '2030-06-04', amount: 59.75, method: 'Bank', note: null, recordedAtUtc: '2030-06-04T09:00:00Z' },
    ],
  };

  const noPayments: Invoice = { ...sent, acceptsPayment: true, paid: 0, remaining: 605.75, payments: [] };

  const paid: Invoice = {
    ...withPayments,
    status: 'Paid',
    acceptsPayment: false,
    paid: 605.75,
    remaining: 0,
    payments: [{ id: 'p9', date: '2030-06-05', amount: 605.75, method: 'Other', note: 'دفعة كاملة', recordedAtUtc: '2030-06-05T09:00:00Z' }],
  };

  it('shows every payment with its date, amount, method and note, in the order the API returned them', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)));
    fixture.detectChanges();

    const rows = Array.from(root.querySelectorAll('[data-testid="payment-row"]'));
    expect(rows.length).toBe(2);
    for (const expected of ['2030', 'SAR 40.25', 'Cash', 'First part']) {
      expect(rows[0].textContent).toContain(expected);
    }
    for (const expected of ['SAR 59.75', 'Bank']) {
      expect(rows[1].textContent).toContain(expected);
    }
    expect(Array.from(root.querySelectorAll('[data-testid="payments"] th')).map((cell) => cell.textContent?.trim())).toEqual([
      'Date', 'Amount', 'Method', 'Note',
    ]);
  });

  it('shows a payment with no note as a dash and keeps an Arabic note as written', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)));
    fixture.detectChanges();
    const rows = Array.from(root.querySelectorAll('[data-testid="payment-row"]'));

    expect(rows[1].querySelector('[data-testid="payment-note"]')?.textContent?.trim()).toBe('—');

    TestBed.resetTestingModule();
    const arabic = setup(vi.fn().mockReturnValue(of(paid)));
    arabic.fixture.detectChanges();
    const note = arabic.root.querySelector('[data-testid="payment-note"]');
    expect(note?.textContent).toContain('دفعة كاملة');
    expect(note?.getAttribute('dir')).toBe('auto');
  });

  it('shows Paid and Remaining exactly as the API returned them and calculates nothing', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)));
    fixture.detectChanges();

    expect(text(root, 'paid')).toContain('SAR 100.00');
    expect(text(root, 'remaining')).toContain('SAR 130.00');
    expect(root.textContent).not.toContain('505.75');
  });

  it('shows nothing paid, the whole amount remaining and "No payments yet" for a Sent invoice with no payments', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(noPayments)));
    fixture.detectChanges();

    expect(text(root, 'paid')).toContain('SAR 0.00');
    expect(text(root, 'remaining')).toContain('SAR 605.75');
    expect(text(root, 'no-payments')).toContain('No payments yet');
    expect(has(root, 'payments')).toBe(false);
  });

  it('shows a settled invoice with 0.00 remaining, its payments, the Paid badge and no way to add another', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(paid)));
    fixture.detectChanges();

    expect(text(root, 'status-badge')).toContain('Paid');
    expect(root.querySelector('[data-testid="status-badge"]')?.classList.contains('paid')).toBe(true);
    expect(root.querySelector('[data-testid="status-badge"]')?.classList.contains('overdue')).toBe(false);
    expect(text(root, 'remaining')).toContain('SAR 0.00');
    expect(root.querySelectorAll('[data-testid="payment-row"]').length).toBe(1);
    expect(has(root, 'record-payment')).toBe(false);
    expect(has(root, 'send')).toBe(false);
    expect(has(root, 'cancel')).toBe(false);
    expect(has(root, 'edit')).toBe(false);
  });

  it('does not show the payments section at all for a Draft or a Cancelled invoice', () => {
    const cancelledInvoice: Invoice = { ...sent, status: 'Cancelled', cancelledAtUtc: '2030-06-03T08:00:00Z', allowedTransitions: [] };
    for (const invoice of [draft, cancelledInvoice]) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)));
      fixture.detectChanges();

      expect(has(root, 'payments-section')).toBe(false);
      expect(has(root, 'record-payment')).toBe(false);
      expect(root.textContent).not.toContain('No payments yet');
      TestBed.resetTestingModule();
    }
  });

  // ---- the Record payment link follows acceptsPayment ----

  it('offers Record payment as a link to the payment form when the API says the invoice accepts one', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)));
    fixture.detectChanges();

    const link = root.querySelector('[data-testid="record-payment"]');
    expect(link?.textContent).toContain('Record payment');
    expect(link?.getAttribute('href')).toBe(`/invoices/${draft.id}/payments/new`);
  });

  it('offers Record payment for a late Sent invoice too', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...noPayments, isOverdue: true })));
    fixture.detectChanges();

    expect(text(root, 'status-badge')).toContain('Overdue');
    expect(has(root, 'record-payment')).toBe(true);
  });

  it('does not offer Record payment when acceptsPayment is false, whatever the status says', () => {
    // A Sent invoice with nothing remaining: the flag, not the status, decides.
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...noPayments, acceptsPayment: false, remaining: 0 })));
    fixture.detectChanges();

    expect(has(root, 'record-payment')).toBe(false);
  });

  it.each(['Agent', 'Admin'] as const)('offers Record payment to an %s alike (any signed-in user may record one)', (role) => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)), { role });
    fixture.detectChanges();

    expect(has(root, 'record-payment')).toBe(true);
  });

  // ---- Cancel follows allowedTransitions ----

  it('does not offer Cancel to an Admin once a payment exists (allowedTransitions has no Cancelled)', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)), { role: 'Admin' });
    fixture.detectChanges();

    expect(has(root, 'cancel')).toBe(false);
  });

  it('still offers Cancel to an Admin for a Sent invoice with no payments', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of({ ...noPayments, allowedTransitions: ['Cancelled'] })), { role: 'Admin' });
    fixture.detectChanges();

    expect(has(root, 'cancel')).toBe(true);
  });

  it('shows the payment date exactly as saved, not shifted by a time zone', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(withPayments)));
    fixture.detectChanges();
    const rows = Array.from(root.querySelectorAll('[data-testid="payment-row"]'));

    expect(rows[0].textContent).toContain('Jun 3, 2030');
    expect(rows[1].textContent).toContain('Jun 4, 2030');
  });

  it('hides Record payment while a cancel question is open, so a payment cannot be started beside a cancel', () => {
    const cancellable: Invoice = { ...noPayments, allowedTransitions: ['Cancelled'] };
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(cancellable)), { role: 'Admin' });
    fixture.detectChanges();
    expect(has(root, 'record-payment')).toBe(true);

    (root.querySelector('[data-testid="cancel"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(has(root, 'confirm')).toBe(true);
    expect(has(root, 'record-payment')).toBe(false);

    (root.querySelector('[data-testid="confirm-keep"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(has(root, 'record-payment')).toBe(true);
  });

  it('shows the API message when a cancel is refused because the invoice has payments, and Cancel and Record payment follow the reloaded invoice', () => {
    const cancellable: Invoice = { ...noPayments, allowedTransitions: ['Cancelled'] };
    const cancel = vi.fn().mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'An invoice that has payments cannot be cancelled.' } })),
    );
    const get = vi.fn().mockReturnValueOnce(of(cancellable)).mockReturnValueOnce(of(withPayments));
    const { fixture, root } = setup(get, { cancel, role: 'Admin' });
    fixture.detectChanges();

    (root.querySelector('[data-testid="cancel"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (root.querySelector('[data-testid="confirm-yes"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(text(root, 'action-error')).toContain('An invoice that has payments cannot be cancelled.');
    expect(get).toHaveBeenCalledTimes(2);
    expect(has(root, 'cancel')).toBe(false);
    expect(root.querySelectorAll('[data-testid="payment-row"]').length).toBe(2);
  });

  it('still offers Send, Edit and Cancel for a Draft exactly as in 007', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { role: 'Admin' });
    fixture.detectChanges();

    expect(has(root, 'send')).toBe(true);
    expect(has(root, 'edit')).toBe(true);
    expect(has(root, 'cancel')).toBe(true);
    expect(has(root, 'record-payment')).toBe(false);
  });
});

// ---- the Print link (spec 009) ----

describe('InvoiceDetailComponent Print link', () => {
  const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;

  const late: Invoice = { ...sent, isOverdue: true };
  const settled: Invoice = { ...sent, status: 'Paid', allowedTransitions: [], acceptsPayment: false, paid: 605.75, remaining: 0 };
  const cancelledInvoice: Invoice = { ...sent, status: 'Cancelled', cancelledAtUtc: '2030-06-03T08:00:00Z', allowedTransitions: [] };

  it.each([
    ['Draft', draft],
    ['Sent', sent],
    ['Overdue', late],
    ['Paid', settled],
    ['Cancelled', cancelledInvoice],
  ] as [string, Invoice][])('offers Print for a %s invoice to an Admin and to an Agent, to the printable page, in the same tab', (_name, invoice) => {
    for (const role of ['Admin', 'Agent'] as const) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)), { role });
      fixture.detectChanges();

      const link = root.querySelector('[data-testid="print"]');
      expect(link?.textContent?.trim()).toBe('Print');
      expect(link?.getAttribute('href')).toBe(`/invoices/${draft.id}/print`);
      // A new tab would not have the session (it lives in this tab's sessionStorage), so the link must not open one.
      expect(link?.hasAttribute('target')).toBe(false);
      TestBed.resetTestingModule();
    }
  });

  it('offers Print even while an action is pending (it only reads)', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { role: 'Admin' });
    fixture.detectChanges();

    (root.querySelector('[data-testid="send"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(has(root, 'confirm')).toBe(true);
    expect(has(root, 'print')).toBe(true);
  });

  it('does not offer Print while the invoice is loading, was not found or could not be loaded', () => {
    const loading = setup(vi.fn().mockReturnValue(new Subject<Invoice>()));
    expect(has(loading.root, 'print')).toBe(false);
    TestBed.resetTestingModule();

    const notFound = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));
    notFound.fixture.detectChanges();
    expect(has(notFound.root, 'print')).toBe(false);
    TestBed.resetTestingModule();

    const failed = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));
    failed.fixture.detectChanges();
    expect(has(failed.root, 'print')).toBe(false);
  });

  it('puts History and Print before the way back to the list and leaves Send, Edit, Cancel and Record payment as they were', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { role: 'Admin' });
    fixture.detectChanges();

    const actions = Array.from(root.querySelectorAll('mat-card-actions a, mat-card-actions button')).map((element) => element.getAttribute('data-testid') ?? element.textContent?.trim());
    expect(actions).toEqual(['edit', 'send', 'cancel', 'history', 'print', 'Back to invoices']);
  });
});

// ---- the History link (spec 010) ----

describe('InvoiceDetailComponent History link', () => {
  const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;

  const late: Invoice = { ...sent, isOverdue: true };
  const settled: Invoice = { ...sent, status: 'Paid', allowedTransitions: [], acceptsPayment: false, paid: 605.75, remaining: 0 };
  const cancelledInvoice: Invoice = { ...sent, status: 'Cancelled', cancelledAtUtc: '2030-06-03T08:00:00Z', allowedTransitions: [] };

  it.each([
    ['Draft', draft],
    ['Sent', sent],
    ['Overdue', late],
    ['Paid', settled],
    ['Cancelled', cancelledInvoice],
  ] as [string, Invoice][])('offers History for a %s invoice to an Admin and to an Agent, to the history page, in the same tab', (_name, invoice) => {
    for (const role of ['Admin', 'Agent'] as const) {
      const { fixture, root } = setup(vi.fn().mockReturnValue(of(invoice)), { role });
      fixture.detectChanges();

      const link = root.querySelector('[data-testid="history"]');
      expect(link?.textContent?.trim()).toBe('History');
      expect(link?.getAttribute('href')).toBe(`/invoices/${draft.id}/history`);
      expect(link?.hasAttribute('target')).toBe(false);
      TestBed.resetTestingModule();
    }
  });

  it('offers History even while an action is pending (it only reads)', () => {
    const { fixture, root } = setup(vi.fn().mockReturnValue(of(draft)), { role: 'Admin' });
    fixture.detectChanges();

    (root.querySelector('[data-testid="send"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(has(root, 'confirm')).toBe(true);
    expect(has(root, 'history')).toBe(true);
  });

  it('does not offer History while the invoice is loading, was not found or could not be loaded', () => {
    const loading = setup(vi.fn().mockReturnValue(new Subject<Invoice>()));
    expect(has(loading.root, 'history')).toBe(false);
    TestBed.resetTestingModule();

    const notFound = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));
    notFound.fixture.detectChanges();
    expect(has(notFound.root, 'history')).toBe(false);
    TestBed.resetTestingModule();

    const failed = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));
    failed.fixture.detectChanges();
    expect(has(failed.root, 'history')).toBe(false);
  });
});
