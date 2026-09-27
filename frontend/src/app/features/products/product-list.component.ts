import { DecimalPipe } from '@angular/common';
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
import { AuthService } from '../../core/auth/auth.service';
import { problemDetail } from '../../core/http/problem-details';
import { PRODUCT_STATUSES, PRODUCT_UNITS, PagedResult, ProductListItem, ProductListQuery } from '../../core/products/product.models';
import { ProductService } from '../../core/products/product.service';

type ListState =
  | { kind: 'loading'; previous?: PagedResult<ProductListItem> }
  | { kind: 'loaded'; result: PagedResult<ProductListItem> }
  | { kind: 'error'; detail: string | null };

const DEFAULT_PAGE_SIZE = 20;
const MAX_PAGE_SIZE = 100;
const MAX_PAGE = 1_000_000;
const SEARCH_PAUSE_MS = 300;
/** The API refuses longer search text with a 400, so the box and a hand-edited address are held to the same limit. */
const MAX_SEARCH_LENGTH = 100;

/** A whole number within range, otherwise the fallback: a hand-edited URL must never leave the list unusable. */
function intInRange(value: string | null, min: number, max: number, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= min && parsed <= max ? parsed : fallback;
}

/**
 * The list's whole state (search, filters, page) lives in the URL, so Back and bookmarks restore it. Only an Admin's
 * request carries a "status": an Agent's list is always the active products, so the value is never sent for them.
 */
function queryFrom(params: ParamMap, isAdmin: boolean): ProductListQuery {
  const unit = params.get('unit');
  const status = PRODUCT_STATUSES.find((candidate) => candidate === params.get('status'));
  const q = params.get('q');
  return {
    // Over-long text in the address is dropped instead of turning the whole list into an error page.
    q: q !== null && q.trim().length <= MAX_SEARCH_LENGTH ? q : undefined,
    unit: PRODUCT_UNITS.find((candidate) => candidate === unit),
    // "active" is the server's default, so it is simply left out.
    status: isAdmin && status !== 'active' ? status : undefined,
    page: intInRange(params.get('page'), 1, MAX_PAGE, 1),
    pageSize: intInRange(params.get('pageSize'), 1, MAX_PAGE_SIZE, DEFAULT_PAGE_SIZE),
  };
}

