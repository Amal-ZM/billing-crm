# Data Model: Invoice Print

**Nothing is stored, added or changed.** No table, column, migration, API field, endpoint or response shape changes. The printable page
is a view of an invoice the API already returns from `GET /api/invoices/{id}` (see the 006, 007 and 008 contracts). This document lists
which parts of that response the page reads, and which it must not show.

## Printable invoice (a view, not an entity)

| Section on the page | Read from the invoice response | Formatted as (same as the details) | Shown when |
|---------------------|--------------------------------|------------------------------------|------------|
| Title | none (the text `BillingCRM`) | plain text, page heading | always |
| Invoice number | `number` (`INV-000123`) | plain text, `dir="ltr"` | always |
| Status | `status`, `isOverdue` | the word: `Overdue` when `isOverdue`, otherwise `status` | always |
| Customer | `customer.name` | plain text, `dir="auto"` | always (also for an inactive customer; the flag is not shown) |
| Created | `createdAtUtc` | `date: 'medium'` | always |
| Due date | `dueDate` (`yyyy-MM-dd`) | `date: 'mediumDate'` | always |
| Sent | `sentAtUtc` | `date: 'medium'` | only when not null |
| Cancelled | `cancelledAtUtc` | `date: 'medium'` | only when not null |
| Notes | `notes` | plain text, `dir="auto"`, line breaks kept | only when not null |
| Lines (in the order returned) | each `lines[i]`: `productName`, `sku`, `unit`, `unitPrice`, `vatPercent`, `quantity`, `net`, `discountShare`, `vat` | `unitPrice`, `net`, `discountShare`, `vat`: `SAR` + `number: '1.2-2'`; `vatPercent`: `number: '1.0-2'` + `%`; `quantity`: `number: '1.2-2'`; names `dir="auto"`, numbers and SKU `dir="ltr"` | always (at least one line); the discount share column only when `totals.discount !== 0` |
| Total net | `totals.totalNet` | `SAR` + `number: '1.2-2'` | always |
| Discount | `totals.discount` | as above | only when `totals.discount !== 0` |
| Net after discount | `totals.netAfterDiscount` | as above | only when `totals.discount !== 0` |
| Total VAT | `totals.totalVat` | as above | always |
| Grand total | `totals.grandTotal` | as above, emphasised | always |

## Fields the page must NOT show (FR-012)

`id`, `customer.id`, `customer.isActive`, `lines[].id`, `lines[].productId`, `lines[].position`, `updatedAtUtc`, `discount` (the top-level
copy; the totals block carries it), `allowedTransitions`, `isEditable`, `acceptsPayment`, `paid`, `remaining`, `payments` (and every
payment field). None of these is rendered; the page's tests assert their absence.

## Rules

- **No arithmetic**: no field of the page is a sum, difference, product, rounding or comparison of amounts. The only comparison is
  `totals.discount !== 0` (whether to show the discount rows, exactly as the details do), which is a display choice, not a money rule.
- **No writes**: the page issues one `GET`. It never calls `send`, `cancel`, `update`, `recordPayment` or any other method.
- **State**: `loading` -> `loaded` | `notFound` | `error` (with Retry). Nothing else is remembered; nothing goes into storage, the URL
  or the server.
