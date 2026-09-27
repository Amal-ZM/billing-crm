import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CustomerListItem, PagedResult } from '../../../core/customers/customer.models';
import { CustomerService } from '../../../core/customers/customer.service';
import { CustomerPickerComponent } from './customer-picker.component';

const abc: CustomerListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'ABC',
  type: 'Company',
  email: 'accounts@abc.example',
  phone: '0500000000',
  vatNumber: null,
  isActive: true,
};

const page = (items: CustomerListItem[]): PagedResult<CustomerListItem> => ({
  items,
  page: 1,
  pageSize: 10,
  totalCount: items.length,
  totalPages: items.length === 0 ? 0 : 1,
});

describe('CustomerPickerComponent', () => {
  function setup(list: ReturnType<typeof vi.fn>) {
    TestBed.configureTestingModule({
      imports: [CustomerPickerComponent],
      providers: [{ provide: CustomerService, useValue: { list } }],
    });
    const fixture = TestBed.createComponent(CustomerPickerComponent);
    const changes: (string | null)[] = [];
    fixture.componentInstance.registerOnChange((value: string | null) => changes.push(value));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector('input[data-testid="customer-search"]') as HTMLInputElement;
    const type = (text: string) => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
    };
    return { fixture, root, input, type, changes, component: fixture.componentInstance as unknown as Record<string, () => unknown> };
  }

  afterEach(() => vi.useRealTimers());

  it('has an accessible label and marks the text as automatic direction for Arabic', () => {
    const { input } = setup(vi.fn().mockReturnValue(of(page([]))));

    expect(input.getAttribute('aria-label')).toBeTruthy();
    expect(input.getAttribute('dir')).toBe('auto');
  });

  it('waits for a pause in typing, then asks the existing customer list for active customers matching the text', () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValue(of(page([abc])));
    const { type } = setup(list);

    type('ab');
    vi.advanceTimersByTime(299);
    expect(list).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(list).toHaveBeenCalledTimes(1);
    expect(list).toHaveBeenCalledWith({ q: 'ab', active: true, page: 1, pageSize: 10 });
  });

  it('asks only once for the final text when typing continues within the pause', () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValue(of(page([])));
    const { type } = setup(list);

    type('a');
    vi.advanceTimersByTime(200);
    type('ab');
    vi.advanceTimersByTime(200);
    type('abc');
    vi.advanceTimersByTime(300);

    expect(list).toHaveBeenCalledTimes(1);
    expect(list).toHaveBeenCalledWith(expect.objectContaining({ q: 'abc' }));
  });

  it('shows the customers it found and never shows an inactive one (it only asks for active customers)', () => {
    vi.useFakeTimers();
    const { type, component } = setup(vi.fn().mockReturnValue(of(page([abc]))));

    type('ab');
    vi.advanceTimersByTime(300);

    expect((component['results'] as () => CustomerListItem[])().map((c) => c.name)).toEqual(['ABC']);
    expect((component['noMatch'] as () => boolean)()).toBe(false);
  });

  it('says there is no match when nothing is found, and keeps the typed text', () => {
    vi.useFakeTimers();
    const { type, input, component } = setup(vi.fn().mockReturnValue(of(page([]))));

    type('zzz');
    vi.advanceTimersByTime(300);

    expect((component['noMatch'] as () => boolean)()).toBe(true);
    expect(input.value).toBe('zzz');
  });

  it('never replaces a newer answer with a slow older one', () => {
    vi.useFakeTimers();
    const slow = new Subject<PagedResult<CustomerListItem>>();
    const list = vi.fn().mockReturnValueOnce(slow).mockReturnValueOnce(of(page([abc])));
    const { type, component } = setup(list);

    type('a');
    vi.advanceTimersByTime(300);
    type('ab');
    vi.advanceTimersByTime(300);
    slow.next(page([{ ...abc, name: 'STALE' }]));

    expect((component['results'] as () => CustomerListItem[])().map((c) => c.name)).toEqual(['ABC']);
  });

  it('shows a message when the lookup fails and lets the person try again by typing', () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 }))).mockReturnValueOnce(of(page([abc])));
    const { type, root, component } = setup(list);

    type('ab');
    vi.advanceTimersByTime(300);
    expect((component['failed'] as () => boolean)()).toBe(true);

    type('abc');
    vi.advanceTimersByTime(300);
    expect((component['failed'] as () => boolean)()).toBe(false);
    expect(root.querySelector('[data-testid="lookup-error"]')).toBeNull();
  });

  it('choosing a customer reports its id and shows its name', async () => {
    const { component, changes, input, fixture } = setup(vi.fn().mockReturnValue(of(page([abc]))));

    (component['choose'] as unknown as (c: CustomerListItem) => void)(abc);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(changes).toEqual([abc.id]);
    expect(input.value).toBe('ABC');
  });

  it('typing again after choosing clears the chosen customer, so a stale id is never kept', () => {
    vi.useFakeTimers();
    const { component, changes, type } = setup(vi.fn().mockReturnValue(of(page([]))));
    (component['choose'] as unknown as (c: CustomerListItem) => void)(abc);

    type('somebody else');

    expect(changes).toEqual([abc.id, null]);
  });

  it('shows a customer set from outside by name (the edit form passes the current one)', async () => {
    const { fixture, input } = setup(vi.fn().mockReturnValue(of(page([]))));
    fixture.componentRef.setInput('displayName', 'ABC');

    fixture.componentInstance.writeValue(abc.id);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(input.value).toBe('ABC');
  });

  it('shows the name when it arrives after the id was set (the edit form loads the invoice first)', async () => {
    const { fixture, input } = setup(vi.fn().mockReturnValue(of(page([]))));

    fixture.componentInstance.writeValue(abc.id);
    fixture.detectChanges();
    expect(input.value).toBe('');

    fixture.componentRef.setInput('displayName', 'ABC');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(input.value).toBe('ABC');
  });

  it('typing over a customer that was set from outside clears its id too', () => {
    vi.useFakeTimers();
    const { fixture, changes, type } = setup(vi.fn().mockReturnValue(of(page([]))));
    fixture.componentInstance.writeValue(abc.id);

    type('somebody else');

    expect(changes).toEqual([null]);
  });

  it('is cleared when the form resets it', async () => {
    const { fixture, input, component } = setup(vi.fn().mockReturnValue(of(page([abc]))));
    (component['choose'] as unknown as (c: CustomerListItem) => void)(abc);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(input.value).toBe('ABC');

    fixture.componentInstance.writeValue(null);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(input.value).toBe('');
  });

  it('can be disabled by the form', () => {
    const { fixture, input } = setup(vi.fn().mockReturnValue(of(page([]))));

    fixture.componentInstance.setDisabledState(true);
    fixture.detectChanges();

    expect(input.disabled).toBe(true);
  });
});
