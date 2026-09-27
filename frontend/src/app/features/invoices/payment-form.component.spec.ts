import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { PAYMENT_AMOUNT_MESSAGE, PAYMENT_AMOUNT_REQUIRED } from '../../core/money/decimal-text';
import { PaymentFormComponent } from './payment-form.component';

const ID = '11111111-1111-1111-1111-111111111111';

/** A Sent invoice of 230.00 that accepts a payment (the numbers are what the API returned; the form calculates nothing). */
const sent: Invoice = {
  id: ID,
  number: 'INV-000123',
  status: 'Sent',
  customer: { id: '22222222-2222-2222-2222-222222222222', name: 'ABC', isActive: true },
  dueDate: '2099-07-01',
  notes: null,
  createdAtUtc: '2030-06-01T10:00:00Z',
  updatedAtUtc: '2030-06-02T11:30:00Z',
  isOverdue: false,
  sentAtUtc: '2030-06-02T09:00:00Z',
  cancelledAtUtc: null,
  allowedTransitions: ['Cancelled'],
  isEditable: false,
  discount: 0,
  totals: { totalNet: 200, discount: 0, netAfterDiscount: 200, totalVat: 30, grandTotal: 230 },
  lines: [],
  paid: 100,
  remaining: 130,
  acceptsPayment: true,
  payments: [],
};

