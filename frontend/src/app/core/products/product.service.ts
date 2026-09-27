import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PagedResult, Product, ProductInput, ProductListItem, ProductListQuery } from './product.models';

@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/products`;

  /** Admin only on the server (an Agent gets 403). The numbers go exactly as given, never re-rounded. */
  create(input: ProductInput): Observable<Product> {
    return this.http.post<Product>(this.base, input);
  }

  /** Admin only on the server. A full replacement: the VAT percent must be sent (there is no default on an edit). */
  update(id: string, input: ProductInput): Observable<Product> {
    return this.http.put<Product>(`${this.base}/${encodeURIComponent(id)}`, input);
  }

  /** Deactivate to hide (false) or restore (true). Admin only on the server; idempotent. */
  setActive(id: string, isActive: boolean): Observable<Product> {
    return this.http.put<Product>(`${this.base}/${encodeURIComponent(id)}/active`, { isActive });
  }

  get(id: string): Observable<Product> {
    return this.http.get<Product>(`${this.base}/${encodeURIComponent(id)}`);
  }

  /** Blank or missing values are left out of the query string entirely. */
  list(query: ProductListQuery): Observable<PagedResult<ProductListItem>> {
    let params = new HttpParams();
    if (query.q?.trim()) {
      params = params.set('q', query.q.trim());
    }
    if (query.unit) {
      params = params.set('unit', query.unit);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.page !== undefined) {
      params = params.set('page', String(query.page));
    }
    if (query.pageSize !== undefined) {
      params = params.set('pageSize', String(query.pageSize));
    }
    return this.http.get<PagedResult<ProductListItem>>(this.base, { params });
  }
}
