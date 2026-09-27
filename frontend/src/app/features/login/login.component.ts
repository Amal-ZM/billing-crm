import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/** Only same-site paths are accepted, so a crafted link cannot bounce someone to another site after sign-in. */
function safeReturnUrl(value: string | null): string {
  return value && /^\/(?![/\\])/.test(value) ? value : '/';
}

/** An email made only of spaces counts as empty, so nothing is sent for verification. */
const notBlank: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  typeof control.value === 'string' && control.value.trim().length > 0 ? null : { required: true };

/** The first specific message in a ProblemDetails body, if the API sent one. */
function problemMessage(error: HttpErrorResponse): string | null {
  const body = error.error;
  const firstFieldError = body?.errors ? Object.values<string[]>(body.errors)[0]?.[0] : undefined;
  if (typeof firstFieldError === 'string') {
    return firstFieldError;
  }
  return typeof body?.detail === 'string' ? body.detail : null;
}

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <main class="login-page">
      <mat-card class="login-card">
        <mat-card-header>
          <h1 mat-card-title>BillingCRM</h1>
          <mat-card-subtitle>Sign in with your work email</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <mat-form-field appearance="outline">
              <mat-label>Email</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="username" />
              @if (form.controls.email.hasError('required')) {
                <mat-error>Email is required.</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput type="password" formControlName="password" autocomplete="current-password" />
              @if (form.controls.password.hasError('required')) {
                <mat-error>Password is required.</mat-error>
              }
            </mat-form-field>

            @if (expired) {
              <p class="notice" role="status" data-testid="login-expired">Your session expired. Sign in again.</p>
            }

            @if (error(); as message) {
              <p class="error" role="alert" data-testid="login-error">{{ message }}</p>
            }

            <button mat-flat-button type="submit" [disabled]="pending()">
              @if (pending()) {
                <mat-spinner diameter="18" aria-hidden="true" />
              }
              Sign in
            </button>
          </form>
        </mat-card-content>
      </mat-card>
    </main>
  `,
  styles: `
    .login-page {
      display: flex;
      justify-content: center;
      padding: 48px 16px;
    }
    .login-card {
      width: 100%;
      max-width: 400px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding-top: 16px;
    }
    .error {
      color: var(--mat-sys-error);
      margin: 0;
    }
    .notice {
      margin: 0;
    }
    button mat-spinner {
      display: inline-block;
      margin-right: 8px;
    }
  `,
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [notBlank]],
    password: ['', [Validators.required]],
  });
  protected readonly expired = this.route.snapshot.queryParamMap.get('reason') === 'expired';
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    if (this.auth.isSignedIn()) {
      // replaceUrl: the sign-in page must not stay in history for Back to bounce off.
      void this.router.navigateByUrl('/', { replaceUrl: true });
    }
  }

  protected submit(): void {
    if (this.pending()) {
      return;
    }

    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending.set(true);
    const { email, password } = this.form.getRawValue();
    this.auth.signIn(email.trim(), password).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')), {
          replaceUrl: true,
        });
      },
      error: (error: unknown) => {
        this.pending.set(false);
        this.error.set(this.messageFor(error));
      },
    });
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return 'Email or password is incorrect.';
      }
      if (error.status === 0) {
        return 'Cannot reach the service. Check that the API is running and try again.';
      }
      const detail = problemMessage(error);
      if (detail) {
        return detail;
      }
    }
    return 'Sign-in failed. Try again.';
  }
}
