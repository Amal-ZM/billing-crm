import { Component, DestroyRef, forwardRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, FormControl, NG_VALUE_ACCESSOR, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { EMPTY, catchError, debounceTime, map, of, switchMap, tap } from 'rxjs';
import { ProductListItem } from '../../../core/products/product.models';
import { ProductService } from '../../../core/products/product.service';

const SEARCH_PAUSE_MS = 300;
const RESULTS_PER_SEARCH = 10;

/**
 * Chooses one **active** product by typing part of its name or SKU. Its value is the product's id (or null). It uses the product
 * list the app already has, which returns active products only (the default of that API for both roles), so it needs no endpoint
 * of its own, and it works as a normal form control (`formControlName`).
 */
@Component({
  selector: 'app-product-picker',
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatFormFieldModule, MatInputModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => ProductPickerComponent), multi: true }],
  template: `
    <mat-form-field appearance="outline" class="picker">
      <mat-label>Product</mat-label>
      <input
        matInput
        data-testid="product-search"
        aria-label="Search for an active product by name or SKU"
        dir="auto"
        autocomplete="off"
        [formControl]="search"
        [matAutocomplete]="auto"
        [attr.aria-invalid]="invalid() ? 'true' : null"
        [attr.aria-describedby]="invalid() ? errorId() : null"
        (blur)="onTouched()"
      />
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="display" (optionSelected)="choose($event.option.value)">
        @for (product of results(); track product.id) {
          <mat-option [value]="product">
            <span dir="auto">{{ product.name }}</span>
            <span class="sku" dir="ltr">{{ product.sku }}</span>
          </mat-option>
        }
        @if (noMatch()) {
          <mat-option disabled>No products match</mat-option>
        }
      </mat-autocomplete>
      @if (failed()) {
        <mat-hint data-testid="lookup-error" role="alert">The products could not be loaded. Keep typing to try again.</mat-hint>
      }
    </mat-form-field>
  `,
  styles: `
    .picker {
      width: 100%;
    }
    .sku {
      margin-inline-start: 8px;
      color: var(--mat-sys-on-surface-variant);
      font-size: 0.85em;
    }
  `,
})
export class ProductPickerComponent implements ControlValueAccessor {
  private readonly products = inject(ProductService);
  private readonly destroyRef = inject(DestroyRef);

  /** True while the form shows an error for this picker: the input is then marked invalid for screen readers and keyboard focus. */
  readonly invalid = input(false);

  /** The id of the element that shows the error message, so a screen reader reads it with the input. */
  readonly errorId = input<string | null>(null);

  protected readonly search = new FormControl<string | ProductListItem>('', { nonNullable: true });
  protected readonly results = signal<ProductListItem[]>([]);
  protected readonly noMatch = signal(false);
  protected readonly failed = signal(false);

  private chosen: ProductListItem | null = null;
  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    this.search.valueChanges
      .pipe(
        tap((value) => {
          // Typing after a choice means the choice is gone; a stale id must never stay in the form.
          if (typeof value === 'string' && this.chosen !== null) {
            this.chosen = null;
            this.onChange(null);
          }
        }),
        map((value) => (typeof value === 'string' ? value.trim() : '')),
        debounceTime(SEARCH_PAUSE_MS),
        // switchMap drops the previous request when a newer one starts, so a slow old answer can never replace a newer one.
        switchMap((text) => {
          if (text.length === 0) {
            this.results.set([]);
            this.noMatch.set(false);
            this.failed.set(false);
            return EMPTY;
          }

          // Active products only, asked for explicitly rather than relying on the list's default.
          return this.products.list({ q: text, status: 'active', page: 1, pageSize: RESULTS_PER_SEARCH }).pipe(
            tap((page) => {
              this.failed.set(false);
              this.results.set(page.items);
              this.noMatch.set(page.items.length === 0);
            }),
            catchError(() => {
              this.failed.set(true);
              this.results.set([]);
              this.noMatch.set(false);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  /** What the input shows for a chosen product: its name. */
  protected readonly display = (value: string | ProductListItem | null): string => (typeof value === 'string' ? value : (value?.name ?? ''));

  protected choose(product: ProductListItem): void {
    this.chosen = product;
    this.search.setValue(product, { emitEvent: false });
    this.onChange(product.id);
  }

  writeValue(id: string | null): void {
    // The form only ever clears this picker (a new line starts empty); a chosen product is shown by the picker itself.
    this.chosen = null;
    if (id === null || id === '') {
      this.search.setValue('', { emitEvent: false });
    }
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    if (disabled) {
      this.search.disable({ emitEvent: false });
    } else {
      this.search.enable({ emitEvent: false });
    }
  }
}
