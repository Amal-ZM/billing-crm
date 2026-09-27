import { HttpErrorResponse } from '@angular/common/http';
import { Component, forwardRef, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ControlValueAccessor, FormArray, FormGroup, NG_VALUE_ACCESSOR } from '@angular/forms';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { InvoiceFormComponent } from './invoice-form.component';
import { CustomerPickerComponent } from './pickers/customer-picker.component';
import { ProductPickerComponent } from './pickers/product-picker.component';

/** The real pickers are tested on their own; here they are plain stand-ins so the form can be driven through its controls. */
@Component({
  selector: 'app-customer-picker',
  template: '<span data-testid="customer-stub"></span>',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => StubCustomerPickerComponent), multi: true }],
})
class StubCustomerPickerComponent implements ControlValueAccessor {
  readonly displayName = input<string | null>(null);
  readonly invalid = input(false);
  readonly errorId = input<string | null>(null);
  writeValue(): void {}
  registerOnChange(): void {}
  registerOnTouched(): void {}
}

@Component({
  selector: 'app-product-picker',
  template: '<span data-testid="product-stub"></span>',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => StubProductPickerComponent), multi: true }],
})
class StubProductPickerComponent implements ControlValueAccessor {
  readonly invalid = input(false);
  readonly errorId = input<string | null>(null);
  writeValue(): void {}
  registerOnChange(): void {}
  registerOnTouched(): void {}
}

const CUSTOMER = '22222222-2222-2222-2222-222222222222';
const NEW_PRODUCT = '77777777-7777-7777-7777-777777777777';
const LINE_1 = '33333333-3333-3333-3333-333333333333';
const LINE_2 = '55555555-5555-5555-5555-555555555555';

