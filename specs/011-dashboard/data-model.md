# Data Model: Dashboard

No new entity, no new table, no new column. Everything here reads `Invoices` and `Payments` (spec 005, 007, 008) as they already are.

## New Application types

### `DashboardSummaryDto` (read-only)

| Field | Type | Meaning |
|-------|------|---------|
| `DraftCount` | `int` | Invoices with stored status `Draft` |
| `UnpaidCount` | `int` | Invoices with stored status `Sent` (on-time and overdue alike) |
| `OverdueCount` | `int` | Invoices with stored status `Sent` and `DueDate` before today's Saudi date |
| `PaidThisMonthCount` | `int` | Invoices with stored status `Paid` whose settling payment's `PaidOn` falls in the current Saudi calendar month |
| `UnpaidAmount` | `decimal(18,2)` SAR | The sum of `Remaining` (`InvoiceMoney.Balance`) across every `Sent` invoice |

No id, no paging, no filter: one summary, always the whole set of invoices, computed fresh on every read.

### `InvoiceQuery` (existing record, extended)

Adds three optional fields, all read as text like `Page`/`PageSize` already are, so a bad value is a 400 naming its own field rather than
a framework failure:

| Field | Values | Meaning |
|-------|--------|---------|
| `Status` | `Draft`, `Sent`, `Paid`, `Cancelled` (case as the enum's own names) or absent | Narrows the list to that stored status; absent means every status, as today |
| `Overdue` | `true` or absent | With `Status=Sent`, narrows to invoices whose due date has passed; ignored with any other `Status` or when `Status` is absent |
| `PaidMonth` | `true` or absent | With `Status=Paid`, narrows to invoices whose settling payment falls in the current Saudi calendar month; ignored with any other `Status` or when `Status` is absent |

## Store contract additions

- `IInvoiceStore.GetSummaryAsync(CancellationToken)` → `Task<DashboardSummaryDto>` (the five figures, one call, read-only).
- `IInvoiceStore.SearchAsync` gains the three new fields already described on `InvoiceQuery` (or an equivalent typed filter object); its
  existing signature's meaning (newest first, paged) is otherwise unchanged.

## Angular types

- `core/dashboard/dashboard.models.ts`: `DashboardSummary` (camelCase mirror of `DashboardSummaryDto`).
- `core/invoices/invoice.models.ts`: `InvoiceListQuery` gains `status?: InvoiceStatus`, `overdue?: boolean`, `paidMonth?: boolean`,
  mirroring the query additions above; left out of the query string entirely when absent, exactly as `page`/`pageSize` already are.

## Rules

- Every figure is computed from the current data at read time; nothing is stored, cached or precomputed (FR-011).
- A Cancelled invoice is never part of any figure or any filtered view (FR-012).
- The unpaid amount sum is built only from figures the invoice already calculates (`InvoiceMoney`/`Balance`); it is never recalculated
  independently (FR-013), and it is never a `float`/`double` (Constitution IV).
