import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Customer } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { problemDetail } from '../../core/http/problem-details';

type DetailState =
  | { kind: 'loading' }
  | { kind: 'loaded'; customer: Customer }
  | { kind: 'notFound' }
  | { kind: 'error'; detail: string | null };

@Component({
  selector: 'app-customer-detail',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink],
  template: `
    @let current = state();
    @if (current.kind === 'loading') {
      <p data-testid="detail-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading customer…</p>
    } @else if (current.kind === 'notFound') {
      <mat-card>
        <mat-card-header><h1 mat-card-title>Customer not found</h1></mat-card-header>
        <mat-card-content><p role="alert">No customer exists with this address. It may have been opened from an old link.</p></mat-card-content>
        <mat-card-actions><a mat-button routerLink="/customers">Back to customers</a></mat-card-actions>
      </mat-card>
    } @else if (current.kind === 'error') {
      <mat-card>
        <mat-card-content>
          <p role="alert" data-testid="detail-error">
            The customer could not be loaded.
            @if (current.detail; as detail) {
              {{ detail }}
            } @else {
              Try again.
            }
          </p>
        </mat-card-content>
        <mat-card-actions>
          <button mat-button type="button" (click)="load()">Retry</button>
          <a mat-button routerLink="/customers">Back to customers</a>
        </mat-card-actions>
      </mat-card>
    } @else {
      @let customer = current.customer;
      <mat-card>
        <mat-card-header>
          <h1 mat-card-title data-testid="name" dir="auto">{{ customer.name }}</h1>
          <mat-card-subtitle>
            {{ customer.type }}
            <span class="badge" [class.inactive]="!customer.isActive" data-testid="status-badge">{{
              customer.isActive ? 'Active' : 'Inactive'
            }}</span>
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <dl>
            <dt>Email</dt>
            <dd dir="auto" data-testid="email">{{ customer.email }}</dd>
            <dt>Phone</dt>
            <dd dir="auto" data-testid="phone">{{ customer.phone }}</dd>
            <dt>VAT number</dt>
            <dd dir="auto" data-testid="vat">{{ customer.vatNumber ?? '—' }}</dd>
            <dt>Address</dt>
            <dd dir="auto" class="address" data-testid="address">{{ customer.address }}</dd>
            <dt>Created</dt>
            <dd data-testid="created-at">{{ customer.createdAtUtc | date: 'medium' }}</dd>
            <dt>Last changed</dt>
            <dd data-testid="updated-at">{{ customer.updatedAtUtc | date: 'medium' }}</dd>
          </dl>
        </mat-card-content>
        <mat-card-actions>
          <a mat-button [routerLink]="['/customers', customer.id, 'edit']">Edit</a>
          <button mat-button type="button" data-testid="toggle-active" [disabled]="toggling()" (click)="toggleActive(customer)">
            {{ customer.isActive ? 'Deactivate' : 'Activate' }}
          </button>
          <a mat-button routerLink="/customers">Back to customers</a>
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
    .address {
      white-space: pre-line;
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
export class CustomerDetailComponent implements OnInit {
  private readonly customers = inject(CustomerService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly state = signal<DetailState>({ kind: 'loading' });
  protected readonly toggling = signal(false);
  protected readonly toggleError = signal<string | null>(null);
  protected readonly toggleDone = signal<string | null>(null);

  /**
   * Retires or restores the customer. On failure the previous state stays on screen, because nothing
   * changed on the server, and the person is told why.
   */
  protected toggleActive(customer: Customer): void {
    if (this.toggling()) {
      return;
    }

    this.toggling.set(true);
    this.toggleError.set(null);
    this.toggleDone.set(null);
    this.customers
      .setActive(customer.id, !customer.isActive)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.toggling.set(false);
          this.state.set({ kind: 'loaded', customer: updated });
          // Said aloud too, because the badge and button label change silently for a screen reader.
          this.toggleDone.set(updated.isActive ? 'Customer activated.' : 'Customer deactivated.');
        },
        error: (error: unknown) => {
          this.toggling.set(false);
          this.toggleError.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'This customer no longer exists.'
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
    this.customers
      .get(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (customer) => this.state.set({ kind: 'loaded', customer }),
        error: (error: unknown) =>
          this.state.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? { kind: 'notFound' }
              : { kind: 'error', detail: problemDetail(error) },
          ),
      });
  }
}
