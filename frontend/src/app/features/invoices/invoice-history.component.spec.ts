import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { HistoryEntry, InvoiceHistory } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';
import { InvoiceHistoryComponent } from './invoice-history.component';

const ID = '11111111-1111-1111-1111-111111111111';

const created: HistoryEntry = {
  id: 'e1',
  kind: 'Created',
  occurredAtUtc: '2030-06-01T10:00:00Z',
  actorEmail: 'agent1@crm.local',
  actorRole: 'Agent',
  changes: [
    { field: 'Customer', before: null, after: 'ABC' },
    { field: 'Due date', before: null, after: '2030-07-01' },
    { field: 'Line: Consulting hour (CNS-001)', before: null, after: '2.00 x SAR 100.00, VAT 15.00%' },
  ],
};

const edited: HistoryEntry = {
  id: 'e2',
  kind: 'DraftEdited',
  occurredAtUtc: '2030-06-01T11:00:00Z',
  actorEmail: 'admin@crm.local',
  actorRole: 'Admin',
  changes: [{ field: 'Notes', before: 'Net 30', after: null }],
};

const sentEntry: HistoryEntry = {
  id: 'e3',
  kind: 'Sent',
  occurredAtUtc: '2030-06-02T09:00:00Z',
  actorEmail: 'agent1@crm.local',
  actorRole: 'Agent',
  changes: [{ field: 'Status', before: 'Draft', after: 'Sent' }],
};

/** As the API returns it: newest first. */
const history: InvoiceHistory = { number: 'INV-000123', startsWithCreation: true, entries: [sentEntry, edited, created] };

