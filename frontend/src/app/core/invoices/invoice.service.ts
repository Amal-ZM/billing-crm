import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Invoice, InvoiceHistory, InvoiceInput, InvoiceListItem, InvoiceListQuery, PagedResult, PaymentInput } from './invoice.models';

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/invoices`;

  /** Saves a new Draft: the whole invoice (header and every line) in one request. Quantities go exactly as given. */
  create(input: InvoiceInput): Observable<Invoice> {
    return this.http.post<Invoice>(this.base, input);
  }

  /**
   * Saves the whole desired Draft in one request: each line is existing (`id` and its quantity), new (`productId` and a quantity)
   * or left out (removed). The server checks everything first, so a refused save changes nothing.
   */
  update(id: string, input: InvoiceInput): Observable<Invoice> {
    return this.http.put<Invoice>(`${this.base}/${encodeURIComponent(id)}`, input);
  }

  /** Newest first. Missing values are left out of the query string entirely (the server then uses page 1 of 20). */
  list(query: InvoiceListQuery): Observable<PagedResult<InvoiceListItem>> {
    let params = new HttpParams();
    if (query.page !== undefined) {
      params = params.set('page', String(query.page));
    }
    if (query.pageSize !== undefined) {
      params = params.set('pageSize', String(query.pageSize));
    }
    if (query.status !== undefined) {
      params = params.set('status', query.status);
    }
    if (query.overdue !== undefined) {
      params = params.set('overdue', String(query.overdue));
    }
    if (query.paidMonth !== undefined) {
      params = params.set('paidMonth', String(query.paidMonth));
    }
    return this.http.get<PagedResult<InvoiceListItem>>(this.base, { params });
  }

  /** Draft to Sent (any signed-in user). No body: it is an action on the invoice. Answers with the invoice, or a 400 / 404 / 409. */
  send(id: string): Observable<Invoice> {
    return this.http.post<Invoice>(`${this.base}/${encodeURIComponent(id)}/send`, null);
  }

  /** Draft or Sent to Cancelled (Admin only: an Agent gets 403). No body. Answers with the invoice, or a 403 / 404 / 409. */
  cancel(id: string): Observable<Invoice> {
    return this.http.post<Invoice>(`${this.base}/${encodeURIComponent(id)}/cancel`, null);
  }

  /**
   * Records a payment against a Sent invoice (any signed-in user). The amount goes exactly as typed. Answers `201` with the whole
   * updated invoice (new status, paid, remaining and payment list), or a 400 (field errors) / 404 / 409. Payments are permanent:
   * there is no way to change or remove one.
   */
  recordPayment(id: string, input: PaymentInput): Observable<Invoice> {
    return this.http.post<Invoice>(`${this.base}/${encodeURIComponent(id)}/payments`, input);
  }

  get(id: string): Observable<Invoice> {
    return this.http.get<Invoice>(`${this.base}/${encodeURIComponent(id)}`);
  }

  /**
   * One invoice's history, newest first (any signed-in user). A read and nothing else: history is permanent, written only by the server
   * inside the change it describes, so there is no method here that could create, change or remove an entry. Answers a 404 for an
   * unknown invoice.
   */
  history(id: string): Observable<InvoiceHistory> {
    return this.http.get<InvoiceHistory>(`${this.base}/${encodeURIComponent(id)}/history`);
  }
}
