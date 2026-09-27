import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { Subject, catchError, map, merge, of, startWith, switchMap, tap } from 'rxjs';
import { problemDetail } from '../../core/http/problem-details';
import { InvoiceListItem, InvoiceListQuery, InvoiceStatus, PagedResult } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';

type ListState =
  | { kind: 'loading'; previous?: PagedResult<InvoiceListItem> }
  | { kind: 'loaded'; result: PagedResult<InvoiceListItem> }
  | { kind: 'error'; detail: string | null };

const DEFAULT_PAGE_SIZE = 20;
const MAX_PAGE_SIZE = 100;
const MAX_PAGE = 1_000_000;

/** A whole number within range, otherwise the fallback: a hand-edited address must never leave the list unusable. */
function intInRange(value: string | null, min: number, max: number, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= min && parsed <= max ? parsed : fallback;
}

/** The list's whole state (only the page, in this spec) lives in the address, so Back and bookmarks restore it. */
const KNOWN_STATUSES: InvoiceStatus[] = ['Draft', 'Sent', 'Paid', 'Cancelled'];

/** Only from a dashboard card's own link (spec 011); the screen has no filter control of its own. */
function queryFrom(params: ParamMap): InvoiceListQuery {
  const status = params.get('status');
  return {
    page: intInRange(params.get('page'), 1, MAX_PAGE, 1),
    pageSize: intInRange(params.get('pageSize'), 1, MAX_PAGE_SIZE, DEFAULT_PAGE_SIZE),
    status: status && (KNOWN_STATUSES as string[]).includes(status) ? (status as InvoiceStatus) : undefined,
    overdue: params.get('overdue') === 'true' ? true : undefined,
    paidMonth: params.get('paidMonth') === 'true' ? true : undefined,
  };
}

@Component({
  selector: 'app-invoice-list',
  imports: [DatePipe, MatButtonModule, MatPaginatorModule, MatProgressSpinnerModule, MatTableModule, RouterLink],
  template: `
    <div class="header">
      <h1>Invoices</h1>
      <a mat-flat-button routerLink="/invoices/new">Create invoice</a>
    </div>

    @let current = state();
    @let shown = shownResult();
    @if (current.kind === 'error') {
      <p role="alert" data-testid="list-error">
        The invoices could not be loaded.
        @if (current.detail; as detail) {
          {{ detail }}
        }
      </p>
      <button mat-button type="button" data-testid="retry" (click)="retry()">Retry</button>
    } @else if (!shown) {
      <p data-testid="list-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading invoices…</p>
    } @else {
      <!-- The previous results stay on screen while the next ones load, so nothing flickers and focus is kept. -->
      @if (shown.items.length === 0) {
        <p data-testid="list-empty" role="status">
          @if (shown.totalCount > 0) {
            No invoices on this page.
          } @else {
            No invoices yet. <a routerLink="/invoices/new">Create the first one.</a>
          }
        </p>
      } @else {
        <p data-testid="total" role="status">
          {{ shown.totalCount }} {{ shown.totalCount === 1 ? 'invoice' : 'invoices' }}
          @if (current.kind === 'loading') {
            <span data-testid="list-updating"> · updating…</span>
          }
        </p>
        <table mat-table [dataSource]="shown.items" aria-label="Invoices" [attr.aria-busy]="current.kind === 'loading'">
          <ng-container matColumnDef="number">
            <th mat-header-cell *matHeaderCellDef>Number</th>
            <td mat-cell *matCellDef="let row" dir="ltr">
              <a [routerLink]="['/invoices', row.id]">{{ row.number }}</a>
            </td>
          </ng-container>
          <ng-container matColumnDef="customer">
            <th mat-header-cell *matHeaderCellDef>Customer</th>
            <td mat-cell *matCellDef="let row" dir="auto" data-testid="customer-name">{{ row.customerName }}</td>
          </ng-container>
          <ng-container matColumnDef="dueDate">
            <th mat-header-cell *matHeaderCellDef>Due date</th>
            <td mat-cell *matCellDef="let row">{{ row.dueDate | date: 'mediumDate' }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <!-- Overdue is not a stored status: the API says a Sent invoice is overdue and the screen shows that word. -->
            <td mat-cell *matCellDef="let row" data-testid="status-cell" [class.overdue]="row.isOverdue" [class.paid]="row.status === 'Paid'">{{ row.isOverdue ? 'Overdue' : row.status }}</td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row data-testid="invoice-row" *matRowDef="let row; columns: columns"></tr>
        </table>
      }

      <mat-paginator
        [length]="shown.totalCount"
        [pageSize]="shown.pageSize"
        [pageIndex]="shown.page - 1"
        [pageSizeOptions]="[10, 20, 50, 100]"
        showFirstLastButtons
        (page)="onPage($event)"
      />
    }
  `,
  styles: `
    .header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    table {
      width: 100%;
    }
    .overdue {
      color: var(--mat-sys-error);
      font-weight: 500;
    }
    .paid {
      color: var(--mat-sys-tertiary);
      font-weight: 500;
    }
  `,
})
export class InvoiceListComponent {
  private readonly invoices = inject(InvoiceService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = ['number', 'customer', 'dueDate', 'status'];
  protected readonly state = signal<ListState>({ kind: 'loading' });

  private query: InvoiceListQuery = {};
  private lastResult?: PagedResult<InvoiceListItem>;
  private readonly retryRequests = new Subject<void>();

  constructor() {
    const fromUrl = this.route.queryParamMap.pipe(
      map(queryFrom),
      tap((query) => (this.query = query)),
    );
    const fromRetry = this.retryRequests.pipe(map(() => this.query));

    // One stream drives loading. switchMap drops the previous request the moment a newer one starts, so a slow
    // old answer can never replace a newer one (paging fast, or Retry overlapping a request).
    merge(fromUrl, fromRetry)
      .pipe(
        switchMap((query) =>
          this.invoices.list(query).pipe(
            map((result): ListState => ({ kind: 'loaded', result })),
            catchError((error: unknown) => of<ListState>({ kind: 'error', detail: problemDetail(error) })),
            startWith<ListState>({ kind: 'loading', previous: this.lastResult }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => {
        if (state.kind === 'loaded') {
          this.lastResult = state.result;
        }
        this.state.set(state);
      });
  }

  /** The results to draw: the current ones, or the previous ones while the next are loading. */
  protected shownResult(): PagedResult<InvoiceListItem> | undefined {
    const current = this.state();
    return current.kind === 'loaded' ? current.result : current.kind === 'loading' ? current.previous : undefined;
  }

  protected retry(): void {
    this.retryRequests.next();
  }

  protected onPage(event: PageEvent): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }
}