function setup(historyCall: ReturnType<typeof vi.fn>, params: BehaviorSubject<ReturnType<typeof convertToParamMap>> = new BehaviorSubject(convertToParamMap({ id: ID }))) {
  TestBed.configureTestingModule({
    imports: [InvoiceHistoryComponent],
    providers: [
      provideRouter([]),
      // Only `history` exists: any other call (a get, a send, a cancel, a payment, an update) would throw, so the page provably only reads history.
      { provide: InvoiceService, useValue: { history: historyCall } },
      { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
    ],
  });
  const fixture: ComponentFixture<InvoiceHistoryComponent> = TestBed.createComponent(InvoiceHistoryComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement, params };
}

const has = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`) !== null;
const text = (root: HTMLElement, testId: string) => root.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim();
const all = (root: HTMLElement, testId: string) => Array.from(root.querySelectorAll(`[data-testid="${testId}"]`)) as HTMLElement[];

describe('InvoiceHistoryComponent states', () => {
  it('shows a loading message while the history is on its way', () => {
    const { root } = setup(vi.fn().mockReturnValue(new Subject<InvoiceHistory>()));

    expect(has(root, 'detail-loading')).toBe(true);
    expect(has(root, 'history-title')).toBe(false);
  });

  it('says the invoice was not found on a 404 and links back to the list', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('Invoice not found');
    expect(root.querySelector('a[href="/invoices"]')).not.toBeNull();
    expect(has(root, 'history-title')).toBe(false);
  });

  it('shows the API detail on another failure and Retry loads again', () => {
    const historyCall = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500, error: { detail: 'The server had a problem.' } })))
      .mockReturnValueOnce(of(history));
    const { fixture, root } = setup(historyCall);

    expect(text(root, 'detail-error')).toContain('The invoice history could not be loaded.');
    expect(text(root, 'detail-error')).toContain('The server had a problem.');

    (root.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(historyCall).toHaveBeenCalledTimes(2);
    expect(has(root, 'detail-error')).toBe(false);
    expect(has(root, 'history-title')).toBe(true);
  });

  it('says "Try again." when the failure carries no detail', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 }))));

    expect(text(root, 'detail-error')).toBe('The invoice history could not be loaded. Try again.');
  });
});

describe('InvoiceHistoryComponent loaded', () => {
  it('shows the heading with the number and a link back to the invoice', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    expect(text(root, 'history-title')).toContain('History');
    const number = root.querySelector('[data-testid="history-number"]');
    expect(number?.textContent?.trim()).toBe('INV-000123');
    expect(number?.getAttribute('dir')).toBe('ltr');
    expect(root.querySelector('[data-testid="back"]')?.getAttribute('href')).toBe(`/invoices/${ID}`);
  });

  it('lists the entries in the order the API returned them, newest first', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    expect(all(root, 'entry-kind').map((element) => element.textContent?.trim())).toEqual(['Sent', 'Draft edited', 'Created']);
    expect(all(root, 'history-entry')).toHaveLength(3);
  });

  it('shows the kind in words for each of the five kinds', () => {
    const kinds = (['Created', 'DraftEdited', 'Sent', 'Payment', 'Cancelled'] as const).map((kind, index) => ({ ...created, id: `k${index}`, kind }));
    const { root } = setup(vi.fn().mockReturnValue(of({ ...history, entries: kinds })));

    expect(all(root, 'entry-kind').map((element) => element.textContent?.trim())).toEqual(['Created', 'Draft edited', 'Sent', 'Payment', 'Cancelled']);
  });

  it('shows the time as a medium date, and the user as "email (Role)", both left to right', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    const time = all(root, 'entry-time')[0];
    expect(time.getAttribute('dir')).toBe('ltr');
    expect(time.textContent).toMatch(/2030/);
    expect(time.textContent).toMatch(/Jun/);
    const actors = all(root, 'entry-actor');
    expect(actors[0].textContent?.trim()).toBe('agent1@crm.local (Agent)');
    expect(actors[1].textContent?.trim()).toBe('admin@crm.local (Admin)');
    expect(actors.every((actor) => actor.getAttribute('dir') === 'ltr')).toBe(true);
  });

  it('shows one row per change with the field, the value before and the value after', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    const rows = all(root, 'change-row').map((row) => Array.from(row.querySelectorAll('th, td')).map((cell) => cell.textContent?.trim()));
    expect(rows).toEqual([
      ['Status', 'Draft', 'Sent'],
      ['Notes', 'Net 30', '—'],
      ['Customer', '—', 'ABC'],
      ['Due date', '—', '2030-07-01'],
      ['Line: Consulting hour (CNS-001)', '—', '2.00 x SAR 100.00, VAT 15.00%'],
    ]);
  });

  it('shows every value exactly as received: Arabic and English kept, markup shown as text, a 500-character value in full', () => {
    const long = 'x'.repeat(500);
    const entry: HistoryEntry = {
      ...created,
      changes: [
        { field: 'Notes', before: '<b>bold</b> & <script>alert(1)</script>', after: 'ملاحظة جديدة / new note' },
        { field: 'Line: خدمة (SVC-1)', before: null, after: long },
      ],
    };
    const { root } = setup(vi.fn().mockReturnValue(of({ ...history, entries: [entry] })));

    const rows = all(root, 'change-row');
    expect(rows[0].querySelectorAll('td')[0].textContent?.trim()).toBe('<b>bold</b> & <script>alert(1)</script>');
    expect(rows[0].querySelector('b')).toBeNull();
    expect(root.querySelector('script')).toBeNull();
    expect(rows[0].querySelectorAll('td')[1].textContent?.trim()).toBe('ملاحظة جديدة / new note');
    expect(rows[1].querySelector('th')?.textContent?.trim()).toBe('Line: خدمة (SVC-1)');
    expect(rows[1].querySelectorAll('td')[1].textContent?.trim()).toBe(long);
  });

  it('lets the browser choose the direction of each text value', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    const row = all(root, 'change-row')[0];
    expect(row.querySelector('th')?.getAttribute('dir')).toBe('auto');
    expect(Array.from(row.querySelectorAll('td')).every((cell) => cell.getAttribute('dir') === 'auto')).toBe(true);
  });

  it('shows no note when the history starts with the creation', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    expect(has(root, 'history-note')).toBe(false);
    expect(has(root, 'history-empty')).toBe(false);
  });

  it('shows the note when the history does not start with the creation and there are entries', () => {
    const { root } = setup(vi.fn().mockReturnValue(of({ number: 'INV-000005', startsWithCreation: false, entries: [sentEntry] })));

    expect(text(root, 'history-note')).toBe('This invoice was made before history was recorded, so events before then are not shown.');
    expect(has(root, 'history-empty')).toBe(false);
    expect(all(root, 'history-entry')).toHaveLength(1);
  });

  it('shows the empty message, and no note, when there are no entries', () => {
    const { root } = setup(vi.fn().mockReturnValue(of({ number: 'INV-000005', startsWithCreation: false, entries: [] })));

    expect(text(root, 'history-empty')).toBe('No history has been recorded for this invoice.');
    expect(has(root, 'history-note')).toBe(false);
    expect(has(root, 'history-entry')).toBe(false);
    expect(has(root, 'history-title')).toBe(true);
  });
});

describe('InvoiceHistoryComponent reads only', () => {
  it('asks for the history once, for the id in the address, and calls nothing else', () => {
    const historyCall = vi.fn().mockReturnValue(of(history));

    setup(historyCall);

    expect(historyCall).toHaveBeenCalledTimes(1);
    expect(historyCall).toHaveBeenCalledWith(ID);
  });

  it('has no button, form, input or menu that could change history: only the back link and Retry', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));

    expect(root.querySelectorAll('button')).toHaveLength(0);
    expect(root.querySelectorAll('form, input, textarea, select, [role="menu"], [contenteditable]')).toHaveLength(0);
    expect(root.querySelectorAll('a')).toHaveLength(1);
  });

  it('offers only Retry (besides links) when it failed', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));

    const buttons = Array.from(root.querySelectorAll('button')).map((button) => button.getAttribute('data-testid'));
    expect(buttons).toEqual(['retry']);
  });

  it('follows the address: a new id loads that invoice, and a slow older answer cannot replace a newer one', () => {
    const slow = new Subject<InvoiceHistory>();
    const fast = of({ ...history, number: 'INV-000999' });
    const historyCall = vi.fn().mockImplementation((id: string) => (id === ID ? slow : fast));
    const { fixture, root, params } = setup(historyCall);

    params.next(convertToParamMap({ id: '99999999-9999-9999-9999-999999999999' }));
    fixture.detectChanges();
    expect(historyCall).toHaveBeenLastCalledWith('99999999-9999-9999-9999-999999999999');
    expect(text(root, 'history-number')).toBe('INV-000999');

    slow.next(history);
    fixture.detectChanges();

    expect(text(root, 'history-number')).toBe('INV-000999');
  });
});

// ---- history is permanent (spec 010): nothing on the screen can change it ----

describe('InvoiceHistoryComponent has no way to change history', () => {
  /** Every element that could be operated: what it is and what it says or where it goes. */
  function controls(root: HTMLElement): string[] {
    return Array.from(root.querySelectorAll('a, button, input, select, textarea, form, [role="button"], [role="menuitem"], [tabindex], [contenteditable], [draggable="true"], [onclick]')).map((element) => {
      const label = element.getAttribute('data-testid') ?? element.getAttribute('href') ?? element.textContent?.trim();
      return `${element.tagName.toLowerCase()}:${label}`;
    });
  }

  const noted: InvoiceHistory = { number: 'INV-000005', startsWithCreation: false, entries: [sentEntry] };
  const empty: InvoiceHistory = { number: 'INV-000005', startsWithCreation: false, entries: [] };

  it.each([
    ['loaded', history],
    ['noted', noted],
    ['empty', empty],
  ] as [string, InvoiceHistory][])('offers only the way back to the invoice in the %s state', (_state, value) => {
    const { root } = setup(vi.fn().mockReturnValue(of(value)));

    expect(controls(root)).toEqual(['a:back']);
  });

  it('offers only Retry and the way back to the list in the error state', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 }))));

    expect(controls(root)).toEqual(['button:retry', 'a:/invoices']);
  });

  it('offers only the way back to the list when the invoice was not found', () => {
    const { root } = setup(vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))));

    expect(controls(root)).toEqual(['a:/invoices']);
  });

  it.each([
    ['loaded', history],
    ['noted', noted],
    ['empty', empty],
  ] as [string, InvoiceHistory][])('has no form, input, select or textarea in the %s state', (_state, value) => {
    const { root } = setup(vi.fn().mockReturnValue(of(value)));

    expect(root.querySelectorAll('form, input, select, textarea')).toHaveLength(0);
    expect(root.querySelectorAll('button')).toHaveLength(0);
  });

  it('carries no edit, delete, remove, hide, add or move label or test id anywhere', () => {
    const { root } = setup(vi.fn().mockReturnValue(of(history)));
    const words = /\b(edit|delete|remove|hide|add|move|undo|revert|restore|clear)\b/i;

    const testIds = Array.from(root.querySelectorAll('[data-testid]')).map((element) => element.getAttribute('data-testid') ?? '');
    const labels = Array.from(root.querySelectorAll('a, button, [aria-label]')).map((element) => `${element.getAttribute('aria-label') ?? ''} ${element.textContent ?? ''}`);
    expect(testIds.filter((id) => words.test(id))).toEqual([]);
    expect(labels.filter((label) => words.test(label))).toEqual([]);
  });

  it('calls the history read once per load and nothing else, and writes nothing to storage', () => {
    const before = [localStorage.length, sessionStorage.length];
    const historyCall = vi.fn().mockReturnValue(of(history));

    const { fixture } = setup(historyCall);
    fixture.detectChanges();

    expect(historyCall).toHaveBeenCalledTimes(1);
    expect([localStorage.length, sessionStorage.length]).toEqual(before);
  });
});
