import type { PagedResult } from '../customers/customer.models';
import type { ProductUnit } from '../products/product.models';

export type { PagedResult };

/**
 * The stored status. **Overdue is not a status**: a Sent invoice whose due date has passed is reported with `isOverdue`, and the
 * screens show "Overdue" for it. What an invoice may become is in `allowedTransitions`, never worked out in the browser.
 */
export type InvoiceStatus = 'Draft' | 'Sent' | 'Paid' | 'Cancelled';

/**
 * One line of what the form sends. A line is either **existing** (`id` set: only its quantity can change, its saved copy is
 * kept) or **new** (`productId` set: copied from the product when it is saved). Never both.
 */
export interface InvoiceLineInput {
  id?: string;
  productId?: string;
  /** Sent exactly as typed (a number), never re-rounded. */
  quantity: number;
}

/** What the add and edit form sends: the whole desired Draft. */
export interface InvoiceInput {
  customerId: string;
  /** `yyyy-MM-dd` */
  dueDate: string;
  notes: string;
  /** The optional header discount in SAR, as typed (a number), or null for none. Leaving it out of a save clears a saved one. */
  discount: number | null;
  lines: InvoiceLineInput[];
}

/** How money was received. Exactly these three, spelled this way (the API refuses anything else). */
export type PaymentMethod = 'Cash' | 'Bank' | 'Other';

/** A payment as saved: a record that money was received. Never edited or deleted. */
export interface Payment {
  id: string;
  /** `yyyy-MM-dd`, the day the money was received. */
  date: string;
  /** SAR, two decimals. */
  amount: number;
  method: PaymentMethod;
  note: string | null;
  recordedAtUtc: string;
}

/** What the payment form sends. The amount is sent exactly as typed (a number), never re-rounded. */
export interface PaymentInput {
  /** `yyyy-MM-dd` */
  date: string;
  amount: number;
  method: PaymentMethod;
  note: string;
}

export interface InvoiceCustomer {
  id: string;
  name: string;
  isActive: boolean;
}

/**
 * A line as saved. The product's values are the copy taken when the line was added. `net`, `discountShare` and `vat` are the
 * amounts the API calculated from that copy (SAR, two decimals); the screens show them and never calculate any.
 */
export interface InvoiceLine {
  id: string;
  position: number;
  productId: string;
  productName: string;
  sku: string;
  unit: ProductUnit;
  unitPrice: number;
  vatPercent: number;
  quantity: number;
  /** Quantity times the saved unit price, rounded to two decimals. */
  net: number;
  /** This line's part of the header discount (0 when there is none). */
  discountShare: number;
  /** VAT on this line's net after its discount share. */
  vat: number;
}

/**
 * What the invoice comes to, calculated by the API from the saved lines and the discount, in SAR with two decimals. Amounts never
 * exceed 9,999,999,999,999.99, so these numbers are exact here. `grandTotal` is `netAfterDiscount` plus `totalVat`.
 */
export interface InvoiceTotals {
  totalNet: number;
  discount: number;
  netAfterDiscount: number;
  totalVat: number;
  grandTotal: number;
}

export interface Invoice {
  id: string;
  /** `INV-` plus six digits, for example `INV-000123`. */
  number: string;
  status: InvoiceStatus;
  customer: InvoiceCustomer;
  /** `yyyy-MM-dd` */
  dueDate: string;
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  /** True when the invoice is Sent and its due date is before today (Saudi Arabia). Derived by the API, never stored. */
  isOverdue: boolean;
  /** When it was sent (null while a Draft, and for an invoice cancelled straight from Draft). */
  sentAtUtc: string | null;
  cancelledAtUtc: string | null;
  /**
   * What a person could ask for, before the caller's role: Draft gives Sent and Cancelled; Sent gives Cancelled **only while it has
   * no payment**; Paid and Cancelled give none. It never lists Paid (only a payment that settles the invoice causes it).
   */
  allowedTransitions: InvoiceStatus[];
  /** True only for a Draft: every other status is read-only. */
  isEditable: boolean;
  /** The saved header discount in SAR (0 when none). */
  discount: number;
  totals: InvoiceTotals;
  lines: InvoiceLine[];
  /** The sum of the payments, SAR with two decimals, worked out by the API. The screens show it and never work it out. */
  paid: number;
  /** `totals.grandTotal` minus `paid`, worked out by the API from the frozen grand total. Shown, never calculated here. */
  remaining: number;
  /** True only when the invoice is Sent (also Overdue) and something remains: the one flag that decides if Record payment is offered. */
  acceptsPayment: boolean;
  /** In the order they were recorded. Payments are permanent: they are only ever shown. */
  payments: Payment[];
}

export interface InvoiceListItem {
  id: string;
  number: string;
  status: InvoiceStatus;
  customerId: string;
  customerName: string;
  dueDate: string;
  createdAtUtc: string;
  /** Shown as Overdue when true (Sent and past its due date). */
  isOverdue: boolean;
}

/**
 * The list's paging, and (spec 011) an optional narrowing to one of the Home dashboard's views. All of it lives in the URL query
 * string. `overdue` only narrows further when `status` is `'Sent'`; `paidMonth` only narrows further when `status` is `'Paid'`; the
 * invoice list screen itself has no filter control of its own — these exist only so a dashboard card's link can reach its own set.
 */
export interface InvoiceListQuery {
  page?: number;
  pageSize?: number;
  status?: InvoiceStatus;
  overdue?: boolean;
  paidMonth?: boolean;
}

/** What happened to an invoice (spec 010): exactly these five. */
export type HistoryKind = 'Created' | 'DraftEdited' | 'Sent' | 'Payment' | 'Cancelled';

/**
 * One thing that changed in a history entry. Every value is **text as it was at the time** (names as they were then, dates `yyyy-MM-dd`, money
 * with two decimals, statuses by their stored name) and is shown exactly as received: nothing is formatted or looked up again. `null` means it
 * did not exist (`before`) or was removed or cleared (`after`).
 */
export interface HistoryChange {
  field: string;
  before: string | null;
  after: string | null;
}

/** One permanent record of one change. It is only ever shown: nothing in the application changes or removes one. */
export interface HistoryEntry {
  id: string;
  kind: HistoryKind;
  occurredAtUtc: string;
  /** The signed-in user's email as it was when they made the change. */
  actorEmail: string;
  actorRole: 'Admin' | 'Agent';
  changes: HistoryChange[];
}

/** An invoice's history, newest first. `startsWithCreation` is false for an invoice made before history was recorded (the API says so; the screen never works it out). */
export interface InvoiceHistory {
  number: string;
  startsWithCreation: boolean;
  entries: HistoryEntry[];
}
