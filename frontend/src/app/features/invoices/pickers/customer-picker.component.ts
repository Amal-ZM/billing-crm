import { Component, DestroyRef, effect, forwardRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, FormControl, NG_VALUE_ACCESSOR, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { EMPTY, catchError, debounceTime, map, of, switchMap, tap } from 'rxjs';
import { CustomerListItem } from '../../../core/customers/customer.models';
import { CustomerService } from '../../../core/customers/customer.service';

const SEARCH_PAUSE_MS = 300;
const RESULTS_PER_SEARCH = 10;

/**
 * Chooses one **active** customer by typing part of its name, email, phone or VAT number. Its value is the customer's id (or
 * null). It uses the customer list the app already has (`active=true`), so it needs no endpoint of its own, and it works as a
 * normal form control (`formControlName`), so the invoice form's own validation and server errors apply to it.
 */
@Component({
  selector: 'app-customer-picker',
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatFormFieldModule, MatInputModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CustomerPickerComponent), multi: true }],
  template: `
    <mat-form-field appearance="outline" class="picker">
      <mat-label>Customer</mat-label>
      <input
        matInput
        data-testid="customer-search"
        aria-label="Search for an active customer by name, email, phone or VAT number"
        dir="auto"
        autocomplete="off"
        [formControl]="search"
        [matAutocomplete]="auto"
        [attr.aria-invalid]="invalid() ? 'true' : null"
        [attr.aria-describedby]="invalid() ? errorId() : null"
        (blur)="onTouched()"
      />
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="display" (optionSelected)="choose($event.option.value)">
        @for (customer of results(); track customer.id) {
          <mat-option [value]="customer">
            <span dir="auto">{{ customer.name }}</span>
          </mat-option>
        }
        @if (noMatch()) {
          <mat-option disabled>No customers match</mat-option>
        }
      </mat-autocomplete>
      @if (failed()) {
        <mat-hint data-testid="lookup-error" role="alert">The customers could not be loaded. Keep typing to try again.</mat-hint>
      }
    </mat-form-field>
  `,
  styles: `
    .picker {
      width: 100%;
    }
  `,
})
export class CustomerPickerComponent implements ControlValueAccessor {
  private readonly customers = inject(CustomerService);
  private readonly destroyRef = inject(DestroyRef);

  /** The name to show when the form sets an id from outside (the edit form knows the current customer's name). */
  readonly displayName = input<string | null>(null);

  /** True while the form shows an error for this picker: the input is then marked invalid for screen readers and keyboard focus. */
  readonly invalid = input(false);

  /** The id of the element that shows the error message, so a screen reader reads it with the input. */
  readonly errorId = input<string | null>(null);

  protected readonly search = new FormControl<string | CustomerListItem>('', { nonNullable: true });
  protected readonly results = signal<CustomerListItem[]>([]);
  protected readonly noMatch = signal(false);
  protected readonly failed = signal(false);

  /** The id the form set from outside (an edit form), until the person searches again; its name comes from displayName. */
  private externalId: string | null = null;

  private chosen: CustomerListItem | null = null;
  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    // The name can arrive after the id (the edit form loads the invoice, then shows its customer): show it when it does.
    effect(() => {
      const name = this.displayName();
      if (this.externalId !== null && name !== null) {
        this.search.setValue(name, { emitEvent: false });
      }
    });

    this.search.valueChanges
      .pipe(
        tap((value) => {
          // Typing after a choice means the choice is gone; a stale id must never stay in the form.
          if (typeof value === 'string' && (this.chosen !== null || this.externalId !== null)) {
            this.chosen = null;
            this.externalId = null;
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

          return this.customers.list({ q: text, active: true, page: 1, pageSize: RESULTS_PER_SEARCH }).pipe(
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

  /** What the input shows for a chosen customer: its name. */
  protected readonly display = (value: string | CustomerListItem | null): string => (typeof value === 'string' ? value : (value?.name ?? ''));

  protected choose(customer: CustomerListItem): void {
    this.chosen = customer;
    this.search.setValue(customer, { emitEvent: false });
    this.onChange(customer.id);
  }

  writeValue(id: string | null): void {
    this.chosen = null;
    this.externalId = id === null || id === '' ? null : id;
    if (id === null || id === '') {
      this.search.setValue('', { emitEvent: false });
      return;
    }

    // An id set from outside: show the name the form knows about, or nothing until the person searches again.
    this.search.setValue(this.displayName() ?? '', { emitEvent: false });
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