const draft: Invoice = {
  id: '11111111-1111-1111-1111-111111111111',
  number: 'INV-000123',
  status: 'Draft',
  customer: { id: CUSTOMER, name: 'ABC', isActive: true },
  dueDate: '2099-07-01',
  notes: 'Net 30',
  createdAtUtc: '2030-06-01T10:00:00Z',
  updatedAtUtc: '2030-06-01T10:00:00Z',
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
      id: LINE_1,
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
      id: LINE_2,
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

const QUANTITY_MESSAGE = 'Quantity must be a number greater than zero with at most two decimal places.';

describe('InvoiceFormComponent (edit)', () => {
  let get: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup(loaded: Invoice | HttpErrorResponse = draft) {
    get = vi.fn().mockReturnValue(loaded instanceof HttpErrorResponse ? throwError(() => loaded) : of(loaded));
    update = vi.fn();
    TestBed.configureTestingModule({
      imports: [InvoiceFormComponent],
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { get, update } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: draft.id }) } } },
      ],
    }).overrideComponent(InvoiceFormComponent, {
      remove: { imports: [CustomerPickerComponent, ProductPickerComponent] },
      add: { imports: [StubCustomerPickerComponent, StubProductPickerComponent] },
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(InvoiceFormComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<InvoiceFormComponent>) => fixture.nativeElement as HTMLElement;
  const text = (fixture: ComponentFixture<InvoiceFormComponent>) => root(fixture).textContent ?? '';
  const form = (fixture: ComponentFixture<InvoiceFormComponent>) => (fixture.componentInstance as unknown as { form: FormGroup }).form;
  const lines = (fixture: ComponentFixture<InvoiceFormComponent>) => form(fixture).get('lines') as FormArray;
  const line = (fixture: ComponentFixture<InvoiceFormComponent>, index: number) => lines(fixture).at(index) as FormGroup;
  const rows = (fixture: ComponentFixture<InvoiceFormComponent>) => Array.from(root(fixture).querySelectorAll('[data-testid="line"]'));
  const quantityInput = (fixture: ComponentFixture<InvoiceFormComponent>, index: number) =>
    root(fixture).querySelectorAll('input[data-testid="quantity"]')[index] as HTMLInputElement;

  function typeInto(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function submit(fixture: ComponentFixture<InvoiceFormComponent>) {
    (root(fixture).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  const click = (fixture: ComponentFixture<InvoiceFormComponent>, selector: string, index = 0) => {
    (root(fixture).querySelectorAll(selector)[index] as HTMLButtonElement).click();
    fixture.detectChanges();
  };

  beforeEach(() => vi.restoreAllMocks());

  it('loads the invoice and pre-fills the customer, the due date, the notes and every line', () => {
    const fixture = setup();

    expect(get).toHaveBeenCalledWith(draft.id);
    expect(root(fixture).querySelector('h1')?.textContent).toContain('Edit invoice');
    expect(form(fixture).getRawValue().customerId).toBe(CUSTOMER);
    expect((root(fixture).querySelector('[formControlName="dueDate"]') as HTMLInputElement).value).toBe('2099-07-01');
    expect((root(fixture).querySelector('[formControlName="notes"]') as HTMLTextAreaElement).value).toBe('Net 30');
    expect(rows(fixture).length).toBe(2);
    expect(quantityInput(fixture, 0).value).toBe('2');
    expect(quantityInput(fixture, 1).value).toBe('1.5');
  });

  it('shows an existing line with its saved name, SKU, unit, price and VAT as read-only text and no product picker', () => {
    const fixture = setup();

    const first = rows(fixture)[0].textContent ?? '';
    for (const expected of ['Consulting hour', 'CNS-001', 'Hour', 'SAR', '100.00', '15%']) {
      expect(first).toContain(expected);
    }
    expect(rows(fixture)[0].querySelector('[data-testid="product-stub"]')).toBeNull();
    expect(rows(fixture)[1].textContent).toContain('ساعة استشارة');
    expect(rows(fixture)[1].textContent).toContain('250.50');
  });

  it('a line added while editing uses the product picker', () => {
    const fixture = setup();

    click(fixture, '[data-testid="add-line"]');

    expect(rows(fixture).length).toBe(3);
    expect(rows(fixture)[2].querySelector('[data-testid="product-stub"]')).not.toBeNull();
  });

  it('saves the whole Draft: kept lines by id with the quantity, new lines by product, removed lines left out', () => {
    const fixture = setup();
    update.mockReturnValue(of(draft));

    click(fixture, '[data-testid="remove-line"]', 0);
    typeInto(quantityInput(fixture, 0), '4.25');
    click(fixture, '[data-testid="add-line"]');
    line(fixture, 1).patchValue({ productId: NEW_PRODUCT });
    typeInto(quantityInput(fixture, 1), '1');
    typeInto(root(fixture).querySelector('[formControlName="notes"]') as HTMLTextAreaElement, '  Pay on receipt  ');
    fixture.detectChanges();
    submit(fixture);

    expect(update).toHaveBeenCalledWith(draft.id, {
      customerId: CUSTOMER,
      dueDate: '2099-07-01',
      notes: 'Pay on receipt',
      discount: null,
      lines: [
        { id: LINE_2, quantity: 4.25 },
        { productId: NEW_PRODUCT, quantity: 1 },
      ],
    });
  });

  // ---- the optional Discount field (spec 006) ----

  const discountInput = (fixture: ComponentFixture<InvoiceFormComponent>) =>
    root(fixture).querySelector('input[data-testid="discount"]') as HTMLInputElement;
  const discounted: Invoice = { ...draft, discount: 20.5, totals: { ...draft.totals, discount: 20.5 } };

  it('shows the saved discount in the Discount field and an empty field when there is none', () => {
    const empty = setup();
    expect(discountInput(empty).value).toBe('');
    TestBed.resetTestingModule();

    const withDiscount = setup(discounted);
    expect(discountInput(withDiscount).value).toBe('20.5');
  });

  it('sends the saved discount back unchanged, because a save describes the whole Draft', () => {
    const fixture = setup(discounted);
    update.mockReturnValue(of(discounted));

    submit(fixture);

    expect(update).toHaveBeenCalledWith(draft.id, expect.objectContaining({ discount: 20.5 }));
  });

  it('clearing the Discount field sends null, which clears the saved discount', () => {
    const fixture = setup(discounted);
    update.mockReturnValue(of(draft));

    typeInto(discountInput(fixture), '');
    fixture.detectChanges();
    submit(fixture);

    expect(update).toHaveBeenCalledWith(draft.id, expect.objectContaining({ discount: null }));
  });

  it('a refusal on the lines and on a line\'s quantity clears on the next save in edit mode', () => {
    const fixture = setup(discounted);
    update.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { lines: ['The invoice total is too large. Reduce the quantities or remove lines.'], 'lines[0].quantity': ['The amount of this line is too large. Reduce the quantity.'] } },
          }),
      ),
    );
    update.mockReturnValueOnce(of(discounted));

    submit(fixture);
    expect(text(fixture)).toContain('The invoice total is too large.');
    expect(text(fixture)).toContain('The amount of this line is too large.');

    submit(fixture);

    expect(update).toHaveBeenCalledTimes(2);
    expect(navigate).toHaveBeenCalled();
  });

  it('a saved discount of 20.10 is shown as 20.1 and sent back as the same number', () => {
    const fixture = setup({ ...draft, discount: 20.1, totals: { ...draft.totals, discount: 20.1 } });
    update.mockReturnValue(of(draft));

    expect(discountInput(fixture).value).toBe('20.1');
    submit(fixture);

    expect(update).toHaveBeenCalledWith(draft.id, expect.objectContaining({ discount: 20.1 }));
  });

  it('a server refusal of the discount lands on the field and the saved values stay', () => {
    const fixture = setup(discounted);
    update.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { discount: ['Discount cannot be more than the total net of the lines.'] } },
          }),
      ),
    );

    click(fixture, '[data-testid="remove-line"]', 0);
    submit(fixture);

    expect(text(fixture)).toContain('Discount cannot be more than the total net of the lines.');
    expect(discountInput(fixture).value).toBe('20.5');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('goes back to the invoice\'s details after saving', () => {
    const fixture = setup();
    update.mockReturnValue(of(draft));

    submit(fixture);

    expect(navigate).toHaveBeenCalledWith(['/invoices', draft.id], { replaceUrl: true });
  });

  it('the last remaining line cannot be removed', () => {
    const fixture = setup();
    click(fixture, '[data-testid="remove-line"]', 0);
    expect(rows(fixture).length).toBe(1);

    click(fixture, '[data-testid="remove-line"]', 0);

    expect(rows(fixture).length).toBe(1);
    expect(root(fixture).querySelector('[data-testid="lines-error"]')?.textContent).toContain('An invoice needs at least one line.');
  });

  it('a 400 for a named line lands on that line, and every value stays', () => {
    const fixture = setup();
    update.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { 'lines[1].quantity': [QUANTITY_MESSAGE] } } })),
    );
    typeInto(quantityInput(fixture, 0), '3');
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain(QUANTITY_MESSAGE);
    expect(quantityInput(fixture, 0).value).toBe('3');
    expect(quantityInput(fixture, 1).value).toBe('1.5');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('a 400 for a line id that is not on the invoice is shown at the top instead of being lost', () => {
    const fixture = setup();
    update.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { 'lines[0].id': ['This line does not belong to this invoice.'] } } })),
    );

    submit(fixture);

    expect(text(fixture)).toContain('This line does not belong to this invoice.');
  });

  it('keeps an inactive customer that is already on the Draft, so saving with the same customer is allowed', () => {
    const fixture = setup({ ...draft, customer: { ...draft.customer, isActive: false } });
    update.mockReturnValue(of(draft));

    submit(fixture);

    expect(update).toHaveBeenCalledWith(draft.id, expect.objectContaining({ customerId: CUSTOMER }));
    expect(text(fixture)).not.toContain('Choose a customer.');
  });

  it('accepts a due date that has passed as long as it is unchanged, and refuses a different past date', () => {
    const fixture = setup({ ...draft, dueDate: '2000-01-01' });
    update.mockReturnValue(of(draft));

    submit(fixture);
    expect(update).toHaveBeenCalledTimes(1);
    expect(update).toHaveBeenCalledWith(draft.id, expect.objectContaining({ dueDate: '2000-01-01' }));

    typeInto(root(fixture).querySelector('[formControlName="dueDate"]') as HTMLInputElement, '2000-01-02');
    fixture.detectChanges();
    submit(fixture);

    expect(text(fixture)).toContain('Due date cannot be in the past.');
    expect(update).toHaveBeenCalledTimes(1);
  });

  it('still refuses a bad quantity on an existing line', () => {
    const fixture = setup();
    typeInto(quantityInput(fixture, 0), '0.001');
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain(QUANTITY_MESSAGE);
    expect(update).not.toHaveBeenCalled();
  });

  it('says "Invoice not found" and shows no form when the invoice does not exist', () => {
    const fixture = setup(new HttpErrorResponse({ status: 404 }));

    expect(text(fixture)).toContain('Invoice not found');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('shows an error with retry when the invoice cannot be loaded', () => {
    const fixture = setup(new HttpErrorResponse({ status: 500 }));

    expect(root(fixture).querySelector('[data-testid="load-error"]')).not.toBeNull();
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('cancels back to the invoice\'s details', () => {
    const fixture = setup();

    expect(root(fixture).querySelector(`a[href="/invoices/${draft.id}"]`)?.textContent).toContain('Cancel');
  });

  // ---- a locked invoice is not edited (spec 007) ----

  const locked = (status: 'Sent' | 'Paid' | 'Cancelled'): Invoice => ({
    ...draft,
    status,
    isEditable: false,
    allowedTransitions: status === 'Sent' ? ['Cancelled'] : [],
  });

  it.each(['Sent', 'Paid', 'Cancelled'] as const)('shows "Only a Draft can be edited" and no form for a %s invoice', (status) => {
    const fixture = setup(locked(status));

    expect(root(fixture).querySelector('[data-testid="locked"]')?.textContent).toContain('Only a Draft can be edited');
    expect(root(fixture).querySelector('form')).toBeNull();
    expect(root(fixture).querySelector('[data-testid="quantity"]')).toBeNull();
    expect(root(fixture).querySelector(`a[href="/invoices/${draft.id}"]`)?.textContent).toContain('Back to invoice');
  });

  it('a Draft that is sent while its form is open: saving shows the message at the top and keeps every typed value', () => {
    const fixture = setup();
    update.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { title: 'Invoice is locked', detail: 'Only a Draft invoice can be edited. This invoice is Sent.' },
          }),
      ),
    );

    typeInto(quantityInput(fixture, 0), '7');
    fixture.detectChanges();
    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="form-error"]')?.textContent).toContain('Only a Draft invoice can be edited. This invoice is Sent.');
    expect(quantityInput(fixture, 0).value).toBe('7');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('names the locked page in its heading and announces it as an alert', () => {
    const fixture = setup(locked('Sent'));

    expect(root(fixture).querySelector('h1')?.textContent).toContain('Invoice cannot be edited');
    expect(root(fixture).querySelector('h1')?.textContent).not.toContain('Edit invoice');
    expect(root(fixture).querySelector('[data-testid="locked"]')?.getAttribute('role')).toBe('alert');
  });

  it('calls a late Sent invoice Overdue on the locked page', () => {
    const fixture = setup({ ...locked('Sent'), isOverdue: true });

    expect(root(fixture).querySelector('[data-testid="locked"]')?.textContent).toContain('This invoice is Overdue.');
  });

  it('after a 409 offers to reload; reloading drops the typed values and shows the locked message for a Sent invoice', () => {
    const fixture = setup();
    update.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { title: 'Invoice is locked', detail: 'Only a Draft invoice can be edited. This invoice is Sent.' } })),
    );
    typeInto(quantityInput(fixture, 0), '7');
    fixture.detectChanges();
    submit(fixture);
    expect(root(fixture).querySelector('[data-testid="reload-latest"]')).not.toBeNull();

    get.mockReturnValue(of(locked('Sent')));
    click(fixture, '[data-testid="reload-latest"]');
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(2);
    expect(root(fixture).querySelector('[data-testid="locked"]')?.textContent).toContain('This invoice is Sent.');
    expect(root(fixture).querySelector('form')).toBeNull();
  });

  it('after a 409 for a Draft that only changed, reloading shows the latest saved values', () => {
    const fixture = setup();
    update.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { title: 'Invoice changed', detail: 'This invoice was changed by someone else.' } })));
    typeInto(quantityInput(fixture, 0), '7');
    fixture.detectChanges();
    submit(fixture);

    get.mockReturnValue(of({ ...draft, lines: [{ ...draft.lines[0], quantity: 5 }, draft.lines[1]] }));
    click(fixture, '[data-testid="reload-latest"]');
    fixture.detectChanges();

    expect(quantityInput(fixture, 0).value).toBe('5');
    expect(root(fixture).querySelector('[data-testid="form-error"]')).toBeNull();
    expect(root(fixture).querySelector('[data-testid="reload-latest"]')).toBeNull();
  });

  it('offers no reload for an ordinary validation refusal (400)', () => {
    const fixture = setup();
    update.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { title: 'Validation', errors: { 'lines[0].quantity': ['Too much.'] } } })));
    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="reload-latest"]')).toBeNull();
  });
});
