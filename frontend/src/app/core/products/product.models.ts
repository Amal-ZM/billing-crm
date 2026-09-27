import type { PagedResult } from '../customers/customer.models';

export type { PagedResult };

export type ProductUnit = 'Hour' | 'Item' | 'Service';

export const PRODUCT_UNITS: readonly ProductUnit[] = ['Hour', 'Item', 'Service'];

/** Which products a list shows by active state. Only an Admin can ask for anything but `active`. */
export type ProductStatus = 'active' | 'inactive' | 'all';

export const PRODUCT_STATUSES: readonly ProductStatus[] = ['active', 'inactive', 'all'];

/** Unit price is SAR and VAT percent is a percentage (15 means 15%); both are exact decimals on the server. */
export interface Product {
  id: string;
  name: string;
  sku: string;
  unit: ProductUnit;
  unitPrice: number;
  vatPercent: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ProductListItem {
  id: string;
  name: string;
  sku: string;
  unit: ProductUnit;
  unitPrice: number;
  vatPercent: number;
  isActive: boolean;
}

/** What the add and edit form sends. The numbers are sent exactly as typed, never re-rounded. */
export interface ProductInput {
  name: string;
  sku: string;
  unit: ProductUnit;
  unitPrice: number;
  /** Left out when the field is empty on a new product; the server then applies the default of 15. */
  vatPercent?: number;
}

/** The list's search, filters and paging. All of it lives in the URL query string. */
export interface ProductListQuery {
  q?: string;
  unit?: ProductUnit;
  status?: ProductStatus;
  page?: number;
  pageSize?: number;
}
