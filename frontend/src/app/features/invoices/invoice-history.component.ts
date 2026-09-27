import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemDetail } from '../../core/http/problem-details';
import { HistoryKind, InvoiceHistory } from '../../core/invoices/invoice.models';
import { InvoiceService } from '../../core/invoices/invoice.service';

type HistoryState =
  | { kind: 'loading' }
  | { kind: 'loaded'; history: InvoiceHistory }
  | { kind: 'notFound' }
  | { kind: 'error'; detail: string | null };

/** The kinds in words (the API sends the names). */
const KIND_WORDS: Record<HistoryKind, string> = {
  Created: 'Created',
  DraftEdited: 'Draft edited',
  Sent: 'Sent',
  Payment: 'Payment',
  Cancelled: 'Cancelled',
};

/**
 * An invoice's history (`/invoices/:id/history`, spec 010): who changed what and when, newest first, as the API recorded it. It only reads: the
 * one request it makes is the history `GET`, and it has no button, form or menu that could change an entry (its only controls are the way back
 * and Retry). It formats nothing but the time: every value is shown exactly as received, as text, so nothing is recalculated or looked up
 * again and markup in a value can never run.
 */
@Component({
  selector: 'app-invoice-history',
  imports: [DatePipe, MatProgressSpinnerModule, RouterLink],
  template: `
    <section class="history">
      @let current = state();
      @if (current.kind === 'loading') {
        <p data-testid="detail-loading" role="status"><mat-spinner diameter="20" aria-hidden="true" /> Loading history…</p>
      } @else if (current.kind === 'notFound') {
        <h1>Invoice not found</h1>
        <p role="alert">No invoice exists with this address. It may have been opened from an old link.</p>
        <a routerLink="/invoices">Back to invoices</a>
      } @else if (current.kind === 'error') {
        <p role="alert" data-testid="detail-error">
          The invoice history could not be loaded.
          @if (current.detail; as detail) {
            {{ detail }}
          } @else {
            Try again.
          }
        </p>
        <div class="bar">
          <button type="button" data-testid="retry" (click)="load()">Retry</button>
          <a routerLink="/invoices">Back to invoices</a>
        </div>
      } @else {
        @let history = current.history;

        <div class="heading">
          <h1 data-testid="history-title">History</h1>
          <span data-testid="history-number" dir="ltr">{{ history.number }}</span>
        </div>
        <a data-testid="back" [routerLink]="['/invoices', id]">Back to invoice</a>

        @if (history.entries.length === 0) {
          <p data-testid="history-empty" role="status">No history has been recorded for this invoice.</p>
        } @else {
          @if (!history.startsWithCreation) {
            <p data-testid="history-note" role="status">This invoice was made before history was recorded, so events before then are not shown.</p>
          }

          <ol class="entries">
            @for (entry of history.entries; track entry.id) {
              <li class="entry" data-testid="history-entry">
                <div class="entry-head">
                  <span class="entry-kind" data-testid="entry-kind">{{ kindWords[entry.kind] }}</span>
                  <span dir="ltr" data-testid="entry-time">{{ entry.occurredAtUtc | date: 'medium' }}</span>
                  <span dir="ltr" data-testid="entry-actor">{{ entry.actorEmail }} ({{ entry.actorRole }})</span>
                </div>
                <div class="table-scroll">
                  <table class="changes" aria-label="What changed">
                    <thead>
                      <tr>
                        <th>Field</th>
                        <th>Before</th>
                        <th>After</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (change of entry.changes; track $index) {
                        <tr data-testid="change-row">
                          <th scope="row" dir="auto">{{ change.field }}</th>
                          <td dir="auto">{{ change.before ?? '—' }}</td>
                          <td dir="auto">{{ change.after ?? '—' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </li>
            }
          </ol>
        }
      }
    </section>
  `,
  styles: `
    .history {
      max-width: 900px;
      margin: 0 auto;
      padding: 16px;
    }
    .heading {
      display: flex;
      gap: 16px;
      align-items: baseline;
    }
    .bar {
      display: flex;
      gap: 16px;
      align-items: center;
    }
    .entries {
      margin: 16px 0 0;
      padding: 0;
      list-style: none;
    }
    .entry {
      margin-bottom: 16px;
      padding: 12px;
      border: 1px solid var(--mat-sys-outline-variant, #ccc);
      border-radius: 8px;
    }
    .entry-head {
      display: flex;
      flex-wrap: wrap;
      gap: 8px 16px;
      align-items: baseline;
      margin-bottom: 8px;
    }
    .entry-kind {
      font-weight: 700;
    }
    .table-scroll {
      overflow-x: auto;
    }
    table.changes {
      width: 100%;
      border-collapse: collapse;
    }
    table.changes th,
    table.changes td {
      padding: 4px 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant, #ddd);
      text-align: start;
      vertical-align: top;
      overflow-wrap: anywhere;
    }
  `,
})
export class InvoiceHistoryComponent implements OnInit {
  private readonly invoices = inject(InvoiceService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly state = signal<HistoryState>({ kind: 'loading' });
  protected readonly kindWords = KIND_WORDS;

  /** The invoice being shown, from the address. It follows the address, so the page can never show one invoice under another's URL. */
  protected id = '';

  /** Numbers the requests, so a slow older answer can never overwrite a newer one. */
  private request = 0;

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.id = params.get('id') ?? '';
      this.load();
    });
  }

  /** Reads the history. This is the only request the page ever makes. */
  protected load(): void {
    const request = ++this.request;
    this.state.set({ kind: 'loading' });
    this.invoices
      .history(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => request === this.request && this.state.set({ kind: 'loaded', history }),
        error: (error: unknown) => {
          if (request === this.request) {
            this.state.set(
              error instanceof HttpErrorResponse && error.status === 404
                ? { kind: 'notFound' }
                : { kind: 'error', detail: problemDetail(error) },
            );
          }
        },
      });
  }
}
