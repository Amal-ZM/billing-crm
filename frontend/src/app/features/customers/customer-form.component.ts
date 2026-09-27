import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CUSTOMER_TYPES, CustomerType } from '../../core/customers/customer.models';
import { CustomerService } from '../../core/customers/customer.service';
import { applyProblemToForm, serverMessage } from '../../core/http/problem-details';

/** Whitespace alone counts as empty. */
const notBlank: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  typeof control.value === 'string' && control.value.trim().length > 0 ? null : { required: true };

/** A length limit that counts the trimmed text, as the server does, so padding never causes a refusal. */
const maxTrimmed =
  (max: number): ValidatorFn =>
  (control: AbstractControl): ValidationErrors | null =>
    typeof control.value === 'string' && control.value.trim().length > max ? { maxlength: { requiredLength: max } } : null;

const LABELS = {
  name: 'Name',
  type: 'Type',
  email: 'Email',
  phone: 'Phone',
  vatNumber: 'VAT number',
  address: 'Address',
} as const;

type Field = keyof typeof LABELS;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

/** One form for both adding (`/customers/new`) and editing (`/customers/:id/edit`). */
@Component({
  selector: 'app-customer-form',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RouterLink,
  ],
  template: `
    <mat-card>
      <mat-card-header>
        <h1 mat-card-title>{{ customerId ? 'Edit customer' : 'Add customer' }}</h1>
      </mat-card-header>
      <mat-card-content>
        @switch (loadState()) {
          @case ('loading') {
            <p role="status" data-testid="form-loading"><mat-spinner diameter="20" aria-hidden="true" /> Loading customer…</p>
          }
          @case ('notFound') {
            <p role="alert">Customer not found. It may have been opened from an old link.</p>
            <a mat-button routerLink="/customers">Back to customers</a>
          }
          @case ('error') {
            <p role="alert" data-testid="load-error">The customer could not be loaded.</p>
            <button mat-button type="button" (click)="load()">Retry</button>
            <a mat-button routerLink="/customers">Back to customers</a>
          }
          @default {
            <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
              <mat-form-field appearance="outline">
                <mat-label>Name</mat-label>
                <input matInput formControlName="name" dir="auto" autocomplete="off" required />
                @if (errorFor('name'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Type</mat-label>
                <select matNativeControl formControlName="type" required>
                  @for (type of types; track type) {
                    <option [value]="type">{{ type }}</option>
                  }
                </select>
                @if (errorFor('type'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Email</mat-label>
                <input matInput type="email" formControlName="email" dir="auto" autocomplete="off" required />
                @if (errorFor('email'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Phone</mat-label>
                <input matInput type="tel" formControlName="phone" dir="auto" autocomplete="off" required />
                @if (errorFor('phone'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>VAT number (optional)</mat-label>
                <input matInput formControlName="vatNumber" dir="auto" autocomplete="off" />
                @if (errorFor('vatNumber'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Address</mat-label>
                <textarea matInput formControlName="address" dir="auto" rows="3" required></textarea>
                @if (errorFor('address'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>

              @if (formError(); as message) {
                <p class="error" role="alert" data-testid="form-error">{{ message }}</p>
              }

              <div class="actions">
                <button mat-flat-button type="submit" [disabled]="saving()">
                  @if (saving()) {
                    <mat-spinner diameter="18" aria-hidden="true" />
                  }
                  Save
                </button>
                <a mat-button [routerLink]="customerId ? ['/customers', customerId] : ['/customers']">Cancel</a>
              </div>
            </form>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    mat-card {
      max-width: 640px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding-top: 16px;
    }
    .error {
      color: var(--mat-sys-error);
      margin: 0 0 8px;
    }
    .actions {
      display: flex;
      gap: 8px;
    }
    button mat-spinner {
      display: inline-block;
      margin-right: 8px;
    }
  `,
})
export class CustomerFormComponent implements OnInit {
  private readonly customers = inject(CustomerService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Set when editing (`/customers/:id/edit`); null when adding. */
  protected readonly customerId = this.route.snapshot.paramMap.get('id');

  protected readonly types = CUSTOMER_TYPES;
  // When editing, show "loading" from the first frame so an empty form never flashes.
  protected readonly loadState = signal<LoadState>(this.customerId ? 'loading' : 'ready');
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [notBlank, maxTrimmed(200)]],
    type: ['Company' as CustomerType, [Validators.required]],
    email: ['', [notBlank, maxTrimmed(256), Validators.email]],
    phone: ['', [notBlank, Validators.minLength(7), maxTrimmed(20)]],
    vatNumber: ['', [maxTrimmed(30)]],
    address: ['', [notBlank, maxTrimmed(500)]],
  });

  ngOnInit(): void {
    if (this.customerId) {
      this.load();
    }
  }

  /** Editing only: fetch the customer and fill the form. */
  protected load(): void {
    if (!this.customerId) {
      return;
    }

    this.loadState.set('loading');
    this.customers
      .get(this.customerId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (customer) => {
          this.form.reset({
            name: customer.name,
            type: customer.type,
            email: customer.email,
            phone: customer.phone,
            vatNumber: customer.vatNumber ?? '',
            address: customer.address,
          });
          this.loadState.set('ready');
        },
        error: (error: unknown) =>
          this.loadState.set(error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'error'),
      });
  }

  /** The message to show under a field, or null. A message from the API wins over local ones. */
  protected errorFor(field: Field): string | null {
    const control = this.form.controls[field];
    const label = LABELS[field];
    const fromServer = serverMessage(control);
    if (fromServer) {
      return fromServer;
    }
    if (control.hasError('required')) {
      return `${label} is required.`;
    }
    const maxlength = control.errors?.['maxlength'];
    if (maxlength) {
      return `${label} must be at most ${maxlength.requiredLength} characters.`;
    }
    if (control.hasError('minlength')) {
      return 'Phone must be 7 to 20 characters.';
    }
    if (control.hasError('email')) {
      return 'Email must be a valid email address.';
    }
    return null;
  }

  protected submit(): void {
    if (this.saving()) {
      return;
    }

    this.formError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    const value = this.form.getRawValue();
    const input = {
      name: value.name.trim(),
      type: value.type,
      email: value.email.trim(),
      phone: value.phone.trim(),
      vatNumber: value.vatNumber.trim(),
      address: value.address.trim(),
    };

    this.saving.set(true);
    const request = this.customerId ? this.customers.update(this.customerId, input) : this.customers.create(input);
    // takeUntilDestroyed: leaving the page while saving must not navigate the person somewhere unexpected.
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (customer) => {
        this.saving.set(false);
        // replaceUrl: Back from the details page should not return to a form that has already been saved.
        void this.router.navigate(['/customers', customer.id], { replaceUrl: true });
      },
      error: (error: unknown) => {
        // Everything the person typed stays in the form; the message goes on the field it belongs to.
        this.saving.set(false);
        this.formError.set(applyProblemToForm(this.form, error));
        this.focusFirstInvalid();
      },
    });
  }

  /** After a refusal, move focus to the first field in error so keyboard and screen-reader users land on it. */
  private focusFirstInvalid(): void {
    setTimeout(() =>
      this.host.nativeElement
        .querySelector<HTMLElement>('input[aria-invalid="true"], select[aria-invalid="true"], textarea[aria-invalid="true"]')
        ?.focus(),
    );
  }
}
