import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../auth/auth.service';

@Component({
  selector: 'app-shell',
  imports: [MatButtonModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <mat-toolbar class="app-toolbar">
      <span>BillingCRM</span>
      <nav aria-label="Main">
        @if (auth.email()) {
          <a mat-button routerLink="/customers" routerLinkActive="active-link" ariaCurrentWhenActive="page">Customers</a>
          <a mat-button routerLink="/products" routerLinkActive="active-link" ariaCurrentWhenActive="page">Products</a>
          <a mat-button routerLink="/invoices" routerLinkActive="active-link" ariaCurrentWhenActive="page">Invoices</a>
        }
        @if (auth.role() === 'Admin') {
          <a mat-button routerLink="/admin" routerLinkActive="active-link">Administration</a>
        }
      </nav>
      <span class="spacer"></span>
      @if (auth.email(); as email) {
        <span class="user" data-testid="user-info">{{ email }} · {{ auth.role() }}</span>
        <button mat-button type="button" data-testid="sign-out" (click)="signOut()">Sign out</button>
      }
    </mat-toolbar>
    <main class="content">
      <router-outlet />
    </main>
  `,
  styles: `
    .app-toolbar {
      --mat-toolbar-container-background-color: var(--mat-sys-primary);
      --mat-toolbar-container-text-color: var(--mat-sys-on-primary);
      --mat-button-text-label-text-color: var(--mat-sys-on-primary);
      gap: 16px;
    }
    .active-link {
      text-decoration: underline;
    }
    .spacer {
      flex: 1 1 auto;
    }
    .user {
      margin-right: 0;
    }
    .content {
      padding: 24px;
    }
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected signOut(): void {
    this.auth.signOut();
    // replaceUrl: the Back button must not return to a protected page's history entry as if still signed in.
    void this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
