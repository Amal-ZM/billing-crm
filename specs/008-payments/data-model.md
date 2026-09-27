# Data Model: Payments

Additions to the 007 model. Everything not listed is unchanged.

## Payment (new entity, table `Payments`)

A record that money was received for one invoice. Created only through `Invoice.RecordPayment`; never edited or deleted.

| Field | Type | Rules |
|-------|------|-------|
| `Id` | Guid | Key. Made by the application (`Guid.NewGuid`), never by the database, so EF inserts a payment added to a tracked invoice |
| `InvoiceId` | Guid | Foreign key to `Invoices`, **no cascade delete** (invoices are never deleted; payment rows must not vanish) |
| `PaidOn` | date (no time of day) | A real calendar date. Not after today's Saudi date; not before the Saudi date the invoice was sent (both checked by the service, not the database) |
| `Amount` | `decimal(18,2)` SAR | Greater than 0 and at most 9,999,999,999,999.99 (`CK_Payments_Amount`); at most two decimals, refused not rounded; never more than the invoice's remaining at the moment it is recorded (service + row version) |
| `Method` | text, max 10 | Exactly `Cash`, `Bank` or `Other` (`CK_Payments_Method`); the enum `PaymentMethod` stored by name |
| `Note` | nvarchar(1000), null | Trimmed; null when empty; at most 1000 characters |
| `RecordedAtUtc` | datetime2 (UTC) | When it was recorded; tagged UTC again on read like the invoice's times |

Index: `IX_Payments_InvoiceId_RecordedAtUtc` (serves loading an invoice's payments in order).

## Invoice (changed)

| Member | Change |
|--------|--------|
| `Payments` | New: read-only view of a private list, ordered by `RecordedAtUtc` then `Id` (like `Lines`); field access in EF |
| `Paid` | New, not stored: the sum of `Payments.Amount` |
| `Remaining` | New, not stored: `Amounts.GrandTotal - Paid`, both from `InvoiceMoney.Balance`. For a non-Draft invoice the grand total is the frozen one |
| `AcceptsPayment` | New, not stored: `Status == Sent && Remaining > 0` (a Sent invoice is Overdue or not; both accept) |
| `AllowedTransitions` | New, not stored: `InvoiceStatusRules.NextStatuses(Status)` without `Paid` (only a payment causes it) and without `Cancelled` while `Payments.Count > 0` |
| `RecordPayment(date, amount, method, note, nowUtc)` | New. Adds the payment, moves `UpdatedAtUtc`, and when the remaining is then exactly 0.00 changes `Status` to `Paid` (the Sent to Paid change). Throws `InvalidOperationException` if the invoice is not Sent or the amount is not in `(0, Remaining]` (a bug: the service refuses first) |
| `Cancel(nowUtc)` | Also returns false, changing nothing, when the invoice has any payment |
| `Send`, `IsOverdue`, `EnsureDraft`, `Amounts`, frozen columns | Unchanged. A Paid invoice is not Overdue (Overdue is Sent only) and is read-only (`EnsureDraft`) |

## Status changes (`InvoiceStatusRules`)

| From | To | Caused by |
|------|----|-----------|
| Draft | Sent | Send (007) |
| Draft | Cancelled | Cancel (007) |
| Sent | Cancelled | Cancel (007), refused while the invoice has payments (008) |
| **Sent** | **Paid** | **A payment that brings the remaining to exactly 0.00 (008). Never requested by a person** |

Every other pair is illegal. Paid and Cancelled are final.

## `InvoiceMoney.Balance` (new, pure)

`Balance(grandTotal, paymentAmounts) -> (Paid, Remaining)`: `Paid` is the sum, `Remaining` is `grandTotal - Paid`. Exact decimal
arithmetic on two-decimal values: no rounding, no floating point. It never returns a negative remaining for data the service allowed;
if stored data ever showed one (it cannot through the application), it would be reported as is, not hidden.

## Existing constraints that already cover Paid

`CK_Invoices_Status` (Paid allowed), `CK_Invoices_StatusTimes` (Paid: sent, not cancelled), `CK_Invoices_FrozenAmounts` (not Draft:
frozen amounts present). No change to `Invoices`.

## State diagram

```text
Draft --send--> Sent --payment settles (remaining = 0.00)--> Paid   (final)
  |               |
  |               +--cancel (Admin, no payments)--> Cancelled       (final)
  +--cancel (Admin)-----------------------------------^
```

A Sent invoice past its due date is shown as Overdue (derived, 007) and behaves exactly as Sent.
