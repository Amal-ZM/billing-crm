import { HttpErrorResponse } from '@angular/common/http';
import { Component, forwardRef, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ControlValueAccessor, FormArray, FormGroup, NG_VALUE_ACCESSOR } from '@angular/forms';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
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
const PRODUCT = '44444444-4444-4444-4444-444444444444';
const PRODUCT_2 = '55555555-5555-5555-5555-555555555555';

const saved: Invoice = {
  id: '11111111-1111-1111-1111-111111111111',
  number: 'INV-000001',
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
  remaining: 230,
  acceptsPayment: false,
  payments: [],
  discount: 0,
  totals: { totalNet: 200, discount: 0, netAfterDiscount: 200, totalVat: 30, grandTotal: 230 },
  lines: [
    {
      id: '33333333-3333-3333-3333-333333333333',
      position: 1,
      productId: PRODUCT,
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
  ],
};

const FUTURE = '2099-12-31';
const PAST = '2000-01-01';
const QUANTITY_MESSAGE = 'Quantity must be a number greater than zero with at most two decimal places.';

describe('InvoiceFormComponent (add)', () => {
  let create: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;

  function setup() {
    create = vi.fn();
    TestBed.configureTestingModule({
      imports: [InvoiceFormComponent],
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { create } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    }).overrideComponent(InvoiceFormComponent, {
      remove: { imports: [CustomerPickerComponent, ProductPickerComponent] },
      add: { imports: [StubCustomerPickerComponent, StubProductPickerComponent] },
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(InvoiceFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<InvoiceFormComponent>) => fixture.nativeElement as HTMLElement;
  const text = (fixture: ComponentFixture<InvoiceFormComponent>) => root(fixture).textContent ?? '';
  const form = (fixture: ComponentFixture<InvoiceFormComponent>) => (fixture.componentInstance as unknown as { form: FormGroup }).form;
  const lines = (fixture: ComponentFixture<InvoiceFormComponent>) => form(fixture).get('lines') as FormArray;
  const line = (fixture: ComponentFixture<InvoiceFormComponent>, index: number) => lines(fixture).at(index) as FormGroup;
  const lineRows = (fixture: ComponentFixture<InvoiceFormComponent>) => root(fixture).querySelectorAll('[data-testid="line"]');
  const quantityInput = (fixture: ComponentFixture<InvoiceFormComponent>, index: number) =>
    root(fixture).querySelectorAll('input[data-testid="quantity"]')[index] as HTMLInputElement;

  function typeInto(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function fillValid(fixture: ComponentFixture<InvoiceFormComponent>, quantity = '2') {
    form(fixture).patchValue({ customerId: CUSTOMER });
    line(fixture, 0).patchValue({ productId: PRODUCT });
    typeInto(root(fixture).querySelector('[formControlName="dueDate"]') as HTMLInputElement, FUTURE);
    typeInto(root(fixture).querySelector('[formControlName="notes"]') as HTMLTextAreaElement, '  Net 30  ');
    typeInto(quantityInput(fixture, 0), quantity);
    fixture.detectChanges();
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

  it('starts with one empty line and a heading that says Create invoice', () => {
    const fixture = setup();

    expect(lineRows(fixture).length).toBe(1);
    expect(root(fixture).querySelector('h1')?.textContent).toContain('Create invoice');
  });

  it('adds lines with Add line, up to 100, and then refuses another', () => {
    const fixture = setup();

    for (let i = 0; i < 99; i++) {
      click(fixture, '[data-testid="add-line"]');
    }
    expect(lineRows(fixture).length).toBe(100);

    click(fixture, '[data-testid="add-line"]');

    expect(lineRows(fixture).length).toBe(100);
    expect(text(fixture)).toContain('An invoice can have at most 100 lines.');
    // A hundred lines of Material controls is slow on a busy machine: a generous limit, not a performance claim.
  }, 30000);

  it('removes a line with Remove, but the last line cannot be removed and says a Draft needs at least one line', () => {
    const fixture = setup();
    click(fixture, '[data-testid="add-line"]');
    expect(lineRows(fixture).length).toBe(2);

    click(fixture, '[data-testid="remove-line"]', 0);
    expect(lineRows(fixture).length).toBe(1);

    click(fixture, '[data-testid="remove-line"]', 0);
    expect(lineRows(fixture).length).toBe(1);
    expect(root(fixture).querySelector('[data-testid="lines-error"]')?.textContent).toContain('An invoice needs at least one line.');
  });

  it('shows required-field errors and sends nothing when empty', () => {
    const fixture = setup();

    submit(fixture);

    expect(text(fixture)).toContain('Choose a customer.');
    expect(text(fixture)).toContain('Due date is required.');
    expect(text(fixture)).toContain('Choose a product.');
    expect(text(fixture)).toContain(QUANTITY_MESSAGE);
    expect(create).not.toHaveBeenCalled();
  });

  it.each(['', '0', '0.00', '-1', '0.001', '1.005', 'abc', '1,5'])('refuses the quantity "%s" with the rule in words and sends nothing', (quantity) => {
    const fixture = setup();
    fillValid(fixture, quantity);

    submit(fixture);

    expect(text(fixture)).toContain(QUANTITY_MESSAGE);
    expect(create).not.toHaveBeenCalled();
    expect(quantityInput(fixture, 0).value).toBe(quantity);
  });

  it('refuses a quantity above 999,999.99', () => {
    const fixture = setup();
    fillValid(fixture, '1000000');

    submit(fixture);

    expect(text(fixture)).toContain('Quantity cannot be more than 999,999.99.');
    expect(create).not.toHaveBeenCalled();
  });

  it('refuses a due date in the past and sends nothing', () => {
    const fixture = setup();
    fillValid(fixture);
    typeInto(root(fixture).querySelector('[formControlName="dueDate"]') as HTMLInputElement, PAST);
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain('Due date cannot be in the past.');
    expect(create).not.toHaveBeenCalled();
  });

  it('sets the earliest choosable due date to today', () => {
    const fixture = setup();
    const today = new Date();
    const expected = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

    expect((root(fixture).querySelector('[formControlName="dueDate"]') as HTMLInputElement).min).toBe(expected);
  });

  it('sends the customer, the due date, trimmed notes and each new line as a product and a numeric quantity', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    click(fixture, '[data-testid="add-line"]');
    line(fixture, 1).patchValue({ productId: PRODUCT_2 });
    typeInto(quantityInput(fixture, 1), '1.50');
    fixture.detectChanges();
    submit(fixture);

    expect(create).toHaveBeenCalledWith({
      customerId: CUSTOMER,
      dueDate: FUTURE,
      notes: 'Net 30',
      discount: null,
      lines: [
        { productId: PRODUCT, quantity: 2 },
        { productId: PRODUCT_2, quantity: 1.5 },
      ],
    });
  });

  // ---- the optional Discount field (spec 006) ----

  const discountInput = (fixture: ComponentFixture<InvoiceFormComponent>) =>
    root(fixture).querySelector('input[data-testid="discount"]') as HTMLInputElement;
  const DISCOUNT_RULE = 'Discount must be a number of 0 or more with at most two decimal places.';

  it('has an optional Discount field, left to right and with a decimal keypad, and sends null when it is empty', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    expect(discountInput(fixture)).not.toBeNull();
    expect(discountInput(fixture).getAttribute('dir')).toBe('ltr');
    expect(discountInput(fixture).getAttribute('inputmode')).toBe('decimal');
    fillValid(fixture);
    submit(fixture);

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ discount: null }));
  });

  it('sends a typed discount as the number typed, and treats spaces alone as empty', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    typeInto(discountInput(fixture), ' 20.00 ');
    fixture.detectChanges();
    submit(fixture);
    expect(create).toHaveBeenLastCalledWith(expect.objectContaining({ discount: 20 }));

    typeInto(discountInput(fixture), '   ');
    fixture.detectChanges();
    submit(fixture);
    expect(create).toHaveBeenLastCalledWith(expect.objectContaining({ discount: null }));
  });

  it('accepts 0, 0.5 and 20.10 as a discount', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));
    fillValid(fixture);

    for (const [typed, sent] of [['0', 0], ['0.00', 0], ['0.5', 0.5], ['20.10', 20.1], ['20.100', 20.1], ['9999999999999.99', 9999999999999.99]] as const) {
      typeInto(discountInput(fixture), typed);
      fixture.detectChanges();
      submit(fixture);
      expect(create).toHaveBeenLastCalledWith(expect.objectContaining({ discount: sent }));
    }
  });

  it.each(['-1', '20.001', '9999999999999.991', 'abc', '1,5', '.5', '5.', '٢٠'])('refuses the discount "%s" on the field and sends nothing', (typed) => {
    const fixture = setup();
    fillValid(fixture);
    typeInto(discountInput(fixture), typed);
    fixture.detectChanges();

    submit(fixture);

    expect(create).not.toHaveBeenCalled();
    expect(text(fixture)).toContain(DISCOUNT_RULE);
    expect(quantityInput(fixture, 0).value).toBe('2');
  });

  it('refuses a discount above 9,999,999,999,999.99 and names the limit', () => {
    const fixture = setup();
    fillValid(fixture);
    for (const typed of ['10000000000000', '10000000000000.00']) {
      typeInto(discountInput(fixture), typed);
      fixture.detectChanges();

      submit(fixture);

      expect(create).not.toHaveBeenCalled();
      expect(text(fixture)).toContain('Discount cannot be more than 9,999,999,999,999.99.');
    }
  });

  it('shows a server refusal of the discount on the Discount field and keeps every value', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { discount: ['Discount cannot be more than the total net of the lines.'] } },
          }),
      ),
    );

    fillValid(fixture);
    typeInto(discountInput(fixture), '500');
    fixture.detectChanges();
    submit(fixture);

    expect(text(fixture)).toContain('Discount cannot be more than the total net of the lines.');
    expect(discountInput(fixture).value).toBe('500');
    expect(quantityInput(fixture, 0).value).toBe('2');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows a server refusal of the invoice total above the lines and keeps every value', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { lines: ['The invoice total is too large. Reduce the quantities or remove lines.'] } },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="lines-error"]')?.textContent).toContain('The invoice total is too large.');
    expect(quantityInput(fixture, 0).value).toBe('2');
  });

  it('shows a server refusal of a too-large line on that line\'s quantity', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { 'lines[0].quantity': ['The amount of this line is too large. Reduce the quantity.'] } },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('The amount of this line is too large. Reduce the quantity.');
  });

  it('a corrected discount goes through on the next save because the last refusal\'s message is dropped first', () => {
    const fixture = setup();
    create.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { discount: ['Discount cannot be more than the total net of the lines.'] } } })),
    );
    create.mockReturnValueOnce(of(saved));
    fillValid(fixture);
    typeInto(discountInput(fixture), '500');
    fixture.detectChanges();
    submit(fixture);
    expect(text(fixture)).toContain('Discount cannot be more than the total net of the lines.');

    typeInto(discountInput(fixture), '20');
    fixture.detectChanges();
    submit(fixture);

    expect(create).toHaveBeenLastCalledWith(expect.objectContaining({ discount: 20 }));
    expect(navigate).toHaveBeenCalled();
  });

  it('moves focus to the Discount field when a bad discount is refused, and marks it invalid', async () => {
    const fixture = setup();
    document.body.appendChild(root(fixture));
    fillValid(fixture);
    typeInto(discountInput(fixture), '20.001');
    fixture.detectChanges();

    submit(fixture);
    await new Promise((resolve) => setTimeout(resolve));

    expect(discountInput(fixture).getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(discountInput(fixture));
    root(fixture).remove();
  });

  it('moves focus to the Discount field when the server refuses the discount', async () => {
    const fixture = setup();
    document.body.appendChild(root(fixture));
    create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { discount: ['Discount cannot be more than the total net of the lines.'] } } })),
    );
    fillValid(fixture);
    typeInto(discountInput(fixture), '500');
    fixture.detectChanges();

    submit(fixture);
    await new Promise((resolve) => setTimeout(resolve));

    expect(document.activeElement).toBe(discountInput(fixture));
    root(fixture).remove();
  });

  it('drops the server\'s message about the lines as soon as a quantity is edited, instead of leaving it until the next save', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { lines: ['The invoice total is too large. Reduce the quantities or remove lines.'] } } })),
    );
    fillValid(fixture);
    submit(fixture);
    expect(root(fixture).querySelector('[data-testid="lines-error"]')?.textContent).toContain('The invoice total is too large.');

    typeInto(quantityInput(fixture, 0), '1');
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-testid="lines-error"]')).toBeNull();
  });

  it('shows no calculated total anywhere on the form', () => {
    const fixture = setup();
    fillValid(fixture);
    typeInto(discountInput(fixture), '20');
    fixture.detectChanges();

    const content = text(fixture).toLowerCase();

    expect(content).not.toContain('grand total');
    expect(content).not.toContain('total net');
    expect(content).not.toContain('net after discount');
  });

  it('goes to the new invoice\'s details after saving', () => {
    const fixture = setup();
    create.mockReturnValue(of(saved));

    fillValid(fixture);
    submit(fixture);

    expect(navigate).toHaveBeenCalledWith(['/invoices', saved.id], { replaceUrl: true });
  });

  it('maps a 400 to the customer field and to the named line, and keeps everything that was entered', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              errors: {
                customerId: ['Choose an active customer.'],
                'lines[0].quantity': ['Quantity must be a number greater than zero with at most two decimal places.'],
              },
            },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('Choose an active customer.');
    expect(text(fixture)).toContain(QUANTITY_MESSAGE);
    expect(form(fixture).getRawValue().customerId).toBe(CUSTOMER);
    expect(line(fixture, 0).getRawValue().productId).toBe(PRODUCT);
    expect(quantityInput(fixture, 0).value).toBe('2');
    expect((root(fixture).querySelector('[formControlName="notes"]') as HTMLTextAreaElement).value).toBe('  Net 30  ');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('maps a 400 for an inactive product to that line\'s product control', () => {
    const fixture = setup();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { 'lines[0].productId': ['This product is inactive or does not exist. Choose an active product.'] } },
          }),
      ),
    );

    fillValid(fixture);
    submit(fixture);

    expect(text(fixture)).toContain('This product is inactive or does not exist. Choose an active product.');
    expect(line(fixture, 0).getRawValue().productId).toBe(PRODUCT);
  });

  it('drops the last refusal\'s messages on the next save, so a fixed problem never blocks a retry', () => {
    const fixture = setup();
    create.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { customerId: ['Choose an active customer.'], 'lines[0].productId': ['Inactive.'] } },
          }),
      ),
    );
    create.mockReturnValueOnce(of({ ...saved }));

    fillValid(fixture);
    submit(fixture);
    expect(text(fixture)).toContain('Choose an active customer.');
    expect(form(fixture).invalid).toBe(true);

    // The person changes nothing the API complained about (a stand-in picker never edits) and simply tries again.
    submit(fixture);

    expect(create).toHaveBeenCalledTimes(2);
    expect(navigate).toHaveBeenCalled();
  });

  it('shows a 409 at the top of the form and moves focus to that message', async () => {
    const fixture = setup();
    document.body.appendChild(root(fixture));
    create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { detail: 'This invoice was changed by someone else. Reload it and try again.' } })),
    );

    fillValid(fixture);
    submit(fixture);
    await new Promise((resolve) => setTimeout(resolve));

    const message = root(fixture).querySelector('[data-testid="form-error"]') as HTMLElement;
    expect(message.textContent).toContain('changed by someone else');
    expect(document.activeElement).toBe(message);
    root(fixture).remove();
  });

  it('labels each line as a group, and its Remove button with the line number', () => {
    const fixture = setup();
    click(fixture, '[data-testid="add-line"]');

    expect(Array.from(lineRows(fixture)).map((row) => row.getAttribute('role'))).toEqual(['group', 'group']);
    expect(Array.from(lineRows(fixture)).map((row) => row.getAttribute('aria-label'))).toEqual(['Line 1', 'Line 2']);
    expect(Array.from(root(fixture).querySelectorAll('[data-testid="remove-line"]')).map((b) => b.getAttribute('aria-label'))).toEqual([
      'Remove line 1',
      'Remove line 2',
    ]);
  });

  it('puts focus on Add line after a line is removed', async () => {
    const fixture = setup();
    document.body.appendChild(root(fixture));
    click(fixture, '[data-testid="add-line"]');

    click(fixture, '[data-testid="remove-line"]', 1);
    await new Promise((resolve) => setTimeout(resolve));

    expect(document.activeElement).toBe(root(fixture).querySelector('[data-testid="add-line"]'));
    root(fixture).remove();
  });

  it('keeps saving after the person leaves: the request is not cancelled and its answer is ignored', () => {
    const fixture = setup();
    const pending = new Subject<Invoice>();
    create.mockReturnValue(pending);

    fillValid(fixture);
    submit(fixture);
    fixture.destroy();
    expect(pending.observed).toBe(true);
    pending.next({ ...saved });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows a message at the top for a failure that belongs to no field and keeps the values', () => {
    const fixture = setup();
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    fillValid(fixture);
    submit(fixture);

    expect(root(fixture).querySelector('[data-testid="form-error"]')?.textContent).toContain('Cannot reach the service');
    expect(quantityInput(fixture, 0).value).toBe('2');
  });

  it('disables the save button while saving and ignores a second submit', () => {
    const fixture = setup();
    create.mockReturnValue(new Subject<Invoice>());

    fillValid(fixture);
    submit(fixture);
    submit(fixture);

    expect((root(fixture).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    expect(create).toHaveBeenCalledTimes(1);
  });

  it('limits the notes to 1,000 characters and marks text as automatic and numbers as left-to-right', () => {
    const fixture = setup();

    const notes = root(fixture).querySelector('[formControlName="notes"]') as HTMLTextAreaElement;
    expect(notes.getAttribute('maxlength')).toBe('1000');
    expect(notes.getAttribute('dir')).toBe('auto');
    expect(quantityInput(fixture, 0).getAttribute('dir')).toBe('ltr');
  });

  it('refuses notes over 1,000 characters typed or pasted past the limit', () => {
    const fixture = setup();
    fillValid(fixture);
    form(fixture).patchValue({ notes: 'n'.repeat(1001) });
    fixture.detectChanges();

    submit(fixture);

    expect(text(fixture)).toContain('Notes must be at most 1,000 characters.');
    expect(create).not.toHaveBeenCalled();
  });

  it('shows no total, amount or VAT amount anywhere on the form', () => {
    const fixture = setup();
    fillValid(fixture);

    const content = text(fixture).toLowerCase();

    expect(content).not.toContain('total');
    expect(content).not.toContain('subtotal');
    expect(content).not.toContain('balance');
  });
});
