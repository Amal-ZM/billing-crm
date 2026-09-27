import { HttpErrorResponse } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { applyProblemToForm, clearServerErrors, serverMessage } from '../../core/http/problem-details';
import { InvoiceInput, InvoiceLine, InvoiceLineInput } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { decimalTextProblem, decimalTextToNumber } from '../../core/money/decimal-text';
import { CustomerPickerComponent } from './pickers/customer-picker.component';
import { ProductPickerComponent } from './pickers/product-picker.component';

const MAX_LINES = 100;
const MAX_NOTES = 1000;
const MAX_QUANTITY = 999999.99;
/** The largest amount the system accepts (the API refuses more); at most this many riyals stay exact in a browser number. */
const MAX_DISCOUNT = 9999999999999.99;
const QUANTITY_MESSAGE = 'Quantity must be a number greater than zero with at most two decimal places.';
const DISCOUNT_MESSAGE = 'Discount must be a number of 0 or more with at most two decimal places.';
const DISCOUNT_TOO_BIG_MESSAGE = 'Discount cannot be more than 9,999,999,999,999.99.';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error' | 'locked';

/** Today as `yyyy-MM-dd` in the browser's own calendar; the server applies the Saudi date and has the last word. */
function todayText(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

/** Whitespace alone counts as empty. */
const notBlank: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  typeof control.value === 'string' && control.value.trim().length > 0 ? null : { required: true };

const maxTrimmed =
  (max: number): ValidatorFn =>
  (control: AbstractControl): ValidationErrors | null =>
    typeof control.value === 'string' && control.value.trim().length > max ? { maxlength: { requiredLength: max } } : null;

/** A quantity, checked as text so a third decimal place can never hide behind rounding. */
const quantity: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const problem = decimalTextProblem(control.value as string, { max: MAX_QUANTITY, allowZero: false });
  return problem ? { quantity: problem } : null;
};

/**
 * The optional discount, checked as text like a quantity: empty is fine (no discount), zero is fine, otherwise a number of 0 or
 * more with at most two decimals. Whether it is more than the invoice's net needs the lines' amounts, which only the API
 * calculates, so that check is the server's and its message is shown on this field.
 */
const discountText: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = typeof control.value === 'string' ? control.value : '';
  if (value.trim().length === 0) {
    return null;
  }
  const problem = decimalTextProblem(value, { max: MAX_DISCOUNT, allowZero: true });
  return problem ? { discount: problem } : null;
};

/** A new line needs a product; an existing line (one that has an id) keeps the product it was saved with. */
const productForNewLine: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const hasId = !!control.parent?.get('id')?.value;
  return hasId || (typeof control.value === 'string' && control.value.length > 0) ? null : { required: true };
};

type LineGroup = FormGroup<{
  id: FormControl<string | null>;
  productId: FormControl<string | null>;
  quantity: FormControl<string>;
  saved: FormControl<InvoiceLine | null>;
}>;

/**
 * One form for both creating (`/invoices/new`) and editing (`/invoices/:id/edit`) a Draft. It works on the whole invoice: the
 * customer, due date, notes and every line are sent together. An existing line shows what was saved with it (read-only) and only
 * its quantity can change; a new line is chosen with the product picker.
 */
