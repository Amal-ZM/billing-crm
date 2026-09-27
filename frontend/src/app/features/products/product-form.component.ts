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
import { applyProblemToForm, serverMessage } from '../../core/http/problem-details';
import { PRODUCT_UNITS, ProductInput, ProductUnit } from '../../core/products/product.models';
import { ProductService } from '../../core/products/product.service';

const MAX_PRICE = 999_999_999.99;
const DEFAULT_VAT_PERCENT = '15';

/** Whitespace alone counts as empty. */
const notBlank: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  typeof control.value === 'string' && control.value.trim().length > 0 ? null : { required: true };

/** A length limit that counts the trimmed text, as the server does, so padding never causes a refusal. */
const maxTrimmed =
  (max: number): ValidatorFn =>
  (control: AbstractControl): ValidationErrors | null =>
    typeof control.value === 'string' && control.value.trim().length > max ? { maxlength: { requiredLength: max } } : null;

/** Letters A-Z, digits, hyphen and underscore, as the server requires (it upper-cases the letters). */
const skuCharacters: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const text = typeof control.value === 'string' ? control.value.trim() : '';
  return text.length === 0 || /^[A-Za-z0-9_-]+$/.test(text) ? null : { skuCharacters: true };
};

const NUMBER_SHAPE = /^-?\d+(\.\d+)?$/;

/** The text of an amount, checked as text so no floating-point rounding can hide a third decimal place. */
function amountProblem(control: AbstractControl): 'required' | 'notNumber' | 'negative' | 'decimals' | null {
  const text = typeof control.value === 'string' ? control.value.trim() : '';
  if (text.length === 0) {
    return 'required';
  }
  if (!NUMBER_SHAPE.test(text)) {
    return 'notNumber';
  }
  if (text.startsWith('-') && Number(text) !== 0) {
    return 'negative';
  }
  const decimals = text.split('.')[1];
  return decimals !== undefined && decimals.length > 2 && !/^0*$/.test(decimals.slice(2)) ? 'decimals' : null;
}

/** Unit price: an amount from 0 to 999,999,999.99 with at most two decimal places. */
const unitPrice: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const problem = amountProblem(control);
  if (problem) {
    return { [problem]: true };
  }
  return Number((control.value as string).trim()) > MAX_PRICE ? { maxPrice: true } : null;
};

/** VAT percent: a percentage from 0 to 100 with at most two decimal places. Left empty it means the default, 15. */
const vatPercent: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  if (typeof control.value === 'string' && control.value.trim().length === 0) {
    return null;
  }
  const problem = amountProblem(control);
  if (problem === 'negative') {
    return { range: true };
  }
  if (problem) {
    return { [problem]: true };
  }
  return Number((control.value as string).trim()) > 100 ? { range: true } : null;
};

const LABELS = {
  name: 'Name',
  sku: 'SKU',
  unit: 'Unit',
  unitPrice: 'Unit price',
  vatPercent: 'VAT percent',
} as const;

type Field = keyof typeof LABELS;

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

