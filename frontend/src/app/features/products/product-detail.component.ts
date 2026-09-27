import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problemDetail } from '../../core/http/problem-details';
import { Product } from '../../core/products/product.models';
import { ProductService } from '../../core/products/product.service';

type DetailState =
  | { kind: 'loading' }
  | { kind: 'loaded'; product: Product }
  | { kind: 'notFound' }
  | { kind: 'error'; detail: string | null };

@Component({
  selector: 'app-product-detail',
  imports: [DatePipe, DecimalPipe, MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink],
  template: `
    @let current = state();
    @if (current.kind === 'loading') {
      <p data-testid="detail-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading product…</p>
    } @else if (current.kind === 'notFound') {
      <mat-card>
        <mat-card-header><h1 mat-card-title>Product not found</h1></mat-card-header>
        <mat-card-content><p role="alert">No product exists with this address. It may have been opened from an old link.</p></mat-card-content>
        <mat-card-actions><a mat-button routerLink="/products">Back to products</a></mat-card-actions>
      </mat-card>
    } @else if (current.kind === 'error') {
      <mat-card>
        <mat-card-content>
          <p role="alert" data-testid="detail-error">
            The product could not be loaded.
            @if (current.detail; as detail) {
              {{ detail }}
            } @else {
              Try again.
            }
          </p>
        </mat-card-content>
        <mat-card-actions>
          <button mat-button type="button" (click)="load()">Retry</button>
          <a mat-button routerLink="/products">Back to products</a>
        </mat-card-actions>
      </mat-card>
    } @else {
      @let product = current.product;
      <mat-card>
        <mat-card-header>
          <h1 mat-card-title data-testid="name" dir="auto">{{ product.name }}</h1>
          <mat-card-subtitle>
            <span dir="ltr">{{ product.sku }}</span>
            <span class="badge" [class.inactive]="!product.isActive" data-testid="status-badge">{{
              product.isActive ? 'Active' : 'Inactive'
            }}</span>
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <dl>
            <dt>SKU</dt>
            <dd dir="ltr" data-testid="sku">{{ product.sku }}</dd>
            <dt>Unit</dt>
            <dd data-testid="unit">{{ product.unit }}</dd>
            <dt>Unit price</dt>
            <dd dir="ltr" data-testid="unit-price">SAR {{ product.unitPrice | number: '1.2-2' }}</dd>
            <dt>VAT</dt>
            <dd dir="ltr" data-testid="vat-percent">{{ product.vatPercent | number: '1.0-2' }}%</dd>
            <dt>Created</dt>
            <dd data-testid="created-at">{{ product.createdAtUtc | date: 'medium' }}</dd>
            <dt>Last changed</dt>
            <dd data-testid="updated-at">{{ product.updatedAtUtc | date: 'medium' }}</dd>
          </dl>
        </mat-card-content>
        <mat-card-actions>
          <!-- Only an Admin can change a product; the API refuses an Agent with 403 whatever this page shows. -->
          @if (isAdmin()) {
            <a mat-button [routerLink]="['/products', product.id, 'edit']">Edit</a>
            <button mat-button type="button" data-testid="toggle-active" [disabled]="toggling()" (click)="toggleActive(product)">
              {{ product.isActive ? 'Deactivate' : 'Activate' }}
            </button>
          }
          <a mat-button routerLink="/products">Back to products</a>
        </mat-card-actions>
        @if (toggleError(); as message) {
          <p class="error" role="alert" data-testid="toggle-error">{{ message }}</p>
        }
        @if (toggling()) {
          <p class="note" role="status" data-testid="toggle-pending">Saving…</p>
        }
        @if (toggleDone(); as message) {
          <p class="note" role="status" data-testid="toggle-done">{{ message }}</p>
        }
      </mat-card>
    }
  `,
  styles: `
    mat-card {
      max-width: 640px;
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
    .error {
      color: var(--mat-sys-error);
      margin: 8px 16px;
    }
    .note {
      margin: 8px 16px;
    }
    .badge {
      margin-inline-start: 8px;
      padding: 2px 8px;
      border-radius: 12px;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
    .badge.inactive {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
})
export class ProductDetailComponent implements OnInit {
  private readonly products = inject(ProductService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly state = signal<DetailState>({ kind: 'loading' });
  protected readonly toggling = signal(false);
  protected readonly toggleError = signal<string | null>(null);
  protected readonly toggleDone = signal<string | null>(null);

  /** Only an Admin sees Edit and Deactivate/Activate. */
  protected readonly isAdmin = () => this.auth.role() === 'Admin';

  /**
   * Retires ("deactivate to hide") or restores the product. On failure the previous state stays on screen,
   * because nothing changed on the server, and the person is told why.
   */
  protected toggleActive(product: Product): void {
    if (this.toggling()) {
      return;
    }

    this.toggling.set(true);
    this.toggleError.set(null);
    this.toggleDone.set(null);
    this.products
      .setActive(product.id, !product.isActive)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.toggling.set(false);
          this.state.set({ kind: 'loaded', product: updated });
          // Said aloud too, because the badge and button label change silently for a screen reader.
          this.toggleDone.set(updated.isActive ? 'Product activated.' : 'Product deactivated.');
        },
        error: (error: unknown) => {
          this.toggling.set(false);
          this.toggleError.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'This product no longer exists.'
              : (problemDetail(error) ?? 'The change could not be saved. Try again.'),
          );
        },
      });
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.state.set({ kind: 'notFound' });
      return;
    }

    this.state.set({ kind: 'loading' });
    this.products
      .get(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (product) => this.state.set({ kind: 'loaded', product }),
        error: (error: unknown) =>
          this.state.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? { kind: 'notFound' }
              : { kind: 'error', detail: problemDetail(error) },
          ),
      });
  }
}