@Component({
  selector: 'app-product-list',
  imports: [
    DecimalPipe,
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
      <h1>Products</h1>
      @if (isAdmin()) {
        <a mat-flat-button routerLink="/products/new">Add product</a>
      }
    </div>

    <div class="filters">
      <mat-form-field appearance="outline" class="search">
        <mat-label>Search</mat-label>
        <input
          matInput
          data-testid="search"
          aria-label="Search products by name or SKU"
          dir="auto"
          autocomplete="off"
          maxlength="100"
          [value]="searchText()"
          (input)="onSearchInput($any($event.target).value)"
        />
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Unit</mat-label>
        <select
          matNativeControl
          data-testid="unit-filter"
          aria-label="Filter by unit"
          (change)="onUnitChange($any($event.target).value)"
        >
          <option value="" [selected]="unitValue() === ''">All</option>
          @for (unit of units; track unit) {
            <option [value]="unit" [selected]="unitValue() === unit">{{ unit }}</option>
          }
        </select>
      </mat-form-field>

      @if (isAdmin()) {
        <mat-form-field appearance="outline">
          <mat-label>Status</mat-label>
          <select
            matNativeControl
            data-testid="status-filter"
            aria-label="Filter by active or inactive"
            (change)="onStatusChange($any($event.target).value)"
          >
            <option value="active" [selected]="statusValue() === 'active'">Active</option>
            <option value="inactive" [selected]="statusValue() === 'inactive'">Inactive</option>
            <option value="all" [selected]="statusValue() === 'all'">All</option>
          </select>
        </mat-form-field>
      }
    </div>

    @let current = state();
    @let shown = shownResult();
    @if (current.kind === 'error') {
      <p role="alert" data-testid="list-error">
        The products could not be loaded.
        @if (current.detail; as detail) {
          {{ detail }}
        }
      </p>
      <button mat-button type="button" data-testid="retry" (click)="retry()">Retry</button>
    } @else if (!shown) {
      <p data-testid="list-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading products…</p>
    } @else {
      <!-- The previous results stay on screen while the next ones load, so nothing flickers and focus is kept. -->
      @if (shown.items.length === 0) {
        <p data-testid="list-empty" role="status">
          {{ shown.totalCount > 0 ? 'No products on this page.' : 'No products match.' }}
        </p>
      } @else {
        <p data-testid="total" role="status">
          {{ shown.totalCount }} {{ shown.totalCount === 1 ? 'product' : 'products' }}
          @if (current.kind === 'loading') {
            <span data-testid="list-updating"> · updating…</span>
          }
        </p>
        <table mat-table [dataSource]="shown.items" aria-label="Products" [attr.aria-busy]="current.kind === 'loading'">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>Name</th>
            <td mat-cell *matCellDef="let row" dir="auto">
              <a [routerLink]="['/products', row.id]">{{ row.name }}</a>
              @if (!row.isActive) {
                <span class="badge inactive" data-testid="inactive-badge">Inactive</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="sku">
            <th mat-header-cell *matHeaderCellDef>SKU</th>
            <td mat-cell *matCellDef="let row" dir="ltr">{{ row.sku }}</td>
          </ng-container>
          <ng-container matColumnDef="unit">
            <th mat-header-cell *matHeaderCellDef>Unit</th>
            <td mat-cell *matCellDef="let row">{{ row.unit }}</td>
          </ng-container>
          <ng-container matColumnDef="unitPrice">
            <th mat-header-cell *matHeaderCellDef>Unit price</th>
            <td mat-cell *matCellDef="let row" dir="ltr">SAR {{ row.unitPrice | number: '1.2-2' }}</td>
          </ng-container>
          <ng-container matColumnDef="vatPercent">
            <th mat-header-cell *matHeaderCellDef>VAT</th>
            <td mat-cell *matCellDef="let row" dir="ltr">{{ row.vatPercent | number: '1.0-2' }}%</td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row data-testid="product-row" *matRowDef="let row; columns: columns"></tr>
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
export class ProductListComponent {
  private readonly products = inject(ProductService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = ['name', 'sku', 'unit', 'unitPrice', 'vatPercent'];
  protected readonly units = PRODUCT_UNITS;
  protected readonly state = signal<ListState>({ kind: 'loading' });

  // What the controls show. Filled from the URL, so Back and bookmarks restore them.
  protected readonly searchText = signal('');
  protected readonly unitValue = signal('');
  protected readonly statusValue = signal('active');

  /** Only an Admin sees Add (and, later, the other change controls); the API refuses an Agent anyway. */
  protected readonly isAdmin = () => this.auth.role() === 'Admin';

  private query: ProductListQuery = {};
  private lastResult?: PagedResult<ProductListItem>;
  private readonly typing = new Subject<string>();
  private readonly retryRequests = new Subject<void>();

  /**
   * The search text the last URL change was made for. The text box is only overwritten from the URL when
   * the URL disagrees with it (Back button, a pasted link), never with the person's own just-typed text.
   */
  private lastSearchInUrl: string | undefined;

  constructor() {
    const fromUrl = this.route.queryParamMap.pipe(
      map((params) => queryFrom(params, this.isAdmin())),
      tap((query) => {
        this.query = query;
        this.showControlsFor(query);
      }),
    );
    const fromRetry = this.retryRequests.pipe(map(() => this.query));

    // One stream drives loading. switchMap drops the previous request the moment a newer one starts, so a slow
    // old answer can never replace a newer one (paging fast, or Retry overlapping a request).
    merge(fromUrl, fromRetry)
      .pipe(
        switchMap((query) =>
          this.products.list(query).pipe(
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
  protected shownResult(): PagedResult<ProductListItem> | undefined {
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

  protected onUnitChange(value: string): void {
    this.navigateWith({ unit: value || null });
  }

  protected onStatusChange(value: string): void {
    // Active is the default, so choosing it removes the parameter instead of writing it.
    this.navigateWith({ status: value && value !== 'active' ? value : null });
  }

  protected onPage(event: PageEvent): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }

  private showControlsFor(query: ProductListQuery): void {
    const q = query.q ?? '';
    if (q !== this.lastSearchInUrl) {
      this.searchText.set(q);
      this.lastSearchInUrl = q;
    }
    this.unitValue.set(query.unit ?? '');
    this.statusValue.set(query.status ?? 'active');
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
