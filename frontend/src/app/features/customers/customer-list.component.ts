import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, map, merge, of, startWith, switchMap, tap } from 'rxjs';
import { CUSTOMER_TYPES, CustomerListItem, CustomerListQuery, PagedResult } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { problemDetail } from '../../core/http/problem-details';

type ListState =
  | { kind: 'loading'; previous?: PagedResult<CustomerListItem> }
  | { kind: 'loaded'; result: PagedResult<CustomerListItem> }
  | { kind: 'error'; detail: string | null };

const DEFAULT_PAGE_SIZE = 20;
const MAX_PAGE_SIZE = 100;
const MAX_PAGE = 1_000_000;
const SEARCH_PAUSE_MS = 300;

/** A whole number within range, otherwise the fallback: a hand-edited URL must never leave the list unusable. */
function intInRange(value: string | null, min: number, max: number, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= min && parsed <= max ? parsed : fallback;
}

/** The list's whole state (search, filters, page) lives in the URL, so Back and bookmarks restore it. */
function queryFrom(params: ParamMap): CustomerListQuery {
  const type = params.get('type');
  const active = params.get('active');
  return {
    q: params.get('q') ?? undefined,
    type: CUSTOMER_TYPES.find((candidate) => candidate === type),
    active: active === 'true' ? true : active === 'false' ? false : undefined,
    page: intInRange(params.get('page'), 1, MAX_PAGE, 1),
    pageSize: intInRange(params.get('pageSize'), 1, MAX_PAGE_SIZE, DEFAULT_PAGE_SIZE),
  };
}

