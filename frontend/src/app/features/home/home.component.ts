import { HttpErrorResponse } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { DashboardSummary } from '../../core/dashboard/dashboard.models';
import { DashboardService } from '../../core/dashboard/dashboard.service';
import { HealthService } from '../../core/health/health.service';

type ApiState =
  | { kind: 'loading' }
  | { kind: 'reported'; status: string; healthy: boolean }
  | { kind: 'unreachable' }
  | { kind: 'failed'; httpStatus: number };

/** The Home dashboard's own state (spec 011): independent of the health card above it, and never blocks or hides it. */
type DashboardState = { kind: 'loading' } | { kind: 'loaded'; summary: DashboardSummary } | { kind: 'unavailable' };

@Component({
  selector: 'app-home',
  imports: [DecimalPipe, MatCardModule, MatProgressSpinnerModule, RouterLink],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>Welcome to BillingCRM</mat-card-title>
        <mat-card-subtitle>Internal staff CRM for customers, products and invoices</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @let current = state();
        <p data-testid="api-status" role="status" aria-live="polite" [class.problem]="isProblem()">
          @if (current.kind === 'loading') {
            <mat-spinner diameter="20" class="inline-spinner" aria-hidden="true" /> Checking API…
          } @else if (current.kind === 'reported') {
            API status: {{ current.status }}
          } @else if (current.kind === 'failed') {
            Could not read the API status (HTTP {{ current.httpStatus }}).
          } @else {
            API unreachable. Start the API and reload this page.
          }
        </p>
      </mat-card-content>
    </mat-card>

    <!-- The invoice dashboard (spec 011): a quiet status card, like the one above. It never blocks Home and never shows an error banner
         or a Retry button — the invoice list is always still one click away even if this card cannot load. -->
    <mat-card class="dashboard">
      @let dashboard = dashboardState();
      @if (dashboard.kind === 'loading') {
        <p data-testid="dashboard-loading" role="status"><mat-spinner diameter="20" class="inline-spinner" aria-hidden="true" /> Loading dashboard…</p>
      } @else if (dashboard.kind === 'unavailable') {
        <p data-testid="dashboard-unavailable" role="status">Dashboard unavailable.</p>
      } @else {
        @let summary = dashboard.summary;
        <!-- Each card is a plain link to the invoice list, already narrowed to that card's own set (spec 011, contracts/dashboard-api.md).
             The Unpaid card and the unpaid amount card lead to the same set: every Sent invoice, on-time and overdue alike. -->
        <div class="cards">
          <a class="stat" data-testid="dashboard-draft" [routerLink]="['/invoices']" [queryParams]="{ status: 'Draft' }">
            <span class="value">{{ summary.draftCount }}</span>
            <span class="label">Draft</span>
          </a>
          <a class="stat" data-testid="dashboard-unpaid" [routerLink]="['/invoices']" [queryParams]="{ status: 'Sent' }">
            <span class="value">{{ summary.unpaidCount }}</span>
            <span class="label">Unpaid</span>
          </a>
          <a class="stat" data-testid="dashboard-overdue" [routerLink]="['/invoices']" [queryParams]="{ status: 'Sent', overdue: 'true' }">
            <span class="value">{{ summary.overdueCount }}</span>
            <span class="label">Overdue</span>
          </a>
          <a class="stat" data-testid="dashboard-paid-month" [routerLink]="['/invoices']" [queryParams]="{ status: 'Paid', paidMonth: 'true' }">
            <span class="value">{{ summary.paidThisMonthCount }}</span>
            <span class="label">Paid this month</span>
          </a>
          <a class="stat" data-testid="dashboard-unpaid-amount" [routerLink]="['/invoices']" [queryParams]="{ status: 'Sent' }">
            <span class="value" dir="ltr">SAR {{ summary.unpaidAmount | number: '1.2-2' }}</span>
            <span class="label">Unpaid amount</span>
          </a>
        </div>
      }
    </mat-card>
  `,
  styles: `
    .inline-spinner {
      display: inline-block;
      vertical-align: middle;
      margin-right: 8px;
    }
    .problem {
      color: var(--mat-sys-error);
      font-weight: 500;
    }
    .dashboard {
      margin-top: 16px;
    }
    .cards {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
    }
    .stat {
      display: flex;
      flex-direction: column;
      min-width: 8rem;
      padding: 8px 12px;
      color: inherit;
      text-decoration: none;
      border-radius: 4px;
    }
    .stat:hover,
    .stat:focus-visible {
      background: var(--mat-sys-surface-variant);
    }
    .stat .value {
      font-size: 1.5rem;
      font-weight: 700;
    }
    .stat .label {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class HomeComponent implements OnInit {
  private readonly healthService = inject(HealthService);
  private readonly dashboardService = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly state = signal<ApiState>({ kind: 'loading' });
  protected readonly dashboardState = signal<DashboardState>({ kind: 'loading' });

  protected isProblem(): boolean {
    const current = this.state();
    return current.kind === 'loading' ? false : !(current.kind === 'reported' && current.healthy);
  }

  ngOnInit(): void {
    this.healthService
      .getHealth()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (health) =>
          this.state.set({ kind: 'reported', status: health.status, healthy: health.status === 'Healthy' }),
        error: (error: unknown) =>
          this.state.set(
            error instanceof HttpErrorResponse && error.status !== 0
              ? { kind: 'failed', httpStatus: error.status }
              : { kind: 'unreachable' },
          ),
      });

    this.dashboardService
      .summary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => this.dashboardState.set({ kind: 'loaded', summary }),
        error: () => this.dashboardState.set({ kind: 'unavailable' }),
      });
  }
}
