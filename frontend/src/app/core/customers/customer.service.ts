import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Customer, CustomerInput, CustomerListItem, CustomerListQuery, PagedResult } from './customer.models';

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/customers`;

  create(input: CustomerInput): Observable<Customer> {
    return this.http.post<Customer>(this.base, input);
  }

  update(id: string, input: CustomerInput): Observable<Customer> {
    return this.http.put<Customer>(`${this.base}/${encodeURIComponent(id)}`, input);
  }

  /** Retire (false) or restore (true). Idempotent on the server. */
  setActive(id: string, isActive: boolean): Observable<Customer> {
    return this.http.put<Customer>(`${this.base}/${encodeURIComponent(id)}/active`, { isActive });
  }

  get(id: string): Observable<Customer> {
    return this.http.get<Customer>(`${this.base}/${encodeURIComponent(id)}`);
  }

  /** Blank or missing values are left out of the query string entirely. */
  list(query: CustomerListQuery): Observable<PagedResult<CustomerListItem>> {
    let params = new HttpParams();
    if (query.q?.trim()) {
      params = params.set('q', query.q.trim());
    }
    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.active !== undefined) {
      params = params.set('active', String(query.active));
    }
    if (query.page !== undefined) {
      params = params.set('page', String(query.page));
    }
    if (query.pageSize !== undefined) {
      params = params.set('pageSize', String(query.pageSize));
    }
    return this.http.get<PagedResult<CustomerListItem>>(this.base, { params });
  }
}
