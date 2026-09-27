import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PagedResult, ProductListItem } from '../../../core/products/product.models';
import { ProductService } from '../../../core/products/product.service';
import { ProductPickerComponent } from './product-picker.component';

const consulting: ProductListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Consulting hour',
  sku: 'CNS-001',
  unit: 'Hour',
  unitPrice: 100,
  vatPercent: 15,
  isActive: true,
};

const page = (items: ProductListItem[]): PagedResult<ProductListItem> => ({
  items,
  page: 1,
  pageSize: 10,
  totalCount: items.length,
  totalPages: items.length === 0 ? 0 : 1,
});

describe('ProductPickerComponent', () => {
  function setup(list: ReturnType<typeof vi.fn>) {
    TestBed.configureTestingModule({
      imports: [ProductPickerComponent],
      providers: [{ provide: ProductService, useValue: { list } }],
    });
    const fixture = TestBed.createComponent(ProductPickerComponent);
    const changes: (string | null)[] = [];
    fixture.componentInstance.registerOnChange((value: string | null) => changes.push(value));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector('input[data-testid="product-search"]') as HTMLInputElement;
    const type = (text: string) => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
    };
    const member = fixture.componentInstance as unknown as Record<string, unknown>;
    return { fixture, root, input, type, changes, member };
  }

  const choose = (member: Record<string, unknown>, item: ProductListItem) => (member['choose'] as (p: ProductListItem) => void)(item);
  const results = (member: Record<string, unknown>) => (member['results'] as () => ProductListItem[])();

  afterEach(() => vi.useRealTimers());

  it('has an accessible label and marks the text as automatic direction for Arabic names', () => {
    const { input } = setup(vi.fn().mockReturnValue(of(page([]))));

    expect(input.getAttribute('aria-label')).toBeTruthy();
    expect(input.getAttribute('dir')).toBe('auto');
  });

  it('waits for a pause in typing, then asks the existing product list (which returns active products only) for the text', () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValue(of(page([consulting])));
    const { type } = setup(list);

    type('cns');
    vi.advanceTimersByTime(299);
    expect(list).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(list).toHaveBeenCalledTimes(1);
    expect(list).toHaveBeenCalledWith({ q: 'cns', status: 'active', page: 1, pageSize: 10 });
  });

  it('asks only once for the final text when typing continues within the pause', () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValue(of(page([])));
    const { type } = setup(list);

    type('c');
    vi.advanceTimersByTime(200);
    type('cn');
    vi.advanceTimersByTime(200);
    type('cns');
    vi.advanceTimersByTime(300);

    expect(list).toHaveBeenCalledTimes(1);
    expect(list).toHaveBeenCalledWith(expect.objectContaining({ q: 'cns' }));
  });

  it('shows what it found with the name and the SKU', () => {
    vi.useFakeTimers();
    const { type, member } = setup(vi.fn().mockReturnValue(of(page([consulting]))));

    type('cns');
    vi.advanceTimersByTime(300);

    expect(results(member).map((p) => `${p.name} ${p.sku}`)).toEqual(['Consulting hour CNS-001']);
    expect((member['noMatch'] as () => boolean)()).toBe(false);
  });

  it('says there is no match when nothing is found, and keeps the typed text', () => {
    vi.useFakeTimers();
    const { type, input, member } = setup(vi.fn().mockReturnValue(of(page([]))));

    type('zzz');
    vi.advanceTimersByTime(300);

    expect((member['noMatch'] as () => boolean)()).toBe(true);
    expect(input.value).toBe('zzz');
  });

  it('never replaces a newer answer with a slow older one', () => {
    vi.useFakeTimers();
    const slow = new Subject<PagedResult<ProductListItem>>();
    const list = vi.fn().mockReturnValueOnce(slow).mockReturnValueOnce(of(page([consulting])));
    const { type, member } = setup(list);

    type('c');
    vi.advanceTimersByTime(300);
    type('cn');
    vi.advanceTimersByTime(300);
    slow.next(page([{ ...consulting, name: 'STALE' }]));

    expect(results(member).map((p) => p.name)).toEqual(['Consulting hour']);
  });

  it('shows a message when the lookup fails and recovers on the next search', () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 }))).mockReturnValueOnce(of(page([consulting])));
    const { type, member } = setup(list);

    type('cns');
    vi.advanceTimersByTime(300);
    expect((member['failed'] as () => boolean)()).toBe(true);

    type('cnsx');
    vi.advanceTimersByTime(300);
    expect((member['failed'] as () => boolean)()).toBe(false);
  });

  it('choosing a product reports its id and shows its name', async () => {
    const { member, changes, input, fixture } = setup(vi.fn().mockReturnValue(of(page([consulting]))));

    choose(member, consulting);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(changes).toEqual([consulting.id]);
    expect(input.value).toBe('Consulting hour');
  });

  it('typing again after choosing clears the chosen product, so a stale id is never kept', () => {
    vi.useFakeTimers();
    const { member, changes, type } = setup(vi.fn().mockReturnValue(of(page([]))));
    choose(member, consulting);

    type('something else');

    expect(changes).toEqual([consulting.id, null]);
  });

  it('is cleared when the form resets it and can be disabled by the form', async () => {
    const { fixture, input, member } = setup(vi.fn().mockReturnValue(of(page([consulting]))));
    choose(member, consulting);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(input.value).toBe('Consulting hour');

    fixture.componentInstance.writeValue(null);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(input.value).toBe('');

    fixture.componentInstance.setDisabledState(true);
    fixture.detectChanges();
    expect(input.disabled).toBe(true);
  });
});
