import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problemDetail } from '../../core/http/problem-details';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';

type DetailState =
  | { kind: 'loading' }
  | { kind: 'loaded'; invoice: Invoice }
  | { kind: 'notFound' }
  | { kind: 'error'; detail: string | null };

@Component({
  selector: 'app-invoice-detail',
  imports: [DatePipe, DecimalPipe, MatButtonModule, MatCardModule, MatProgressSpinnerModule, MatTableModule, RouterLink],
  template: `
    @let current = state();
    @if (current.kind === 'loading') {
      <p data-testid="detail-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading invoice…</p>
    } @else if (current.kind === 'notFound') {
      <mat-card>
        <mat-card-header><h1 mat-card-title>Invoice not found</h1></mat-card-header>
        <mat-card-content><p role="alert">No invoice exists with this address. It may have been opened from an old link.</p></mat-card-content>
      </mat-card>
    } @else if (current.kind === 'error') {
      <mat-card>
        <mat-card-content>
          <p role="alert" data-testid="detail-error">
            The invoice could not be loaded.
            @if (current.detail; as detail) {
              {{ detail }}
            } @else {
              Try again.
            }
          </p>
        </mat-card-content>
        <mat-card-actions>
          <button mat-button type="button" data-testid="retry" (click)="load()">Retry</button>
        </mat-card-actions>
      </mat-card>
    } @else {
      @let invoice = current.invoice;
      <mat-card>
        <mat-card-header>
          <h1 mat-card-title data-testid="number" dir="ltr" tabindex="-1">{{ invoice.number }}</h1>
          <mat-card-subtitle>
            <!-- Overdue is not a stored status: the API says a Sent invoice is overdue and the screen shows that word. -->
            <span class="badge" data-testid="status-badge" [class.overdue]="invoice.isOverdue" [class.paid]="invoice.status === 'Paid'">{{ invoice.isOverdue ? 'Overdue' : invoice.status }}</span>
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <dl>
            <dt>Customer</dt>
            <dd>
              <span dir="auto" data-testid="customer">{{ invoice.customer.name }}</span>
              @if (!invoice.customer.isActive) {
                <span class="badge inactive" data-testid="customer-inactive">Inactive</span>
              }
            </dd>
            <dt>Due date</dt>
            <dd data-testid="due-date">{{ invoice.dueDate | date: 'mediumDate' }}</dd>
            <dt>Notes</dt>
            <dd dir="auto" class="notes" data-testid="notes">{{ invoice.notes ?? '—' }}</dd>
            <dt>Created</dt>
            <dd data-testid="created-at">{{ invoice.createdAtUtc | date: 'medium' }}</dd>
            <dt>Last changed</dt>
            <dd data-testid="updated-at">{{ invoice.updatedAtUtc | date: 'medium' }}</dd>
            @if (invoice.sentAtUtc) {
              <dt>Sent</dt>
              <dd data-testid="sent-at">{{ invoice.sentAtUtc | date: 'medium' }}</dd>
            }
            @if (invoice.cancelledAtUtc) {
              <dt>Cancelled</dt>
              <dd data-testid="cancelled-at">{{ invoice.cancelledAtUtc | date: 'medium' }}</dd>
            }
          </dl>

          <h2>Lines</h2>
          <!-- Each price and percent is the copy saved when the line was added. Net, discount share and VAT are the amounts the API calculated from it: shown as given, never calculated here. -->
          <div class="table-scroll">
          <table mat-table [dataSource]="invoice.lines" aria-label="Invoice lines">
            <ng-container matColumnDef="product">
              <th mat-header-cell *matHeaderCellDef>Product</th>
              <td mat-cell *matCellDef="let line" dir="auto">{{ line.productName }}</td>
            </ng-container>
            <ng-container matColumnDef="sku">
              <th mat-header-cell *matHeaderCellDef>SKU</th>
              <td mat-cell *matCellDef="let line" dir="ltr">{{ line.sku }}</td>
            </ng-container>
            <ng-container matColumnDef="unit">
              <th mat-header-cell *matHeaderCellDef>Unit</th>
              <td mat-cell *matCellDef="let line">{{ line.unit }}</td>
            </ng-container>
            <ng-container matColumnDef="unitPrice">
              <th mat-header-cell *matHeaderCellDef>Unit price</th>
              <td mat-cell *matCellDef="let line" dir="ltr">SAR {{ line.unitPrice | number: '1.2-2' }}</td>
            </ng-container>
            <ng-container matColumnDef="vatPercent">
              <th mat-header-cell *matHeaderCellDef>VAT</th>
              <td mat-cell *matCellDef="let line" dir="ltr">{{ line.vatPercent | number: '1.0-2' }}%</td>
            </ng-container>
            <ng-container matColumnDef="quantity">
              <th mat-header-cell *matHeaderCellDef>Quantity</th>
              <td mat-cell *matCellDef="let line" dir="ltr">{{ line.quantity | number: '1.2-2' }}</td>
            </ng-container>
            <ng-container matColumnDef="net">
              <th mat-header-cell *matHeaderCellDef class="amount">Net</th>
              <td mat-cell *matCellDef="let line" dir="ltr" class="amount">SAR {{ line.net | number: '1.2-2' }}</td>
            </ng-container>
            <ng-container matColumnDef="discountShare">
              <th mat-header-cell *matHeaderCellDef class="amount">Discount</th>
              <td mat-cell *matCellDef="let line" dir="ltr" class="amount">SAR {{ line.discountShare | number: '1.2-2' }}</td>
            </ng-container>
            <ng-container matColumnDef="vat">
              <th mat-header-cell *matHeaderCellDef class="amount">VAT amount</th>
              <td mat-cell *matCellDef="let line" dir="ltr" class="amount">SAR {{ line.vat | number: '1.2-2' }}</td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="columns()"></tr>
            <tr mat-row data-testid="line-row" *matRowDef="let row; columns: columns()"></tr>
          </table>
          </div>

          <!-- What the invoice comes to, as calculated by the API. The discount rows appear only when there is a discount. -->
          <section aria-labelledby="totals-heading">
          <h2 id="totals-heading">Totals</h2>
          <dl class="totals" data-testid="totals">
            <dt>Total net</dt>
            <dd dir="ltr" data-testid="total-net">SAR {{ invoice.totals.totalNet | number: '1.2-2' }}</dd>
            @if (invoice.totals.discount !== 0) {
              <dt>Discount</dt>
              <dd dir="ltr" data-testid="discount">SAR {{ invoice.totals.discount | number: '1.2-2' }}</dd>
              <dt>Net after discount</dt>
              <dd dir="ltr" data-testid="net-after-discount">SAR {{ invoice.totals.netAfterDiscount | number: '1.2-2' }}</dd>
            }
            <dt>Total VAT</dt>
            <dd dir="ltr" data-testid="total-vat">SAR {{ invoice.totals.totalVat | number: '1.2-2' }}</dd>
            <dt class="grand">Grand total</dt>
            <dd dir="ltr" class="grand" data-testid="grand-total">SAR {{ invoice.totals.grandTotal | number: '1.2-2' }}</dd>
          </dl>
          </section>

          <!-- What has been paid, as the API worked it out (paid = the sum of the payments, remaining = grand total minus paid): shown as
               given, never calculated here. Only a Sent or Paid invoice can have payments, so a Draft or Cancelled one shows none. -->
          @if (invoice.status === 'Sent' || invoice.status === 'Paid') {
            <section aria-labelledby="payments-heading" data-testid="payments-section">
              <h2 id="payments-heading">Payments</h2>
              <dl class="totals" data-testid="balance">
                <dt>Paid</dt>
                <dd dir="ltr" data-testid="paid">SAR {{ invoice.paid | number: '1.2-2' }}</dd>
                <dt class="grand">Remaining</dt>
                <dd dir="ltr" class="grand" data-testid="remaining">SAR {{ invoice.remaining | number: '1.2-2' }}</dd>
              </dl>
              @if (invoice.payments.length === 0) {
                <p data-testid="no-payments">No payments yet.</p>
              } @else {
                <div class="table-scroll">
                  <table mat-table [dataSource]="invoice.payments" aria-label="Payments" data-testid="payments">
                    <ng-container matColumnDef="date">
                      <th mat-header-cell *matHeaderCellDef>Date</th>
                      <td mat-cell *matCellDef="let payment" dir="ltr">{{ payment.date | date: 'mediumDate' }}</td>
                    </ng-container>
                    <ng-container matColumnDef="amount">
                      <th mat-header-cell *matHeaderCellDef class="amount">Amount</th>
                      <td mat-cell *matCellDef="let payment" dir="ltr" class="amount">SAR {{ payment.amount | number: '1.2-2' }}</td>
                    </ng-container>
                    <ng-container matColumnDef="method">
                      <th mat-header-cell *matHeaderCellDef>Method</th>
                      <td mat-cell *matCellDef="let payment">{{ payment.method }}</td>
                    </ng-container>
                    <ng-container matColumnDef="note">
                      <th mat-header-cell *matHeaderCellDef>Note</th>
                      <td mat-cell *matCellDef="let payment" dir="auto" class="notes" data-testid="payment-note">{{ payment.note ?? '—' }}</td>
                    </ng-container>
                    <tr mat-header-row *matHeaderRowDef="paymentColumns"></tr>
                    <tr mat-row data-testid="payment-row" *matRowDef="let row; columns: paymentColumns"></tr>
                  </table>
                </div>
              }
            </section>
          }
        </mat-card-content>
        <!-- What may be done comes from the API (isEditable, allowedTransitions); the screen does not work the rules out itself. -->
        <!-- A live region that is always there, so a screen reader hears what happened (the buttons that were pressed disappear). -->
        <p class="visually-hidden" role="status" data-testid="announcement">{{ announcement() }}</p>
        @if (pending(); as action) {
          <!-- role="alertdialog": announced when it appears; focus moves to "Keep it" (the safe answer) and Escape keeps the invoice as it is. -->
          <div
            class="confirm"
            data-testid="confirm"
            role="alertdialog"
            aria-labelledby="confirm-question"
            (keydown.escape)="acting() ? null : keep()"
          >
            <p id="confirm-question">
              @if (action === 'send') {
                Send this invoice? This locks it and it cannot be edited afterwards.
              } @else {
                Cancel this invoice? This cannot be undone.
              }
            </p>
            <button mat-flat-button type="button" data-testid="confirm-yes" [disabled]="acting()" (click)="confirm()">
              @if (acting()) {
                {{ action === 'send' ? 'Sending…' : 'Cancelling…' }}
              } @else {
                {{ action === 'send' ? 'Yes, send invoice' : 'Yes, cancel invoice' }}
              }
            </button>
            <button mat-button type="button" data-testid="confirm-keep" [disabled]="acting()" (click)="keep()">Keep it</button>
          </div>
        }
        @if (actionError(); as message) {
          <p class="error" role="alert" data-testid="action-error">{{ message }}</p>
        }
        <mat-card-actions>
          @if (invoice.isEditable) {
            <a mat-button data-testid="edit" [routerLink]="['/invoices', invoice.id, 'edit']">Edit</a>
          }
          @if (invoice.allowedTransitions.includes('Sent')) {
            <button mat-flat-button type="button" data-testid="send" [disabled]="busy()" (click)="ask('send')">Send</button>
          }
          <!-- The API says whether a payment can be recorded (Sent, also Overdue, with something remaining); any signed-in user may. -->
          @if (invoice.acceptsPayment && !busy()) {
            <a mat-flat-button data-testid="record-payment" [routerLink]="['/invoices', invoice.id, 'payments', 'new']">Record payment</a>
          }
          <!-- Only an Admin may cancel. Hiding it from an Agent is a courtesy: the API refuses an Agent with 403 whatever this screen shows.
               After a 403 the button goes too: the role in the token has not changed, so offering it again would only fail again. -->
          @if (invoice.allowedTransitions.includes('Cancelled') && auth.role() === 'Admin' && !cancelForbidden()) {
            <button mat-stroked-button type="button" data-testid="cancel" [disabled]="busy()" (click)="ask('cancel')">Cancel invoice</button>
          }
          <!-- The history page (spec 010): any signed-in user, any status; it only reads. Same tab, like Print. -->
          <a mat-button data-testid="history" [routerLink]="['/invoices', invoice.id, 'history']">History</a>
          <!-- The printable page (spec 009): any signed-in user, any status. It opens in THIS tab: the session lives in this tab's
               sessionStorage, so a new tab would open signed out. -->
          <a mat-button data-testid="print" [routerLink]="['/invoices', invoice.id, 'print']">Print</a>
          <a mat-button routerLink="/invoices">Back to invoices</a>
        </mat-card-actions>
      </mat-card>
    }
  `,
  styles: `
    mat-card {
      max-width: 900px;
    }
    dl {
      display: grid;
      grid-template-columns: 9rem 1fr;
      gap: 8px 16px;
      margin: 16px 0 0;
    }
    dt {
      font-weight: 500;
    }
    dd {
      margin: 0;
    }
    .notes {
      white-space: pre-line;
    }
    table {
      width: 100%;
    }
    /* Many columns in a narrow card: scroll sideways instead of squeezing. */
    .table-scroll {
      overflow-x: auto;
    }
    .amount {
      text-align: end;
    }
    .totals {
      grid-template-columns: 1fr auto;
      max-width: 22rem;
      margin: 8px 0 0;
      margin-inline-start: auto;
    }
    .totals dd {
      text-align: end;
    }
    .totals .grand {
      font-weight: 700;
    }
    h2 {
      margin: 24px 0 8px;
    }
    .badge {
      margin-inline-start: 8px;
      padding: 2px 8px;
      border-radius: 12px;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
    .confirm {
      margin: 16px;
      padding: 12px 16px;
      border-radius: 8px;
      background: var(--mat-sys-surface-container-high);
      display: flex;
      flex-wrap: wrap;
      gap: 8px 12px;
      align-items: center;
    }
    .confirm p {
      flex: 1 1 100%;
      margin: 0;
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      margin: -1px;
      padding: 0;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      white-space: nowrap;
      border: 0;
    }
    .error {
      color: var(--mat-sys-error);
      margin: 0 16px 8px;
    }
    .badge.paid {
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .badge.overdue,
    .badge.inactive {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
})
export class InvoiceDetailComponent implements OnInit {
  private readonly invoices = inject(InvoiceService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);

  protected readonly state = signal<DetailState>({ kind: 'loading' });

  /** The action the person has asked for and is being asked to confirm (both are permanent), or null. */
  protected readonly pending = signal<'send' | 'cancel' | null>(null);

  /** True while a send or cancel request is running: its buttons are disabled so nothing is done twice. */
  protected readonly acting = signal(false);

  /** The API's message for a refused action, shown above the actions. */
  protected readonly actionError = signal<string | null>(null);

  /** What a screen reader is told after an action ("Invoice sent."); the pressed button is gone by then. */
  protected readonly announcement = signal('');

  /** True after the API refused a cancel with 403: the token's role will not change, so Cancel is not offered again. */
  protected readonly cancelForbidden = signal(false);

  /** True when the page could not be refreshed after a refusal: what it offers may be out of date, so nothing is offered. */
  protected readonly stale = signal(false);

  /** Actions are disabled while one runs, while a question is open, and while the page may be out of date. */
  protected readonly busy = computed(() => this.acting() || this.pending() !== null || this.stale());

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Set when the person leaves the page: an action in flight is NOT cancelled (the server may still apply it), its answer is just not shown. */
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));
  }

  private focus(selector: string): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus());
  }

  /** The discount column is shown only when the invoice has a discount (whether to show it is not a calculation). */
  protected readonly columns = computed(() => {
    const current = this.state();
    const hasDiscount = current.kind === 'loaded' && current.invoice.totals.discount !== 0;
    return ['product', 'sku', 'unit', 'unitPrice', 'vatPercent', 'quantity', 'net', ...(hasDiscount ? ['discountShare'] : []), 'vat'];
  });

  /** The columns of the payments table (payments are only ever shown: there is nothing to edit or remove). */
  protected readonly paymentColumns = ['date', 'amount', 'method', 'note'];

  ngOnInit(): void {
    this.load();
  }

  protected ask(action: 'send' | 'cancel'): void {
    this.actionError.set(null);
    this.announcement.set('');
    this.pending.set(action);
    this.focus('[data-testid="confirm-keep"]');
  }

  protected keep(): void {
    const action = this.pending();
    this.pending.set(null);
    // The question is gone (and so is the button that had focus): put focus back on the button that opened it.
    if (action) {
      this.focus(`[data-testid="${action === 'send' ? 'send' : 'cancel'}"]`);
    }
  }

  /** Does the confirmed action. The API decides whether it is legal; whatever it answers, the page then shows the truth. */
  protected confirm(): void {
    const action = this.pending();
    const current = this.state();
    if (!action || this.acting() || current.kind !== 'loaded') {
      return;
    }

    const id = current.invoice.id;
    this.acting.set(true);
    const request = action === 'send' ? this.invoices.send(id) : this.invoices.cancel(id);
    // Not cancelled if the person leaves: the server may still apply a permanent send or cancel, so the request is left to finish.
    request.subscribe({
      next: (invoice) => {
        if (this.destroyed) {
          return;
        }
        this.acting.set(false);
        this.pending.set(null);
        this.state.set({ kind: 'loaded', invoice });
        this.announcement.set(action === 'send' ? 'Invoice sent.' : 'Invoice cancelled.');
        this.focus('[data-testid="number"]');
      },
      error: (error: unknown) => {
        if (this.destroyed) {
          return;
        }
        this.acting.set(false);
        this.pending.set(null);
        if (error instanceof HttpErrorResponse && error.status === 403) {
          this.cancelForbidden.set(true);
        }
        this.actionError.set(
          problemDetail(error) ??
            (error instanceof HttpErrorResponse && error.status === 0
              ? 'Cannot reach the service. Check that the API is running and try again.'
              : 'Something went wrong. Try again.'),
        );
        this.refresh(id);
      },
    });
  }

  /** Reloads the invoice without the loading screen, so the buttons offered match what is now true (it may have changed meanwhile). */
  private refresh(id: string): void {
    this.invoices
      .get(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => {
          this.stale.set(false);
          this.state.set({ kind: 'loaded', invoice });
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.state.set({ kind: 'notFound' });
            return;
          }
          // What is on screen may no longer be true, so nothing is offered until the page is reloaded.
          this.stale.set(true);
          this.actionError.update((message) => `${message ?? ''} The invoice could not be refreshed: reload the page before doing anything else.`.trim());
        },
      });
  }

  protected load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.state.set({ kind: 'notFound' });
      return;
    }

    this.state.set({ kind: 'loading' });
    this.invoices
      .get(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => this.state.set({ kind: 'loaded', invoice }),
        error: (error: unknown) =>
          this.state.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? { kind: 'notFound' }
              : { kind: 'error', detail: problemDetail(error) },
          ),
      });
  }
}