/** One form for both adding (`/products/new`) and editing (`/products/:id/edit`). Both routes are Admin only. */
@Component({
  selector: 'app-product-form',
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
        <h1 mat-card-title>{{ productId ? 'Edit product' : 'Add product' }}</h1>
      </mat-card-header>
      <mat-card-content>
        @switch (loadState()) {
          @case ('loading') {
            <p role="status" data-testid="form-loading"><mat-spinner diameter="20" aria-hidden="true" /> Loading product…</p>
          }
          @case ('notFound') {
            <p role="alert">Product not found. It may have been opened from an old link.</p>
            <a mat-button routerLink="/products">Back to products</a>
          }
          @case ('error') {
            <p role="alert" data-testid="load-error">The product could not be loaded.</p>
            <button mat-button type="button" (click)="load()">Retry</button>
            <a mat-button routerLink="/products">Back to products</a>
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
            <mat-label>SKU</mat-label>
            <input matInput formControlName="sku" dir="ltr" autocomplete="off" required />
            <mat-hint>Letters, digits, hyphens and underscores. Saved in upper case.</mat-hint>
            @if (errorFor('sku'); as message) {
              <mat-error>{{ message }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Unit</mat-label>
            <select matNativeControl formControlName="unit" required>
              @for (unit of units; track unit) {
                <option [value]="unit">{{ unit }}</option>
              }
            </select>
            @if (errorFor('unit'); as message) {
              <mat-error>{{ message }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Unit price (SAR)</mat-label>
            <input matInput formControlName="unitPrice" inputmode="decimal" dir="ltr" autocomplete="off" required />
            @if (errorFor('unitPrice'); as message) {
              <mat-error>{{ message }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>VAT percent</mat-label>
            <input
              matInput
              formControlName="vatPercent"
              inputmode="decimal"
              dir="ltr"
              autocomplete="off"
              [required]="!!productId"
            />
            @if (!productId) {
              <mat-hint>Leave empty for 15.</mat-hint>
            }
            @if (errorFor('vatPercent'); as message) {
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
            <a mat-button [routerLink]="productId ? ['/products', productId] : ['/products']">Cancel</a>
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
export class ProductFormComponent implements OnInit {
  private readonly products = inject(ProductService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Set when editing (`/products/:id/edit`); null when adding. */
  protected readonly productId = this.route.snapshot.paramMap.get('id');

  protected readonly units = PRODUCT_UNITS;
  // When editing, show "loading" from the first frame so an empty form never flashes.
  protected readonly loadState = signal<LoadState>(this.productId ? 'loading' : 'ready');
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  // The two amounts are held as text so what was typed is exactly what is checked; they become numbers only
  // when sent, and are never rounded on the way.
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [notBlank, maxTrimmed(200)]],
    sku: ['', [notBlank, maxTrimmed(50), skuCharacters]],
    unit: ['Hour' as ProductUnit, [Validators.required]],
    unitPrice: ['', [unitPrice]],
    vatPercent: [DEFAULT_VAT_PERCENT, [vatPercent]],
  });

  ngOnInit(): void {
    if (this.productId) {
      // An edit has no default: the VAT percent must be there, so an edit never resets it silently.
      this.form.controls.vatPercent.addValidators(notBlank);
      this.load();
    }
  }

  /** Editing only: fetch the product and fill the form. */
  protected load(): void {
    if (!this.productId) {
      return;
    }

    this.loadState.set('loading');
    this.products
      .get(this.productId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (product) => {
          this.form.reset({
            name: product.name,
            sku: product.sku,
            unit: product.unit,
            unitPrice: product.unitPrice.toFixed(2),
            vatPercent: String(product.vatPercent),
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
    if (control.hasError('skuCharacters')) {
      return 'SKU can only contain letters A-Z, digits, hyphens and underscores.';
    }
    if (control.hasError('notNumber')) {
      return `${label} must be a number.`;
    }
    if (control.hasError('negative')) {
      return `${label} cannot be negative.`;
    }
    if (control.hasError('maxPrice')) {
      return 'Unit price cannot be more than 999,999,999.99.';
    }
    if (control.hasError('range')) {
      return 'VAT percent must be between 0 and 100.';
    }
    if (control.hasError('decimals')) {
      return `${label} can have at most two decimal places.`;
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
    const vat = value.vatPercent.trim();
    const input: ProductInput = {
      name: value.name.trim(),
      sku: value.sku.trim(),
      unit: value.unit,
      unitPrice: Number(value.unitPrice.trim()),
      // Left empty on a new product: send nothing, and the server applies the default of 15.
      ...(vat.length > 0 ? { vatPercent: Number(vat) } : {}),
    };

    this.saving.set(true);
    // takeUntilDestroyed: leaving the page while saving must not navigate the person somewhere unexpected.
    const request = this.productId ? this.products.update(this.productId, input) : this.products.create(input);
    request
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (product) => {
          this.saving.set(false);
          // replaceUrl: Back from the details page should not return to a form that has already been saved.
          void this.router.navigate(['/products', product.id], { replaceUrl: true });
        },
        error: (error: unknown) => {
          // Everything the person typed stays in the form; the message goes on the field it belongs to.
          this.saving.set(false);
          this.formError.set(applyProblemToForm(this.form, error, { conflictField: 'sku', subject: 'product' }));
          this.focusFirstInvalid();
        },
      });
  }

  /** After a refusal, move focus to the first field in error so keyboard and screen-reader users land on it. */
  private focusFirstInvalid(): void {
    setTimeout(() =>
      this.host.nativeElement
        .querySelector<HTMLElement>('input[aria-invalid="true"], select[aria-invalid="true"]')
        ?.focus(),
    );
  }
}
