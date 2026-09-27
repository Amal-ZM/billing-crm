import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { AdminService } from '../../core/admin/admin.service';

@Component({
  selector: 'app-admin',
  imports: [MatCardModule],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>Administration</mat-card-title>
        <mat-card-subtitle>Admin-only area (no content yet)</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        <p data-testid="admin-status" role="status" aria-live="polite">{{ message() }}</p>
      </mat-card-content>
    </mat-card>
  `,
})
export class AdminComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly message = signal('Checking admin access…');

  ngOnInit(): void {
    this.admin
      .getStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => this.message.set(`Admin API status: ${result.status}`),
        error: (error: unknown) =>
          this.message.set(
            error instanceof HttpErrorResponse && error.status !== 0
              ? `The admin API refused the request (HTTP ${error.status}).`
              : 'Cannot reach the service.',
          ),
      });
  }
}