function todayText(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

describe('PaymentFormComponent', () => {
  let get: ReturnType<typeof vi.fn>;
  let recordPayment: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup(loaded: Invoice | HttpErrorResponse | Subject<Invoice> = sent) {
    get = vi.fn().mockReturnValue(
      loaded instanceof HttpErrorResponse ? throwError(() => loaded) : loaded instanceof Subject ? loaded : of(loaded),
    );
    recordPayment = vi.fn();
    TestBed.configureTestingModule({
      imports: [PaymentFormComponent],
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { get, recordPayment } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ID }) } } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(PaymentFormComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<PaymentFormComponent>) => fixture.nativeElement as HTMLElement;
  const form = (fixture: ComponentFixture<PaymentFormComponent>) => (fixture.componentInstance as unknown as { form: FormGroup }).form;
  const field = (fixture: ComponentFixture<PaymentFormComponent>, testId: string) =>
    root(fixture).querySelector(`[data-testid="${testId}"]`) as HTMLInputElement | HTMLTextAreaElement;

  function typeInto(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function submit(fixture: ComponentFixture<PaymentFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  /** Fills a valid payment; the method is a native select, so it is set through the form control. */
  function fillValid(fixture: ComponentFixture<PaymentFormComponent>, amount = '100.10') {
    typeInto(field(fixture, 'date'), '2030-06-02');
    typeInto(field(fixture, 'amount'), amount);
    form(fixture).controls['method'].setValue('Cash');
    typeInto(field(fixture, 'note'), '  First half  ');
    fixture.detectChanges();
  }

  beforeEach(() => vi.restoreAllMocks());

  // ---- loading and what is shown ----

  it('shows a loading state first', () => {
    const fixture = setup(new Subject<Invoice>());

    expect(root(fixture).querySelector('[data-testid="form-loading"]')).not.toBeNull();
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('loads the invoice by the route id and shows the heading, the number, the customer and what remains exactly as returned', () => {
    const fixture = setup();

    expect(get).toHaveBeenCalledWith(ID);
    expect(root(fixture).querySelector('h1')?.textContent).toContain('Record payment');
    const summary = root(fixture).querySelector('[data-testid="summary"]')?.textContent ?? '';
    expect(summary).toContain('INV-000123');
    expect(summary).toContain('ABC');
    // 130 is what the API said remains (230 - 100); it is shown as given, never worked out here.
    expect(summary).toContain('SAR 130.00');
  });

  it('shows what remains as the API returned it even when that is not what a client would work out', () => {
    const fixture = setup({ ...sent, remaining: 17.5 });

    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).toContain('SAR 17.50');
  });

  it('offers date, amount, method and note, with the date prefilled with the browsers date and the rest empty', () => {
    const fixture = setup();

    expect(field(fixture, 'date').value).toBe(todayText());
    expect(field(fixture, 'amount').value).toBe('');
    expect(field(fixture, 'note').value).toBe('');
    expect(form(fixture).controls['method'].value).toBe('');
    expect(Object.keys(form(fixture).controls).sort()).toEqual(['amount', 'date', 'method', 'note']);
  });

  it('offers exactly the methods Cash, Bank and Other', () => {
    const fixture = setup();

    const options = (fixture.componentInstance as unknown as { methods: readonly string[] }).methods;

    expect(options).toEqual(['Cash', 'Bank', 'Other']);
  });

  it('has a way back to the invoice', () => {
    const fixture = setup();

    expect(root(fixture).querySelector(`a[href="/invoices/${ID}"]`)).not.toBeNull();
  });

  // ---- a valid submit ----

  it('sends the date, the amount as a number exactly as typed, the method and the trimmed note, then returns to the invoice', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(of({ ...sent, paid: 200.1, remaining: 29.9 }));

    fillValid(fixture, '100.10');
    submit(fixture);

    expect(recordPayment).toHaveBeenCalledTimes(1);
    expect(recordPayment).toHaveBeenCalledWith(ID, { date: '2030-06-02', amount: 100.1, method: 'Cash', note: 'First half' });
    expect(navigate).toHaveBeenCalledWith(['/invoices', ID], { replaceUrl: true });
  });

  it('sends an empty note as an empty string (the API turns it into none)', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(of(sent));

    fillValid(fixture);
    typeInto(field(fixture, 'note'), '');
    submit(fixture);

    expect(recordPayment.mock.calls[0][1].note).toBe('');
  });

  it('accepts an amount with a trailing zero beyond two places and sends its value', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(of(sent));

    fillValid(fixture, '100.100');
    submit(fixture);

    expect(recordPayment.mock.calls[0][1].amount).toBe(100.1);
  });

  // ---- the client's own checks (nothing is sent) ----

  it('refuses an empty amount on the client and sends nothing', () => {
    const fixture = setup();

    fillValid(fixture, '');
    submit(fixture);

    expect(recordPayment).not.toHaveBeenCalled();
    expect(root(fixture).textContent).toContain(PAYMENT_AMOUNT_REQUIRED);
  });

  it.each(['abc', '0', '-5', '10.005', '1,5'])('refuses the amount "%s" with the rule and sends nothing', (amount) => {
    const fixture = setup();

    fillValid(fixture, amount);
    submit(fixture);

    expect(recordPayment).not.toHaveBeenCalled();
    expect(root(fixture).textContent).toContain(PAYMENT_AMOUNT_MESSAGE);
  });

  it('refuses a payment with no method chosen and sends nothing', () => {
    const fixture = setup();

    fillValid(fixture);
    form(fixture).controls['method'].setValue('');
    submit(fixture);

    expect(recordPayment).not.toHaveBeenCalled();
    expect(root(fixture).textContent).toContain('Choose a payment method.');
  });

  it('refuses an empty date and sends nothing', () => {
    const fixture = setup();

    fillValid(fixture);
    typeInto(field(fixture, 'date'), '');
    submit(fixture);

    expect(recordPayment).not.toHaveBeenCalled();
    expect(root(fixture).textContent).toContain('Date is required.');
  });

  it('refuses a note of more than 1000 characters and sends nothing', () => {
    const fixture = setup();

    fillValid(fixture);
    form(fixture).controls['note'].setValue('x'.repeat(1001));
    submit(fixture);

    expect(recordPayment).not.toHaveBeenCalled();
    expect(root(fixture).textContent).toContain('Note must be at most 1,000 characters.');
  });

  // ---- the API's field messages ----

  it('puts each message from a 400 on its own field and keeps everything that was typed', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              title: 'One or more validation errors occurred.',
              errors: {
                date: ['Date cannot be in the future.'],
                amount: ['Amount cannot be more than the remaining 130.00 SAR.'],
                note: ['Note must be at most 1,000 characters.'],
              },
            },
          }),
      ),
    );

    fillValid(fixture, '500');
    submit(fixture);

    const text = root(fixture).textContent ?? '';
    expect(text).toContain('Date cannot be in the future.');
    expect(text).toContain('Amount cannot be more than the remaining 130.00 SAR.');
    expect(text).toContain('Note must be at most 1,000 characters.');
    expect(field(fixture, 'amount').value).toBe('500');
    expect(field(fixture, 'date').value).toBe('2030-06-02');
    expect(form(fixture).controls['method'].value).toBe('Cash');
    expect(navigate).not.toHaveBeenCalled();
  });

  // ---- refusals (US2) ----

  it('shows every message from a 400 together and gives no reload button for it', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { date: ['Date is required.'], amount: ['Amount is required.'], method: ['Method is required.'] } },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    const text = root(fixture).textContent ?? '';
    expect(text).toContain('Date is required.');
    expect(text).toContain('Amount is required.');
    expect(text).toContain('Method is required.');
    expect(root(fixture).querySelector('[data-testid="reload-latest"]')).toBeNull();
  });

  it('disables the form while a payment is being recorded and ignores a second submit', () => {
    const fixture = setup();
    const pending = new Subject<Invoice>();
    recordPayment.mockReturnValue(pending);

    fillValid(fixture);
    submit(fixture);
    submit(fixture);

    expect(recordPayment).toHaveBeenCalledTimes(1);
    expect((root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    pending.next(sent);
    pending.complete();
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('enables the button again and keeps the inputs after a refusal so the person can try again', () => {
    const fixture = setup();
    recordPayment.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { amount: ['Amount cannot be more than the remaining 130.00 SAR.'] } } })),
    );
    recordPayment.mockReturnValueOnce(of(sent));

    fillValid(fixture, '500');
    submit(fixture);
    expect((root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
    typeInto(field(fixture, 'amount'), '130');
    fixture.detectChanges();
    submit(fixture);

    expect(recordPayment).toHaveBeenCalledTimes(2);
    expect(recordPayment.mock.calls[1][1].amount).toBe(130);
    expect(navigate).toHaveBeenCalledWith(['/invoices', ID], { replaceUrl: true });
  });

  it('shows a network failure as a message at the top and keeps what was typed', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    fillValid(fixture);
    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="form-error"]')?.textContent).toContain('Cannot reach the service');
    expect(field(fixture, 'amount').value).toBe('100.10');
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each([
    [409, 'An invoice that is Paid cannot take a payment.'],
    [409, 'This invoice was changed by someone else. Reload it and try again.'],
  ])('a 409 (%#) shows the API message at the top, keeps the typed values and offers to discard them and reload', (status, detail) => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { title: 'Conflict', detail } })));

    fillValid(fixture);
    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="form-error"]')?.textContent).toContain(detail);
    expect(field(fixture, 'amount').value).toBe('100.10');
    expect(root(fixture).querySelector('[data-testid="reload-latest"]')?.textContent).toContain('Discard my changes and reload the invoice');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('reloading after a 409 fetches the invoice again and, if it still takes a payment, shows a fresh form with the new remaining', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'This invoice was changed by someone else. Reload it and try again.' } })));
    fillValid(fixture);
    submit(fixture);

    get.mockReturnValue(of({ ...sent, paid: 200, remaining: 30 }));
    (root(fixture).querySelector('[data-testid="reload-latest"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(3);
    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).toContain('SAR 30.00');
    expect(field(fixture, 'amount').value).toBe('');
    expect(root(fixture).querySelector('[data-testid="form-error"]')).toBeNull();
    expect(root(fixture).querySelector('[data-testid="reload-latest"]')).toBeNull();
  });

  it('reloading after a 409 shows the not-payable message when the invoice can no longer take a payment', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'An invoice that is Paid cannot take a payment.' } })));
    fillValid(fixture);
    submit(fixture);

    get.mockReturnValue(of({ ...sent, status: 'Paid', paid: 230, remaining: 0, acceptsPayment: false, allowedTransitions: [] }));
    (root(fixture).querySelector('[data-testid="reload-latest"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-testid="not-payable"]')?.textContent).toContain('It is Paid.');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  // ---- an invoice that does not take a payment ----

  it.each([
    ['Draft', { status: 'Draft' as const, acceptsPayment: false, isEditable: true, allowedTransitions: ['Sent', 'Cancelled'] as Invoice['allowedTransitions'] }],
    ['Paid', { status: 'Paid' as const, acceptsPayment: false, paid: 230, remaining: 0, allowedTransitions: [] as Invoice['allowedTransitions'] }],
    ['Cancelled', { status: 'Cancelled' as const, acceptsPayment: false, allowedTransitions: [] as Invoice['allowedTransitions'] }],
  ])('shows "cannot take a payment. It is %s." with a way back and no form', (shown, change) => {
    const fixture = setup({ ...sent, ...change });

    const message = root(fixture).querySelector('[data-testid="not-payable"]');
    expect(message?.getAttribute('role')).toBe('alert');
    expect(message?.textContent).toContain(`This invoice cannot take a payment. It is ${shown}.`);
    expect(root(fixture).querySelector('form')).toBeNull();
    expect(root(fixture).querySelector('[data-testid="amount"]')).toBeNull();
    expect(message?.querySelector(`a[href="/invoices/${ID}"]`)?.textContent).toContain('Back to invoice');
  });

  it('shows the form, not the message, for a late Sent invoice that accepts a payment', () => {
    const fixture = setup({ ...sent, isOverdue: true });

    expect(root(fixture).querySelector('[data-testid="not-payable"]')).toBeNull();
    expect(root(fixture).querySelector('form')).not.toBeNull();
  });

  it('follows acceptsPayment, not the status: a Sent invoice that accepts none is not offered a form', () => {
    const fixture = setup({ ...sent, acceptsPayment: false, paid: 230, remaining: 0 });

    expect(root(fixture).querySelector('[data-testid="not-payable"]')?.textContent).toContain('It is Sent.');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('calls a late Sent invoice that does not accept a payment Overdue, the word the other screens use', () => {
    const fixture = setup({ ...sent, isOverdue: true, acceptsPayment: false, remaining: 0 });

    expect(root(fixture).querySelector('[data-testid="not-payable"]')?.textContent).toContain('It is Overdue.');
  });

  // ---- loading problems ----

  it('says the invoice was not found for a 404 and offers the list', () => {
    const fixture = setup(new HttpErrorResponse({ status: 404 }));

    expect(root(fixture).querySelector('[data-testid="not-found"]')).not.toBeNull();
    expect(root(fixture).querySelector('form')).toBeNull();
    expect(root(fixture).querySelector('a[href="/invoices"]')).not.toBeNull();
  });

  it('shows an error with a Retry for any other load failure and loads again on Retry', () => {
    const fixture = setup(new HttpErrorResponse({ status: 500 }));
    expect(root(fixture).querySelector('[data-testid="load-error"]')).not.toBeNull();

    get.mockReturnValue(of(sent));
    (Array.from(root(fixture).querySelectorAll('button')).find((button) => button.textContent?.includes('Retry')) as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(2);
    expect(root(fixture).querySelector('form')).not.toBeNull();
  });

  // ---- review fixes (spec 008, T040) ----

  it('says nothing remains to be paid for a Sent invoice with nothing left, not only that it is Sent', () => {
    const fixture = setup({ ...sent, acceptsPayment: false, paid: 230, remaining: 0 });

    const text = root(fixture).querySelector('[data-testid="not-payable"]')?.textContent ?? '';
    expect(text).toContain('It is Sent.');
    expect(text).toContain('Nothing remains to be paid on it.');
  });

  it('does not add that sentence for a Paid or Draft invoice', () => {
    const fixture = setup({ ...sent, status: 'Paid', acceptsPayment: false, remaining: 0 });

    expect(root(fixture).querySelector('[data-testid="not-payable"]')?.textContent).not.toContain('Nothing remains');
  });

  // An answer that never came back: the payment may already be recorded, and a payment cannot be undone.
  it.each([
    [0, 'Cannot reach the service.'],
    [500, 'The service failed.'],
    [503, 'The service failed.'],
  ])('after a failure with status %s the form says the payment may have been recorded and will not save again until the invoice is checked', (status, reason) => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

    fillValid(fixture);
    submit(fixture);

    const message = root(fixture).querySelector('[data-testid="form-error"]')?.textContent ?? '';
    expect(message).toContain(reason);
    expect(message).toContain('could not confirm whether the payment was recorded');
    expect((root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    expect(root(fixture).querySelector('[data-testid="check-again"]')).not.toBeNull();
    expect(field(fixture, 'amount').value).toBe('100.10');

    // A second submit (Enter in a field) sends nothing.
    submit(fixture);
    expect(recordPayment).toHaveBeenCalledTimes(1);
  });

  it('checking the invoice again keeps what was typed, shows the current remaining and allows saving again', () => {
    const fixture = setup();
    recordPayment.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    recordPayment.mockReturnValueOnce(of(sent));
    fillValid(fixture);
    submit(fixture);

    // The first attempt did reach the server: 100 more is already paid.
    get.mockReturnValue(of({ ...sent, paid: 200, remaining: 30 }));
    (root(fixture).querySelector('[data-testid="check-again"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).toContain('SAR 30.00');
    expect(field(fixture, 'amount').value).toBe('100.10');
    expect((root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
    expect(root(fixture).querySelector('[data-testid="form-error"]')).toBeNull();
    expect(root(fixture).querySelector('[data-testid="check-again"]')).toBeNull();
  });

  it('checking the invoice again shows the not-payable message if the lost payment settled it', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    fillValid(fixture, '130');
    submit(fixture);

    get.mockReturnValue(of({ ...sent, status: 'Paid', paid: 230, remaining: 0, acceptsPayment: false, allowedTransitions: [] }));
    (root(fixture).querySelector('[data-testid="check-again"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-testid="not-payable"]')?.textContent).toContain('It is Paid.');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('after a 409 the invoice is fetched again quietly: the figure at the top updates and what was typed stays', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'This invoice was changed by someone else. Reload it and try again.' } })));
    fillValid(fixture);
    get.mockReturnValue(of({ ...sent, paid: 200, remaining: 30 }));

    submit(fixture);

    expect(get).toHaveBeenCalledTimes(2);
    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).toContain('SAR 30.00');
    expect(field(fixture, 'amount').value).toBe('100.10');
    expect(root(fixture).querySelector('[data-testid="reload-latest"]')).not.toBeNull();
  });

  it('after a 409 for an invoice that can no longer take a payment, the message takes the form\'s place', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'An invoice that is Paid cannot take a payment.' } })));
    fillValid(fixture);
    get.mockReturnValue(of({ ...sent, status: 'Paid', paid: 230, remaining: 0, acceptsPayment: false, allowedTransitions: [] }));

    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="not-payable"]')?.textContent).toContain('It is Paid.');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('after a 400 about the amount (more than remains) the figure at the top is refreshed, so it no longer contradicts the message', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { amount: ['Amount cannot be more than the remaining 30.00 SAR.'] } } })),
    );
    fillValid(fixture, '130');
    get.mockReturnValue(of({ ...sent, paid: 200, remaining: 30 }));

    submit(fixture);

    expect(get).toHaveBeenCalledTimes(2);
    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).toContain('SAR 30.00');
    expect(field(fixture, 'amount').closest('mat-form-field')?.textContent).toContain('Amount cannot be more than the remaining 30.00 SAR.');
  });

  it('does not fetch the invoice again after a 400 that is not about the amount', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { note: ['Note must be at most 1,000 characters.'] } } })),
    );
    fillValid(fixture);

    submit(fixture);

    expect(get).toHaveBeenCalledTimes(1);
  });

  it('a slow older answer to a load can never overwrite a newer one', () => {
    const fixture = setup();
    const older = new Subject<Invoice>();
    get.mockReturnValueOnce(older).mockReturnValueOnce(of({ ...sent, remaining: 42 }));
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    fillValid(fixture);
    submit(fixture);

    (root(fixture).querySelector('[data-testid="check-again"]') as HTMLButtonElement).click(); // older request, still waiting
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { load: () => void }).load(); // newer request, answers at once
    fixture.detectChanges();
    older.next({ ...sent, remaining: 999 });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).toContain('SAR 42.00');
    expect(root(fixture).querySelector('[data-testid="summary"]')?.textContent).not.toContain('999');
  });

  // ---- focus, placement and the real select ----

  it('moves focus to the first field in error after a submit the form itself refuses', () => {
    const fixture = setup();

    fillValid(fixture, 'abc');
    submit(fixture);

    expect(field(fixture, 'amount').getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(field(fixture, 'amount'));
  });

  it('moves focus to the first field the API complained about', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { note: ['Note must be at most 1,000 characters.'] } } })));

    fillValid(fixture);
    submit(fixture);

    expect(document.activeElement).toBe(field(fixture, 'note'));
  });

  it('puts each API message inside its own field and no other', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { date: ['DATE-MESSAGE'], amount: ['AMOUNT-MESSAGE'], note: ['NOTE-MESSAGE'] } },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    const inside = (testId: string) => field(fixture, testId).closest('mat-form-field')?.textContent ?? '';
    expect(inside('date')).toContain('DATE-MESSAGE');
    expect(inside('date')).not.toContain('AMOUNT-MESSAGE');
    expect(inside('amount')).toContain('AMOUNT-MESSAGE');
    expect(inside('amount')).not.toContain('NOTE-MESSAGE');
    expect(inside('note')).toContain('NOTE-MESSAGE');
    expect(inside('method')).not.toContain('MESSAGE');
  });

  it('renders exactly the three methods (after a disabled "Choose a method") and choosing one in the real select sets the control', () => {
    const fixture = setup();
    const select = field(fixture, 'method') as unknown as HTMLSelectElement;

    const options = Array.from(select.querySelectorAll('option'));
    expect(options.map((option) => option.textContent?.trim())).toEqual(['Choose a method', 'Cash', 'Bank', 'Other']);
    expect(options[0].disabled).toBe(true);

    select.value = 'Bank';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(form(fixture).controls['method'].value).toBe('Bank');
  });

  it('lets a note of more than 1000 characters be typed and then refuses it with its message (no silent cut-off)', () => {
    const fixture = setup();

    fillValid(fixture);
    typeInto(field(fixture, 'note'), 'x'.repeat(1001));
    submit(fixture);

    expect(field(fixture, 'note').value.length).toBe(1001);
    expect(recordPayment).not.toHaveBeenCalled();
    expect(field(fixture, 'note').closest('mat-form-field')?.textContent).toContain('Note must be at most 1,000 characters.');
  });

  // ---- what the person sees while saving, and afterwards ----

  it('says Saving… while a payment is being recorded and takes the way back away until it settles', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(new Subject<Invoice>());

    fillValid(fixture);
    submit(fixture);

    const button = root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.textContent).toContain('Saving…');
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(root(fixture).querySelector('[data-testid="back"]')).toBeNull();
  });

  it('enables the button again after a 409 and shows the way back', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'x' } })));

    fillValid(fixture);
    submit(fixture);

    expect((root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
    expect(root(fixture).querySelector('[data-testid="back"]')?.textContent).toContain('Back to invoice');
  });

  it('reloading after a 409 also puts the date, method and note back to a fresh form', () => {
    const fixture = setup();
    recordPayment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'x' } })));
    fillValid(fixture);
    submit(fixture);

    (root(fixture).querySelector('[data-testid="reload-latest"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(field(fixture, 'date').value).toBe(todayText());
    expect(form(fixture).controls['method'].value).toBe('');
    expect(field(fixture, 'note').value).toBe('');
    expect(field(fixture, 'amount').value).toBe('');
  });

  it('a payment that finishes after the person has left neither navigates nor changes anything', () => {
    const fixture = setup();
    const pending = new Subject<Invoice>();
    recordPayment.mockReturnValue(pending);
    fillValid(fixture);
    submit(fixture);

    fixture.destroy();
    pending.next(sent);
    pending.complete();

    expect(navigate).not.toHaveBeenCalled();
  });
});
