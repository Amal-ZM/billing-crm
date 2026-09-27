import { HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT, DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemDetail } from '../../core/http/problem-details';
import { Invoice } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';

type PrintState =
  | { kind: 'loading' }
  | { kind: 'loaded'; invoice: Invoice }
  | { kind: 'notFound' }
  | { kind: 'error'; detail: string | null };

/**
 * The printable invoice (`/invoices/:id/print`, spec 009): the invoice as a clean document for the browser's own print command. It
 * lives outside the signed-in shell (so none of the application's toolbar or menus is in the page) but behind the same guard, reads the
 * invoice with the one existing `GET`, and shows what the details show, in the same words, with the same formats and the same
 * `data-testid`s (a parity test compares the two screens). It calculates, rounds and derives nothing, and it changes nothing.
 *
 * The print rules are this component's own, with no style encapsulation, every rule under `.invoice-print`: Angular removes the
 * component's styles when it is destroyed, so the page margin below applies only while this page is on screen.
 */
@Component({
  selector: 'app-invoice-print',
  imports: [DatePipe, DecimalPipe, MatProgressSpinnerModule, RouterLink],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="invoice-print">
      @let current = state();
      @if (current.kind === 'loading') {
        <p data-testid="detail-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading invoice…</p>
      } @else if (current.kind === 'notFound') {
        <h1>Invoice not found</h1>
        <p role="alert">No invoice exists with this address. It may have been opened from an old link.</p>
        <a class="no-print" routerLink="/invoices">Back to invoices</a>
      } @else if (current.kind === 'error') {
        <p role="alert" data-testid="detail-error">
          The invoice could not be loaded.
          @if (current.detail; as detail) {
            {{ detail }}
          } @else {
            Try again.
          }
        </p>
        <!-- Nothing to print here but the message: the controls are for the screen only. -->
        <div class="no-print bar">
          <button type="button" data-testid="retry" (click)="load()">Retry</button>
          <a routerLink="/invoices">Back to invoices</a>
        </div>
      } @else {
        @let invoice = current.invoice;

        <!-- On screen only: the Print button opens the browser's print dialog (nothing prints by itself) and the way back. -->
        <div class="no-print bar">
          <button type="button" data-testid="print-button" (click)="print()">Print</button>
          <a data-testid="back" [routerLink]="['/invoices', invoice.id]">Back to invoice</a>
        </div>

        <article class="document">
          <h1>BillingCRM</h1>

          <div class="heading">
            <h2 data-testid="number" dir="ltr">{{ invoice.number }}</h2>
            <span class="status-line">
              <span class="status-label">Status</span>
              <!-- Overdue is not a stored status: the API says a Sent invoice is overdue and the page shows that word, like the details. -->
              <span class="status" data-testid="status-badge">{{ invoice.isOverdue ? 'Overdue' : invoice.status }}</span>
            </span>
          </div>

          <dl class="facts">
            <dt>Customer</dt>
            <dd dir="auto" data-testid="customer">{{ invoice.customer.name }}</dd>
            <dt>Created</dt>
            <dd dir="ltr" data-testid="created-at">{{ invoice.createdAtUtc | date: 'medium' }}</dd>
            <dt>Due date</dt>
            <dd dir="ltr" data-testid="due-date">{{ invoice.dueDate | date: 'mediumDate' }}</dd>
            @if (invoice.sentAtUtc) {
              <dt>Sent</dt>
              <dd dir="ltr" data-testid="sent-at">{{ invoice.sentAtUtc | date: 'medium' }}</dd>
            }
            @if (invoice.cancelledAtUtc) {
              <dt>Cancelled</dt>
              <dd dir="ltr" data-testid="cancelled-at">{{ invoice.cancelledAtUtc | date: 'medium' }}</dd>
            }
          </dl>

          @if (invoice.notes) {
            <section class="notes-block" aria-label="Notes">
              <h3>Notes</h3>
              <p dir="auto" class="notes" data-testid="notes">{{ invoice.notes }}</p>
            </section>
          }

          <!-- Each price and percent is the copy saved when the line was added. Net, discount share and VAT are the amounts the API
               calculated from it: shown as given, never calculated here. On screen a very wide table scrolls sideways. -->
          <div class="table-scroll">
            <table class="lines" aria-label="Invoice lines">
              <thead>
                <tr>
                  <th>Product</th>
                  <th>SKU</th>
                  <th>Unit</th>
                  <th class="amount">Unit price</th>
                  <th class="amount">VAT</th>
                  <th class="amount">Quantity</th>
                  <th class="amount">Net</th>
                  @if (invoice.totals.discount !== 0) {
                    <th class="amount">Discount</th>
                  }
                  <th class="amount">VAT amount</th>
                </tr>
              </thead>
              <tbody>
                @for (line of invoice.lines; track line.id) {
                  <tr data-testid="line-row">
                    <td dir="auto">{{ line.productName }}</td>
                    <td dir="ltr">{{ line.sku }}</td>
                    <td>{{ line.unit }}</td>
                    <td dir="ltr" class="amount">SAR {{ line.unitPrice | number: '1.2-2' }}</td>
                    <td dir="ltr" class="amount">{{ line.vatPercent | number: '1.0-2' }}%</td>
                    <td dir="ltr" class="amount">{{ line.quantity | number: '1.2-2' }}</td>
                    <td dir="ltr" class="amount">SAR {{ line.net | number: '1.2-2' }}</td>
                    @if (invoice.totals.discount !== 0) {
                      <td dir="ltr" class="amount">SAR {{ line.discountShare | number: '1.2-2' }}</td>
                    }
                    <td dir="ltr" class="amount">SAR {{ line.vat | number: '1.2-2' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <!-- What the invoice comes to, as calculated by the API. The discount rows appear only when there is a discount. -->
          <section class="totals-block" aria-label="Totals">
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
        </article>
      }
    </div>
  `,
  styles: `
    .invoice-print {
      max-width: 800px;
      margin: 0 auto;
      padding: 24px;
      color: #000;
      background: #fff;
      font-family: Roboto, 'Helvetica Neue', Arial, sans-serif;
      font-size: 14px;
      line-height: 1.4;
    }
    .invoice-print .bar {
      display: flex;
      gap: 16px;
      align-items: center;
      margin-bottom: 24px;
      padding-bottom: 12px;
      border-bottom: 1px solid #999;
    }
    .invoice-print h1 {
      margin: 0 0 4px;
      font-size: 28px;
    }
    .invoice-print h2 {
      margin: 0;
      font-size: 20px;
    }
    .invoice-print h3 {
      margin: 16px 0 4px;
      font-size: 14px;
    }
    .invoice-print .heading {
      display: flex;
      gap: 16px;
      align-items: baseline;
      margin-bottom: 12px;
    }
    .invoice-print .status-label {
      margin-inline-end: 6px;
      font-weight: 700;
    }
    .invoice-print .status {
      font-weight: 700;
    }
    .invoice-print dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
      margin: 0;
    }
    .invoice-print dt {
      font-weight: 700;
    }
    .invoice-print dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .invoice-print .notes {
      margin: 0;
      white-space: pre-line;
      overflow-wrap: anywhere;
    }
    .invoice-print .table-scroll {
      margin-top: 20px;
      overflow-x: auto;
    }
    .invoice-print table.lines {
      width: 100%;
      border-collapse: collapse;
    }
    .invoice-print table.lines th,
    .invoice-print table.lines td {
      padding: 6px 8px;
      border-bottom: 1px solid #999;
      text-align: start;
      vertical-align: top;
      overflow-wrap: anywhere;
    }
    .invoice-print table.lines th {
      border-bottom: 2px solid #000;
    }
    /* Amounts line up on their right edge and are never split. This rule is written with the table's own selector so it is at least
       as specific as the cell rule above (a plain ".amount" would lose to it and leave the amounts left-aligned). */
    .invoice-print table.lines th.amount,
    .invoice-print table.lines td.amount {
      text-align: end;
      white-space: nowrap;
    }
    .invoice-print .totals-block {
      margin-top: 16px;
    }
    .invoice-print dl.totals {
      grid-template-columns: 1fr max-content;
      max-width: 22rem;
      margin-inline-start: auto;
    }
    .invoice-print dl.totals dd {
      text-align: end;
      white-space: nowrap;
    }
    .invoice-print dl.totals .grand {
      padding-top: 4px;
      border-top: 2px solid #000;
      font-size: 16px;
      font-weight: 700;
    }

    @page {
      margin: 15mm;
    }
    @media print {
      .invoice-print {
        max-width: 100%;
        margin: 0;
        padding: 0;
        color: #000;
        background: #fff;
      }
      /* !important: whatever else sets "display" on the bar (it is a flex row on screen) must never win in print. */
      .invoice-print .no-print {
        display: none !important;
      }
      .invoice-print .table-scroll {
        overflow: visible;
      }
      /* A little smaller and tighter on paper, so a line with large amounts has room in the width of the page. */
      .invoice-print table.lines {
        font-size: 11px;
      }
      .invoice-print table.lines th,
      .invoice-print table.lines td {
        padding: 4px 5px;
      }
      .invoice-print thead {
        display: table-header-group;
      }
      .invoice-print tr {
        break-inside: avoid;
      }
      .invoice-print .totals-block,
      .invoice-print .notes-block {
        break-inside: avoid;
      }
      .invoice-print .totals-block {
        break-before: avoid;
      }
    }
  `,
})
export class InvoicePrintComponent implements OnInit {
  private readonly invoices = inject(InvoiceService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);

  protected readonly state = signal<PrintState>({ kind: 'loading' });

  /** The invoice being shown, from the address. It follows the address, so the page can never show one invoice under another's URL. */
  private id = '';

  /** Numbers the requests, so a slow older answer can never overwrite a newer one. */
  private request = 0;

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.id = params.get('id') ?? '';
      this.load();
    });
  }

  /** Reads the invoice. This is the only request the page ever makes. */
  protected load(): void {
    const request = ++this.request;
    this.state.set({ kind: 'loading' });
    this.invoices
      .get(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => request === this.request && this.state.set({ kind: 'loaded', invoice }),
        error: (error: unknown) => {
          if (request === this.request) {
            this.state.set(
              error instanceof HttpErrorResponse && error.status === 404
                ? { kind: 'notFound' }
                : { kind: 'error', detail: problemDetail(error) },
            );
          }
        },
      });
  }

  /** Opens the browser's own print dialog. It is never called by the page itself: only the person's press of the Print button does. */
  protected print(): void {
    this.document.defaultView?.print();
  }
}
