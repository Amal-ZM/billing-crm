export type CustomerType = 'Company' | 'Individual';

export const CUSTOMER_TYPES: readonly CustomerType[] = ['Company', 'Individual'];

export interface Customer {
  id: string;
  name: string;
  type: CustomerType;
  email: string;
  phone: string;
  vatNumber: string | null;
  address: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CustomerListItem {
  id: string;
  name: string;
  type: CustomerType;
  email: string;
  phone: string;
  vatNumber: string | null;
  isActive: boolean;
}

/** What the add and edit form sends. An empty VAT number means "none". */
export interface CustomerInput {
  name: string;
  type: CustomerType;
  email: string;
  phone: string;
  vatNumber: string;
  address: string;
}

/** The list's search, filters and paging. All of it lives in the URL query string. */
export interface CustomerListQuery {
  q?: string;
  type?: CustomerType;
  active?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
