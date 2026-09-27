import { HttpErrorResponse } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { ChangeDetectorRef, Component, DestroyRef, ElementRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
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
import { Invoice, PaymentInput, PaymentMethod } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { decimalTextToNumber, paymentAmountProblem } from '../../core/money/decimal-text';

const MAX_NOTE = 1000;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error' | 'notPayable';

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

/** An amount, checked as text so a third decimal place can never hide behind rounding. */
const amountText: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const message = paymentAmountProblem(control.value as string);
  return message ? { amount: message } : null;
};

/**
 * The form that records a payment against an invoice (`/invoices/:id/payments/new`), built like the other forms: reactive, the
 * amount held as text and judged as text, each message from the API on its own field, everything typed kept after a refusal. The
 * date is prefilled with the browser's date; the server judges it (it uses the Saudi date) and has the last word. Nothing here
 * works out paid or remaining: what remains is shown as the API returned it.
 */
@Component({
  selector: 'app-payment-form',
  imports: [
    DecimalPipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RouterLink,
  ],
  template: `
    <mat-card>
      <mat-card-header>
        <h1 mat-card-title tabindex="-1">Record payment</h1>
      </mat-card-header>
      <mat-card-content>
        @switch (loadState()) {
          @case ('loading') {
            <p role="status" data-testid="form-loading"><mat-spinner diameter="20" aria-hidden="true" /> Loading invoice…</p>
          }
          @case ('notFound') {
            <p role="alert" data-testid="not-found">Invoice not found. It may have been opened from an old link.</p>
            <a mat-button routerLink="/invoices">Back to invoices</a>
          }
          @case ('notPayable') {
            <!-- The API says whether the invoice takes a payment (acceptsPayment); anything else shows this instead of a form that could only fail. -->
            <div data-testid="not-payable" role="alert">
              <p>
                This invoice cannot take a payment. It is {{ shownStatus() }}.
                @if (nothingRemains()) {
                  Nothing remains to be paid on it.
                }
              </p>
              <a mat-button [routerLink]="['/invoices', invoiceId]">Back to invoice</a>
            </div>
          }
          @case ('error') {
            <p role="alert" data-testid="load-error">The invoice could not be loaded.</p>
            <button mat-button type="button" (click)="load()">Retry</button>
            <a mat-button routerLink="/invoices">Back to invoices</a>
          }
          @default {
            @if (invoice(); as current) {
              <p data-testid="summary">
                Invoice <strong dir="ltr">{{ current.number }}</strong> for <strong dir="auto">{{ current.customer.name }}</strong>.
                Remaining: <strong dir="ltr">SAR {{ current.remaining | number: '1.2-2' }}</strong>
              </p>
            }

            <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
              <mat-form-field appearance="outline">
                <mat-label>Date received</mat-label>
                <input matInput data-testid="date" type="date" formControlName="date" dir="ltr" required />
                <mat-hint>Filled in from this computer's date; the system checks it against the Saudi date.</mat-hint>
                @if (dateError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Amount in SAR</mat-label>
                <input
                  matInput
                  data-testid="amount"
                  formControlName="amount"
                  inputmode="decimal"
                  dir="ltr"
                  autocomplete="off"
                  required
                />
                <mat-hint>Digits and a decimal point only, for example 100.50</mat-hint>
                @if (amountError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Method</mat-label>
                <select matNativeControl data-testid="method" formControlName="method" required>
                  <option value="" disabled>Choose a method</option>
                  @for (method of methods; track method) {
                    <option [value]="method">{{ method }}</option>
                  }
                </select>
                @if (methodError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Note (optional)</mat-label>
                <textarea matInput data-testid="note" formControlName="note" dir="auto" rows="3"></textarea>
                @if (noteError(); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              @if (formError(); as message) {
                <p class="error" role="alert" data-testid="form-error" tabindex="-1">{{ message }}</p>
                @if (conflict()) {
                  <!-- The invoice changed under the person (another payment, a cancel): what was typed cannot be trusted any more. -->
                  <button mat-stroked-button type="button" data-testid="reload-latest" (click)="reloadLatest()">
                    Discard my changes and reload the invoice
                  </button>
                }
                @if (uncertain()) {
                  <!-- The answer was lost: the payment may or may not have been recorded, and payments cannot be undone. Nothing more can be
                       saved until the person has looked at the invoice again; what was typed stays. -->
                  <button mat-stroked-button type="button" data-testid="check-again" (click)="checkAgain()">
                    Check the invoice again
                  </button>
                }
              }

              <div class="actions">
                <button mat-flat-button type="submit" [disabled]="saving() || uncertain()" [attr.aria-busy]="saving()">
                  @if (saving()) {
                    <mat-spinner diameter="18" aria-hidden="true" />
                    Saving…
                  } @else {
                    Save
                  }
                </button>
                @if (!saving()) {
                  <a mat-button data-testid="back" [routerLink]="['/invoices', invoiceId]">Back to invoice</a>
                }
              </div>
            </form>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 600px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding-top: 16px;
    }
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
export class PaymentFormComponent implements OnInit {
  private readonly invoices = inject(InvoiceService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly changeDetector = inject(ChangeDetectorRef);

  /** Numbers the requests to load the invoice, so a slow older answer can never overwrite a newer one. */
  private loadRequest = 0;

  /** True once the person has left the page, so a late answer to a save does not act on a form that is gone. */
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));
  }

  /** The invoice being paid, from the route. */
  protected readonly invoiceId = this.route.snapshot.paramMap.get('id') ?? '';

  /** Exactly the methods the API accepts, spelled the way it wants them. */
  protected readonly methods: readonly PaymentMethod[] = ['Cash', 'Bank', 'Other'];

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly invoice = signal<Invoice | null>(null);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  /** True after a 409 on save: the invoice changed under the person, so the only way on is to reload it. */
  protected readonly conflict = signal(false);
  /**
   * True after a save whose answer was lost (no reply, or a server failure): the payment may already have been recorded, and a payment
   * cannot be undone, so Save stays off until the invoice has been checked again (a retry could record it twice).
   */
  protected readonly uncertain = signal(false);
  /** How to name the status of an invoice that cannot take a payment (Overdue for a late Sent one, like the other screens). */
  protected readonly shownStatus = signal('');
  /** A Sent invoice that takes no payment because nothing is left to pay: the message says so instead of only naming the status. */
  protected readonly nothingRemains = signal(false);

  protected readonly form = new FormGroup({
    date: new FormControl(todayText(), { nonNullable: true, validators: [notBlank] }),
    /** Held as text, like a quantity; the API judges it again. */
    amount: new FormControl('', { nonNullable: true, validators: [amountText] }),
    method: new FormControl('', { nonNullable: true, validators: [notBlank] }),
    note: new FormControl('', { nonNullable: true, validators: [maxTrimmed(MAX_NOTE)] }),
  });

  ngOnInit(): void {
    this.load();
  }

  /** Fetch the invoice, to say which one is being paid and what remains. */
  protected load(): void {
    const request = ++this.loadRequest;
    this.loadState.set('loading');
    this.invoices
      .get(this.invoiceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => request === this.loadRequest && this.show(invoice),
        error: (error: unknown) => {
          if (request === this.loadRequest) {
            this.loadState.set(error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'error');
          }
        },
      });
  }

  /**
   * After a refusal, fetch the invoice again without touching the form, so the figure at the top matches what the server now says
   * (another payment may have gone in) and a form for an invoice that can no longer take a payment gives way to the message. A
   * failure here is ignored: the refusal that is already showing is the message that matters.
   */
  private refreshQuietly(): void {
    const request = ++this.loadRequest;
    this.invoices
      .get(this.invoiceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => request === this.loadRequest && this.show(invoice),
        error: () => undefined,
      });
  }

  private show(invoice: Invoice): void {
    this.invoice.set(invoice);
    // The API decides whether a payment is possible (acceptsPayment); the browser never works it out from the status. The one
    // extra word is for a Sent invoice with nothing left to pay, where "It is Sent" alone would not say why.
    if (!invoice.acceptsPayment) {
      this.shownStatus.set(invoice.isOverdue ? 'Overdue' : invoice.status);
      this.nothingRemains.set(invoice.status === 'Sent' && invoice.remaining === 0);
      this.loadState.set('notPayable');
      return;
    }
    this.loadState.set('ready');
  }

  protected dateError(): string | null {
    const control = this.form.controls.date;
    return serverMessage(control) ?? (control.touched && control.hasError('required') ? 'Date is required.' : null);
  }

  protected amountError(): string | null {
    const control = this.form.controls.amount;
    const fromServer = serverMessage(control);
    if (fromServer) {
      return fromServer;
    }
    return control.touched ? ((control.errors?.['amount'] as string | undefined) ?? null) : null;
  }

  protected methodError(): string | null {
    const control = this.form.controls.method;
    return serverMessage(control) ?? (control.touched && control.hasError('required') ? 'Choose a payment method.' : null);
  }

  protected noteError(): string | null {
    const control = this.form.controls.note;
    return serverMessage(control) ?? (control.hasError('maxlength') ? 'Note must be at most 1,000 characters.' : null);
  }

  protected submit(): void {
    if (this.saving() || this.uncertain()) {
      return;
    }

    // Messages from the last refusal are dropped first: the form is judged on what is in it now, so a fixed problem cannot block a retry.
    this.formError.set(null);
    this.conflict.set(false);
    clearServerErrors(this.form);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    const value = this.form.getRawValue();
    const input: PaymentInput = {
      date: value.date,
      // Sent exactly as typed (a number), never re-rounded.
      amount: decimalTextToNumber(value.amount),
      method: value.method as PaymentMethod,
      note: value.note.trim(),
    };

    this.saving.set(true);
    // The request is NOT cancelled if the person leaves while it runs (cancelling on the client does not stop the server, so the
    // payment could be recorded while they believe it was not). The result of a request that outlived the page is not shown.
    this.invoices.recordPayment(this.invoiceId, input).subscribe({
      next: () => {
        if (this.destroyed) {
          return;
        }
        this.saving.set(false);
        // replaceUrl: Back from the details page should not return to a form that has already been saved.
        void this.router.navigate(['/invoices', this.invoiceId], { replaceUrl: true });
      },
      error: (error: unknown) => {
        if (this.destroyed) {
          return;
        }
        // Everything the person entered stays in the form; each message goes on the field it belongs to.
        this.saving.set(false);
        const status = error instanceof HttpErrorResponse ? error.status : 0;

        if (status === 0 || status >= 500) {
          // No answer, or a server failure: the payment may have been recorded before the answer was lost, and a payment cannot be
          // undone, so a second Save could record it twice. Look at the invoice again first (what was typed stays).
          this.uncertain.set(true);
          this.formError.set(
            (status === 0 ? 'Cannot reach the service.' : 'The service failed.') +
              ' We could not confirm whether the payment was recorded. Check the invoice again before saving, so it is not recorded twice.',
          );
          this.focusFirstInvalid();
          return;
        }

        // A 409 has no field of its own here (there is no "email" control, so the API's message comes back as the top message).
        this.formError.set(applyProblemToForm(this.form, error, { subject: 'payment' }));
        this.conflict.set(status === 409);
        // The invoice may have changed under the person (another payment, a cancel): fetch it again quietly so the figure at the top
        // is current and a form for an invoice that can no longer take a payment gives way to the message.
        if (status === 409 || (status === 400 && this.form.controls.amount.errors?.['server'])) {
          this.refreshQuietly();
        }
        this.focusFirstInvalid();
      },
    });
  }

  /**
   * After a save whose answer was lost: fetch the invoice again (what was typed stays) so the person can see whether the payment is
   * already there, and only then allow another Save. If the invoice can no longer take a payment, the message takes the form's place.
   */
  protected checkAgain(): void {
    this.uncertain.set(false);
    this.formError.set(null);
    this.load();
  }

  /** After a 409: throw away what was typed and load the invoice as it is now (it may no longer take a payment at all). */
  protected reloadLatest(): void {
    this.conflict.set(false);
    this.formError.set(null);
    clearServerErrors(this.form);
    this.form.reset({ date: todayText(), amount: '', method: '', note: '' });
    this.load();
  }

  /** After a refusal, move focus to the first control in error (or the message at the top) so keyboard and screen-reader users land on it. */
  private focusFirstInvalid(): void {
    // The invalid styling and the messages are drawn by the next change detection; run it now, so the control that is invalid (or the
    // message at the top) already exists when it is looked for, whatever scheduled the call.
    this.changeDetector.detectChanges();
    this.host.nativeElement
      .querySelector<HTMLElement>('input[aria-invalid="true"], select[aria-invalid="true"], textarea[aria-invalid="true"], [data-testid="form-error"]')
      ?.focus();
  }
}
