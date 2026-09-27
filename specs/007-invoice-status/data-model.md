# Data Model: Invoice Status

**Feature**: 007-invoice-status | One migration, `AddInvoiceStatuses` (research R10).

## Status and its changes

Stored statuses: `Draft`, `Sent`, `Paid`, `Cancelled`. **Overdue is never stored**: it is `Sent` with a due date before today
(UTC+03:00 date), returned as `isOverdue`.

```text
        send                 cancel (Admin)
Draft ────────► Sent ─────────────────────► Cancelled
  │               │                              ▲
  │               └─ (later spec) ──► Paid       │
  └──────────────────────────────────────────────┘
                cancel (Admin)
```

| From \ To | Sent | Cancelled | Paid | Draft |
|-----------|------|-----------|------|-------|
| **Draft** | legal (send, any staff) | legal (cancel, Admin) | illegal | illegal |
| **Sent** (also shown as Overdue) | illegal | legal (cancel, Admin) | illegal (no payments yet) | illegal |
| **Paid** | illegal | illegal | illegal | illegal |
| **Cancelled** | illegal | illegal | illegal | illegal |

An illegal change is `409` naming the current status. A role that may not cancel is `403`, checked first. Only a Draft can be edited;
editing any other status is `409`.

## Table change: `Invoices`

| Column | Type | Rules |
|--------|------|-------|
| `Status` | `nvarchar(20)` | Existing. Check `CK_Invoices_Status` widened to `IN ('Draft','Sent','Paid','Cancelled')` (drop and re-add) |
| `SentAtUtc` | `datetime2` null | **New.** Set once when sent. Null while Draft, and null for an invoice cancelled straight from Draft |
| `CancelledAtUtc` | `datetime2` null | **New.** Set once when cancelled |
| `FrozenTotalNet` | `decimal(18,2)` null | **New.** The total net at the moment the invoice left Draft |
| `FrozenNetAfterDiscount` | `decimal(18,2)` null | **New.** Net after discount, then |
| `FrozenTotalVat` | `decimal(18,2)` null | **New.** Total VAT, then |
| `FrozenGrandTotal` | `decimal(18,2)` null | **New.** Grand total, then |
| `RowVersion` | `rowversion` | **New.** Concurrency token (research R5); never sent to a client |

The frozen discount is the existing `Discount` column, which is locked from then on (nothing can be edited after leaving Draft).

Check constraints (new):

- `CK_Invoices_StatusTimes`:
  `([Status] = 'Draft' AND [SentAtUtc] IS NULL AND [CancelledAtUtc] IS NULL)`
  `OR ([Status] IN ('Sent','Paid') AND [SentAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL)`
  `OR ([Status] = 'Cancelled' AND [CancelledAtUtc] IS NOT NULL)`
- `CK_Invoices_FrozenAmounts`: `([Status] = 'Draft' AND` the four frozen columns are all `NULL`) `OR ([Status] <> 'Draft' AND` all four
  are `NOT NULL)`.

## Table change: `InvoiceLines`

| Column | Type | Rules |
|--------|------|-------|
| `FrozenNet` | `decimal(18,2)` null | **New.** The line's net when the invoice left Draft |
| `FrozenDiscountShare` | `decimal(18,2)` null | **New.** Its share of the discount, then |
| `FrozenVat` | `decimal(18,2)` null | **New.** Its VAT, then |

A database cannot see across rows, so "every line of a non-Draft invoice has its frozen values" is a Domain rule (`Invoice.Send` and
`Invoice.Cancel` write them all in one save). Existing rows stay `NULL` (all are Drafts).

## Domain types

- `InvoiceStatus`: `Draft`, `Sent`, `Paid`, `Cancelled`.
- `InvoiceStatusRules` (static): the table above as data; `CanChange(from, to)` and `NextStatuses(from)` (Draft gives Sent and
  Cancelled, Sent gives Cancelled, the others none).
- `Invoice`: `SentAtUtc`, `CancelledAtUtc`, `RowVersion`; `IsOverdue(DateOnly today)`; `Send(nowUtc)` and `Cancel(nowUtc)` (return whether
  the change happened; they set the time, move `UpdatedAtUtc` and freeze the amounts when leaving Draft); `IsEditable` (Draft); every
  edit member throws when not a Draft; `Amounts` returns the frozen numbers for a non-Draft invoice and the 006 calculation for a Draft.
- `InvoiceLine`: `FrozenNet`, `FrozenDiscountShare`, `FrozenVat` with an internal `Freeze(LineAmounts)`.

## Application types

- `InvoiceDto` gains `IsOverdue`, `SentAtUtc`, `CancelledAtUtc`, `AllowedTransitions` (list of status names) and `IsEditable`;
  `InvoiceDto.From(invoice, customer, today)` takes today's date.
- `InvoiceSummary` (list) gains `IsOverdue`; `InvoiceListRow.ToSummary(today)` works it out from the row's status and due date.
- `InvoiceService`: `SendAsync(id)`, `CancelAsync(id)`; `UpdateAsync` refuses a non-Draft invoice first. New errors:
  `invoice_status_change_not_allowed` (409) and `invoice_not_draft` (409); the existing `invoice_changed` (409) covers a lost race.

## Validation summary

| Situation | Rule | Result |
|-----------|------|--------|
| Send a non-Draft invoice | must be Draft | `409` naming the current status |
| Send a Draft whose due date is before today | due date not in the past | `400` on `dueDate` |
| Cancel a Paid or Cancelled invoice | must be Draft or Sent | `409` naming the current status |
| Cancel as an Agent | Admin only | `403` (before anything else) |
| Edit a non-Draft invoice | must be Draft | `409` (before the body is judged) |
| Two actions at once | row version | one succeeds, the other `409 invoice_changed` |