@Component({
  selector: 'app-invoice-form',
  imports: [
    DecimalPipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RouterLink,
    CustomerPickerComponent,
    ProductPickerComponent,
  ],
  template: `
    <mat-card>
      <mat-card-header>
        <h1 mat-card-title>{{ loadState() === 'locked' ? 'Invoice cannot be edited' : invoiceId ? 'Edit invoice' : 'Create invoice' }}</h1>
      </mat-card-header>
      <mat-card-content>
        @switch (loadState()) {
          @case ('loading') {
            <p role="status" data-testid="form-loading"><mat-spinner diameter="20" aria-hidden="true" /> Loading invoice…</p>
          }
          @case ('notFound') {
            <p role="alert">Invoice not found. It may have been opened from an old link.</p>
            <a mat-button routerLink="/invoices">Back to invoices</a>
          }
          @case ('locked') {
            <!-- Only a Draft can be edited. Anything else (Sent, Overdue, Paid, Cancelled) shows this instead of a form that could only fail. -->
            <div data-testid="locked" role="alert">
              <p>Only a Draft can be edited. This invoice is {{ lockedStatus() }}.</p>
              <a mat-button [routerLink]="['/invoices', invoiceId]">Back to invoice</a>
            </div>
          }
          @case ('error') {
            <p role="alert" data-testid="load-error">The invoice could not be loaded.</p>
            <button mat-button type="button" (click)="load()">Retry</button>
            <a mat-button routerLink="/invoices">Back to invoices</a>
          }
          @default {
            <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
              <div class="field">
                <app-customer-picker
                  formControlName="customerId"
                  [displayName]="customerName()"
                  [invalid]="!!pickerError(form.controls.customerId, 'Choose a customer.')"
                  errorId="customer-error"
                />
                @if (pickerError(form.controls.customerId, 'Choose a customer.'); as message) {
                  <p class="field-error" id="customer-error" role="alert" tabindex="-1">{{ message }}</p>
                }
              </div>

              <mat-form-field appearance="outline">
                <mat-label>Due date</mat-label>
                <input matInput type="date" formControlName="dueDate" dir="ltr" [min]="today" required />
                @if (dueDateError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Notes (optional)</mat-label>
                <textarea matInput formControlName="notes" dir="auto" rows="3" maxlength="1000"></textarea>
                @if (notesError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Discount in SAR (optional)</mat-label>
                <input
                  matInput
                  data-testid="discount"
                  formControlName="discount"
                  inputmode="decimal"
                  dir="ltr"
                  autocomplete="off"
                />
                <mat-hint>Comes off the net, before VAT is added</mat-hint>
                @if (discountError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <h2>Lines</h2>
              @if (linesMessage(); as message) {
                <p class="field-error" role="alert" data-testid="lines-error" tabindex="-1">{{ message }}</p>
              }

              <div formArrayName="lines">
                @for (line of lines.controls; track line; let i = $index) {
                  <div class="line" data-testid="line" role="group" [attr.aria-label]="'Line ' + (i + 1)" [formGroupName]="i">
                    <div class="product">
                      @if (line.controls.saved.value; as saved) {
                        <!-- An existing line: the copy saved when it was added. Only the quantity can change. -->
                        <div class="saved" data-testid="saved-line">
                          <strong dir="auto">{{ saved.productName }}</strong>
                          <span dir="ltr">{{ saved.sku }}</span>
                          <span>{{ saved.unit }}</span>
                          <span dir="ltr">SAR {{ saved.unitPrice | number: '1.2-2' }}</span>
                          <span dir="ltr">{{ saved.vatPercent | number: '1.0-2' }}%</span>
                        </div>
                        @if (lineIdError(line); as message) {
                          <p class="field-error" role="alert" tabindex="-1">{{ message }}</p>
                        }
                      } @else {
                        <app-product-picker
                          formControlName="productId"
                          [invalid]="!!pickerError(line.controls.productId, 'Choose a product.')"
                          [errorId]="'product-error-' + i"
                        />
                        @if (pickerError(line.controls.productId, 'Choose a product.'); as message) {
                          <p class="field-error" [id]="'product-error-' + i" role="alert" tabindex="-1">{{ message }}</p>
                        }
                      }
                    </div>

                    <mat-form-field appearance="outline" class="quantity">
                      <mat-label>Quantity</mat-label>
                      <input matInput data-testid="quantity" formControlName="quantity" inputmode="decimal" dir="ltr" autocomplete="off" required />
                      @if (quantityError(line.controls.quantity); as message) {
                        <mat-error>{{ message }}</mat-error>
                      }
                    </mat-form-field>

                    <button mat-button type="button" data-testid="remove-line" [attr.aria-label]="'Remove line ' + (i + 1)" (click)="removeLine(i)">
                      Remove
                    </button>
                  </div>
                }
              </div>

              <button mat-stroked-button type="button" data-testid="add-line" (click)="addLine()">Add line</button>

              @if (formError(); as message) {
                <p class="error" role="alert" data-testid="form-error" tabindex="-1">{{ message }}</p>
                @if (conflict()) {
                  <!-- Someone else changed or sent this invoice: what was typed cannot be saved on top of it. -->
                  <button mat-stroked-button type="button" data-testid="reload-latest" (click)="reloadLatest()">
                    Discard my changes and reload the invoice
                  </button>
                }
              }

              <div class="actions">
                <button mat-flat-button type="submit" [disabled]="saving()">
                  @if (saving()) {
                    <mat-spinner diameter="18" aria-hidden="true" />
                  }
                  Save
                </button>
                <a mat-button [routerLink]="invoiceId ? ['/invoices', invoiceId] : ['/invoices']">Cancel</a>
              </div>
            </form>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 900px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding-top: 16px;
    }
    .line {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: flex-start;
    }
    .product {
      flex: 1 1 320px;
    }
    .saved {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: baseline;
      padding: 12px 0;
    }
    .quantity {
      width: 160px;
    }
    .field-error,
    .error {
      color: var(--mat-sys-error);
      margin: 0 0 8px;
    }
    .actions {
      display: flex;
      gap: 8px;
      margin-top: 16px;
    }
    button mat-spinner {
      display: inline-block;
      margin-right: 8px;
    }
  `,
})
export class InvoiceFormComponent implements OnInit {
  private readonly invoices = inject(InvoiceService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** True once the person has left the page, so a late answer to a save does not act on a form that is gone. */
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));

    // A message about the lines (too large a total, too many lines) is about what was there when it was shown: as soon as a line
    // is edited it may no longer be true, so it goes away instead of staying until the next save. (Adding or removing a line
    // that is refused sets its message before anything changes, so those are not affected.)
    this.form.controls.lines.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.linesMessage.set(null));
  }

  /** Set when editing (`/invoices/:id/edit`); null when creating. */
  protected readonly invoiceId = this.route.snapshot.paramMap.get('id');

  /** The earliest date the date box offers. */
  protected readonly today = todayText();

  // When editing, show "loading" from the first frame so an empty form never flashes.
  protected readonly loadState = signal<LoadState>(this.invoiceId ? 'loading' : 'ready');
  /** How to name the status of an invoice that cannot be edited (Sent, Overdue, Paid or Cancelled). */
  protected readonly lockedStatus = signal('');
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  /** True after a 409 on save: the invoice changed under the person, so the only way on is to reload it. */
  protected readonly conflict = signal(false);
  /** Said when an action on the lines is refused (the last line cannot go; 100 is the most). */
  protected readonly linesMessage = signal<string | null>(null);
  /** The current customer's name, for the picker to show when editing (the customer may have become inactive since). */
  protected readonly customerName = signal<string | null>(null);

  /** The due date the Draft was saved with: a date that has passed is still accepted as long as it is not changed. */
  private originalDueDate: string | null = null;

  protected readonly form = new FormGroup({
    customerId: new FormControl<string | null>(null, {
      validators: (control) => (control.value ? null : { required: true }),
    }),
    dueDate: new FormControl('', { nonNullable: true, validators: [notBlank, (control) => this.dueDateProblem(control)] }),
    notes: new FormControl('', { nonNullable: true, validators: [maxTrimmed(MAX_NOTES)] }),
    /** Held as text, like a quantity; empty means no discount. */
    discount: new FormControl('', { nonNullable: true, validators: [discountText] }),
    lines: new FormArray<LineGroup>([this.newLine()]),
  });

  protected get lines(): FormArray<LineGroup> {
    return this.form.controls.lines;
  }

  ngOnInit(): void {
    if (this.invoiceId) {
      this.load();
    }
  }

  /** Editing only: fetch the invoice and fill the form. */
  protected load(): void {
    if (!this.invoiceId) {
      return;
    }

    this.loadState.set('loading');
    this.invoices
      .get(this.invoiceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => {
          // Only a Draft can be edited (the API says so with isEditable). Anything else shows a message instead of the form.
          if (!invoice.isEditable) {
            this.lockedStatus.set(invoice.isOverdue ? 'Overdue' : invoice.status);
            this.loadState.set('locked');
            return;
          }

          this.originalDueDate = invoice.dueDate;
          this.customerName.set(invoice.customer.name);
          this.lines.clear();
          for (const saved of invoice.lines) {
            this.lines.push(this.newLine(saved));
          }
          this.form.reset(
            {
              customerId: invoice.customer.id,
              dueDate: invoice.dueDate,
              notes: invoice.notes ?? '',
              // No discount shows an empty box (0 and empty mean the same thing).
              discount: invoice.discount !== 0 ? String(invoice.discount) : '',
              lines: invoice.lines.map((saved) => ({
                id: saved.id,
                productId: saved.productId,
                quantity: String(saved.quantity),
                saved,
              })),
            },
            { emitEvent: true },
          );
          this.loadState.set('ready');
        },
        error: (error: unknown) =>
          this.loadState.set(error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'error'),
      });
  }

  private newLine(saved: InvoiceLine | null = null): LineGroup {
    return new FormGroup({
      id: new FormControl<string | null>(saved?.id ?? null),
      productId: new FormControl<string | null>(saved?.productId ?? null, { validators: [productForNewLine] }),
      quantity: new FormControl(saved ? String(saved.quantity) : '', { nonNullable: true, validators: [quantity] }),
      saved: new FormControl<InvoiceLine | null>(saved),
    });
  }

  private dueDateProblem(control: AbstractControl): ValidationErrors | null {
    const value = typeof control.value === 'string' ? control.value : '';
    if (value.length === 0) {
      return null;
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || Number.isNaN(Date.parse(value))) {
      return { dateFormat: true };
    }
    return value < todayText() && value !== this.originalDueDate ? { past: true } : null;
  }

  protected addLine(): void {
    if (this.lines.length >= MAX_LINES) {
      this.linesMessage.set(`An invoice can have at most ${MAX_LINES} lines.`);
      return;
    }
    this.linesMessage.set(null);
    this.lines.push(this.newLine());
  }

  protected removeLine(index: number): void {
    if (this.lines.length <= 1) {
      this.linesMessage.set('An invoice needs at least one line.');
      return;
    }
    this.linesMessage.set(null);
    this.lines.removeAt(index);
    // The Remove button that had focus is gone; put focus on Add line so keyboard and screen-reader users are not thrown to the top.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('[data-testid="add-line"]')?.focus());
  }

  /** The message for a picker control (customer or product): the API's own first, then "choose one" when it is empty and touched. */
  protected pickerError(control: AbstractControl, required: string): string | null {
    const fromServer = serverMessage(control);
    if (fromServer) {
      return fromServer;
    }
    return control.touched && control.hasError('required') ? required : null;
  }

  /** A refusal on a saved line, which shows no inputs of its own but the quantity: its id (a line not on this invoice) or its product. */
  protected lineIdError(line: LineGroup): string | null {
    return serverMessage(line.controls.id) ?? serverMessage(line.controls.productId);
  }

  protected dueDateError(): string | null {
    const control = this.form.controls.dueDate;
    return (
      serverMessage(control) ??
      (control.hasError('required')
        ? 'Due date is required.'
        : control.hasError('dateFormat')
          ? 'Due date must be a valid date.'
          : control.hasError('past')
            ? 'Due date cannot be in the past.'
            : null)
    );
  }

  protected notesError(): string | null {
    const control = this.form.controls.notes;
    return serverMessage(control) ?? (control.hasError('maxlength') ? 'Notes must be at most 1,000 characters.' : null);
  }

  protected discountError(): string | null {
    const control = this.form.controls.discount;
    const fromServer = serverMessage(control);
    if (fromServer) {
      return fromServer;
    }
    const problem = control.errors?.['discount'];
    if (!problem) {
      return null;
    }
    return problem === 'tooBig' ? DISCOUNT_TOO_BIG_MESSAGE : DISCOUNT_MESSAGE;
  }

  protected quantityError(control: AbstractControl): string | null {
    const fromServer = serverMessage(control);
    if (fromServer) {
      return fromServer;
    }
    const problem = control.errors?.['quantity'];
    if (!problem) {
      return null;
    }
    return problem === 'tooBig' ? 'Quantity cannot be more than 999,999.99.' : QUANTITY_MESSAGE;
  }

  protected submit(): void {
    if (this.saving()) {
      return;
    }

    // Messages from the last refusal are dropped first: the form is judged on what is in it now, so a fixed problem cannot block a retry.
    this.formError.set(null);
    this.conflict.set(false);
    this.linesMessage.set(null);
    clearServerErrors(this.form);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    const value = this.form.getRawValue();
    const input: InvoiceInput = {
      customerId: value.customerId as string,
      dueDate: value.dueDate,
      notes: value.notes.trim(),
      // Sent exactly as typed; empty means none, and (a save describes the whole Draft) clears a saved discount.
      discount: value.discount.trim() === '' ? null : decimalTextToNumber(value.discount),
      lines: value.lines.map(
        (line): InvoiceLineInput =>
          line.id
            ? { id: line.id, quantity: decimalTextToNumber(line.quantity) }
            : { productId: line.productId as string, quantity: decimalTextToNumber(line.quantity) },
      ),
    };

    this.saving.set(true);
    const request = this.invoiceId ? this.invoices.update(this.invoiceId, input) : this.invoices.create(input);
    // The request is NOT cancelled if the person leaves while it runs (cancelling on the client does not stop the server, so the
    // invoice could be saved while they believe it was not). The result of a request that outlived the page is simply not shown.
    request.subscribe({
      next: (invoice) => {
        if (this.destroyed) {
          return;
        }
        this.saving.set(false);
        // replaceUrl: Back from the details page should not return to a form that has already been saved.
        void this.router.navigate(['/invoices', invoice.id], { replaceUrl: true });
      },
      error: (error: unknown) => {
        if (this.destroyed) {
          return;
        }
        // Everything the person entered stays in the form; each message goes on the field or line it belongs to.
        this.saving.set(false);
        this.formError.set(applyProblemToForm(this.form, error, { subject: 'invoice' }));
        this.conflict.set(this.invoiceId !== null && error instanceof HttpErrorResponse && error.status === 409);
        this.linesMessage.set(serverMessage(this.form.controls.lines));
        this.focusFirstInvalid();
      },
    });
  }

  /** After a 409: throw away what was typed and load the invoice as it is now (a Sent one then shows the locked message). */
  protected reloadLatest(): void {
    this.conflict.set(false);
    this.formError.set(null);
    this.load();
  }

  /**
   * After a refusal, move focus to the first control in error so keyboard and screen-reader users land on it. When no field is in
   * error (a 409, a network failure) the message at the top of the form or above the lines takes the focus instead.
   */
  private focusFirstInvalid(): void {
    setTimeout(() =>
      this.host.nativeElement
        .querySelector<HTMLElement>(
          'input[aria-invalid="true"], textarea[aria-invalid="true"], select[aria-invalid="true"], .field-error[tabindex="-1"], [data-testid="form-error"]',
        )
        ?.focus(),
    );
  }
}