@Component({
  selector: 'app-customer-list',
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    RouterLink,
  ],
  template: `
    <div class="header">
      <h1>Customers</h1>
      <a mat-flat-button routerLink="/customers/new">Add customer</a>
    </div>

    <div class="filters">
      <mat-form-field appearance="outline" class="search">
        <mat-label>Search</mat-label>
        <input
          matInput
          data-testid="search"
          aria-label="Search customers by name, email, phone or VAT number"
          dir="auto"
          autocomplete="off"
          [value]="searchText()"
          (input)="onSearchInput($any($event.target).value)"
        />
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Type</mat-label>
        <select
          matNativeControl
          data-testid="type-filter"
          aria-label="Filter by customer type"
          (change)="onTypeChange($any($event.target).value)"
        >
          <option value="" [selected]="typeValue() === ''">All</option>
          @for (type of types; track type) {
            <option [value]="type" [selected]="typeValue() === type">{{ type }}</option>
          }
        </select>
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Status</mat-label>
        <select
          matNativeControl
          data-testid="status-filter"
          aria-label="Filter by active or inactive"
          (change)="onStatusChange($any($event.target).value)"
        >
          <option value="" [selected]="statusValue() === ''">Both</option>
          <option value="true" [selected]="statusValue() === 'true'">Active</option>
          <option value="false" [selected]="statusValue() === 'false'">Inactive</option>
        </select>
      </mat-form-field>
    </div>

    @let current = state();
    @let shown = shownResult();
    @if (current.kind === 'error') {
      <p role="alert" data-testid="list-error">
        The customers could not be loaded.
        @if (current.detail; as detail) {
          {{ detail }}
        }
      </p>
      <button mat-button type="button" data-testid="retry" (click)="retry()">Retry</button>
    } @else if (!shown) {
      <p data-testid="list-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading customers…</p>
    } @else {
      <!-- The previous results stay on screen while the next ones load, so nothing flickers and focus is kept. -->
      @if (shown.items.length === 0) {
        <p data-testid="list-empty" role="status">
          {{ shown.totalCount > 0 ? 'No customers on this page.' : 'No customers match.' }}
        </p>
      } @else {
        <p data-testid="total" role="status">
          {{ shown.totalCount }} {{ shown.totalCount === 1 ? 'customer' : 'customers' }}
          @if (current.kind === 'loading') {
            <span data-testid="list-updating"> · updating…</span>
          }
        </p>
        <table mat-table [dataSource]="shown.items" aria-label="Customers" [attr.aria-busy]="current.kind === 'loading'">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>Name</th>
            <td mat-cell *matCellDef="let row" dir="auto">
              <a [routerLink]="['/customers', row.id]">{{ row.name }}</a>
              @if (!row.isActive) {
                <span class="badge inactive" data-testid="inactive-badge">Inactive</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="type">
            <th mat-header-cell *matHeaderCellDef>Type</th>
            <td mat-cell *matCellDef="let row">{{ row.type }}</td>
          </ng-container>
          <ng-container matColumnDef="email">
            <th mat-header-cell *matHeaderCellDef>Email</th>
            <td mat-cell *matCellDef="let row" dir="auto">{{ row.email }}</td>
          </ng-container>
          <ng-container matColumnDef="phone">
            <th mat-header-cell *matHeaderCellDef>Phone</th>
            <td mat-cell *matCellDef="let row" dir="auto">{{ row.phone }}</td>
          </ng-container>
          <ng-container matColumnDef="vatNumber">
            <th mat-header-cell *matHeaderCellDef>VAT number</th>
            <td mat-cell *matCellDef="let row" dir="auto">{{ row.vatNumber ?? '—' }}</td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row data-testid="customer-row" *matRowDef="let row; columns: columns"></tr>
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
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      margin-top: 8px;
    }
    .search {
      flex: 1 1 280px;
    }
    table {
      width: 100%;
    }
    .badge {
      margin-inline-start: 8px;
      padding: 2px 8px;
      border-radius: 12px;
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
      font-size: 0.8em;
    }
  `,
})
export class CustomerListComponent {
  private readonly customers = inject(CustomerService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = ['name', 'type', 'email', 'phone', 'vatNumber'];
  protected readonly types = CUSTOMER_TYPES;
  protected readonly state = signal<ListState>({ kind: 'loading' });

  // What the controls show. Filled from the URL, so Back and bookmarks restore them.
  protected readonly searchText = signal('');
  protected readonly typeValue = signal('');
  protected readonly statusValue = signal('');

  private query: CustomerListQuery = {};
  private lastResult?: PagedResult<CustomerListItem>;
  private readonly typing = new Subject<string>();
  private readonly retryRequests = new Subject<void>();

  /**
   * The search text the last URL change was made for. The text box is only overwritten from the URL when
   * the URL disagrees with it (Back button, a pasted link), never with the person's own just-typed text.
   */
  private lastSearchInUrl: string | undefined;

  constructor() {
    const fromUrl = this.route.queryParamMap.pipe(
      map(queryFrom),
      tap((query) => {
        this.query = query;
        this.showControlsFor(query);
      }),
    );
    const fromRetry = this.retryRequests.pipe(map(() => this.query));

    // One stream drives loading. switchMap drops the previous request the moment a newer one starts, so a slow
    // old answer can never replace a newer one (typing fast, paging fast, or Retry overlapping a request).
    merge(fromUrl, fromRetry)
      .pipe(
        switchMap((query) =>
          this.customers.list(query).pipe(
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

    // Wait for a pause in typing so each keystroke does not become a request.
    this.typing.pipe(debounceTime(SEARCH_PAUSE_MS), takeUntilDestroyed(this.destroyRef)).subscribe((value) => {
      const q = value.trim();
      this.lastSearchInUrl = q;
      // replaceUrl: Back should not step through every fragment the person typed.
      this.navigateWith({ q: q || null }, true);
    });
  }

  /** The results to draw: the current ones, or the previous ones while the next are loading. */
  protected shownResult(): PagedResult<CustomerListItem> | undefined {
    const current = this.state();
    return current.kind === 'loaded' ? current.result : current.kind === 'loading' ? current.previous : undefined;
  }

  protected retry(): void {
    this.retryRequests.next();
  }

  protected onSearchInput(value: string): void {
    this.searchText.set(value);
    this.typing.next(value);
  }

  protected onTypeChange(value: string): void {
    this.navigateWith({ type: value || null });
  }

  protected onStatusChange(value: string): void {
    this.navigateWith({ active: value || null });
  }

  protected onPage(event: PageEvent): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }

  private showControlsFor(query: CustomerListQuery): void {
    const q = query.q ?? '';
    if (q !== this.lastSearchInUrl) {
      this.searchText.set(q);
      this.lastSearchInUrl = q;
    }
    this.typeValue.set(query.type ?? '');
    this.statusValue.set(query.active === undefined ? '' : String(query.active));
  }

  /** Any change to the search or a filter goes back to page 1. */
  private navigateWith(params: Record<string, string | null>, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { ...params, page: null },
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }
}
