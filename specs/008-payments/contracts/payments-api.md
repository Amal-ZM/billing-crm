# Contract: Payments (changes to `/api/invoices`)

**Feature**: 008-payments | One new route, additions to the invoice body, and one rule added to cancel. Everything not mentioned
here is as in [005](../../005-invoice-create/contracts/invoices-api.md), [006](../../006-invoice-totals/contracts/invoice-totals-api.md)
and [007](../../007-invoice-status/contracts/invoice-status-api.md).

| Operation | Who | Success | Errors |
|-----------|-----|---------|--------|
| `POST /api/invoices/{id}/payments` | any signed-in Admin or Agent | `201` + `Location: /api/invoices/{id}` + the invoice | `400` (`date`, `amount`, `method`, `note`), `401`, `404`, `409` |
| `POST /api/invoices/{id}/cancel` | **Admin only** | as in 007 | adds `409 invoice_has_payments` |
| `GET /api/invoices/{id}` | any signed-in Admin or Agent | the invoice with the additions below | as before |
| `GET /api/invoices`, `POST /api/invoices`, `PUT /api/invoices/{id}`, `POST .../send` | as before | as before | as before |
| `GET`, `PUT`, `PATCH`, `DELETE` on a payment | nobody | there is no such route (payments are permanent) | `404`/`405` |

## `POST /api/invoices/{id}/payments`

Records a payment. If it brings the remaining amount to exactly `0.00` the invoice becomes `Paid` in the same save.

Request body (JSON):

```json
{ "date": "2030-06-15", "amount": 100.00, "method": "Cash", "note": "Paid at the office" }
```

| Field | Rule |
|-------|------|
| `date` | Required, text `yyyy-MM-dd`, a real date; not after today's date in Saudi Arabia (UTC+03:00); not before the Saudi date the invoice was sent |
| `amount` | Required number (SAR) greater than 0, at most two decimals (a third is refused, never rounded), not more than the invoice's remaining amount |
| `method` | Required, exactly `Cash`, `Bank` or `Other` |
| `note` | Optional, trimmed, at most 1000 characters |

Order of checks: the invoice exists, then its status, then the body, then the save.

| Situation | Answer |
|-----------|--------|
| Sent (also Overdue), amount within the remaining | `201`, the invoice with the new payment; `status` still `Sent` while something remains |
| The payment brings the remaining to `0.00` | `201`, `status: "Paid"`, `remaining: 0.00`, `isOverdue: false`, `acceptsPayment: false` |
| Draft, Cancelled or Paid | `409`, title "Payment not allowed", detail "An invoice that is Draft cannot take a payment." (the current status) (service code `invoice_payment_not_allowed`); nothing changes, and this is answered before the values in the body are judged (a body the framework cannot read at all, such as malformed JSON, is its own `400` first, as in 007) |
| Field problems | `400` with `errors` keyed `date`, `amount`, `method`, `note`; **all** problems are reported together |
| `amount` above the remaining | `400`, `errors.amount`: "Amount cannot be more than the remaining 130.00 SAR." |
| Unknown or malformed id | `404` "Invoice not found" |
| Another change to the same invoice happened in the same instant (another payment, a cancel) | `409` "Invoice changed": "This invoice was changed by someone else. Reload it and try again."; nothing is saved |
| Unreadable body | the framework's `400` |
| No token | `401` |

Example: an invoice with `totals.grandTotal` 230.00: `amount: 100.00` gives `201` with `paid: 100.00`, `remaining: 130.00`,
`status: "Sent"`; a second `amount: 130.00` gives `201` with `paid: 230.00`, `remaining: 0.00`, `status: "Paid"`; a third payment
of any amount is `409`.

## Invoice body additions (create, get, edit, send, cancel, payments)

```json
{
  "status": "Sent",
  "paid": 100.00,
  "remaining": 130.00,
  "acceptsPayment": true,
  "allowedTransitions": [],
  "payments": [
    { "id": "…", "date": "2030-06-15", "amount": 100.00, "method": "Cash", "note": "Paid at the office", "recordedAtUtc": "2030-06-15T09:30:00Z" }
  ],
  "…": "all the 007 fields"
}
```

- `paid` is the sum of the payments and `remaining` is `totals.grandTotal - paid`, both SAR with two decimals, calculated by the API from
  the invoice's frozen grand total (a repriced product never moves them). They are present for every status; for a Draft `paid` is
  `0.00` and `remaining` is its calculated grand total, and for a Cancelled invoice `remaining` is its frozen grand total (a
  Cancelled invoice never has payments): screens show them only for Sent and Paid invoices.
- `acceptsPayment` is `true` only when the stored status is `Sent` (so also Overdue) **and** `remaining` is greater than 0. A client
  offers Record payment only when it is `true`.
- `payments` is ordered by the time recorded, then id. Each has `date` (`yyyy-MM-dd`), `amount`, `method` (`Cash`, `Bank`, `Other`),
  `note` (`null` if none) and `recordedAtUtc`. There is no id-based route for a payment.
- `allowedTransitions`: as in 007 but **never lists `Paid`** (only a payment settles an invoice) and lists `Cancelled` only while the
  invoice has **no payments**. So a Sent invoice with no payments gives `["Cancelled"]`, and with payments gives `[]`.
- The list rows are unchanged; a settled invoice shows `status: "Paid"`.

## `POST /api/invoices/{id}/cancel` (one added rule)

| Situation | Answer |
|-----------|--------|
| Sent (also Overdue) with **one or more payments** | `409`, title "Invoice has payments", detail "An invoice that has payments cannot be cancelled." (service code `invoice_has_payments`); nothing changes |
| An Agent, any invoice | `403` first, as in 007 |
| Everything else | as in 007 |

For an Admin the order is: not found, then Paid/Cancelled ("Illegal status change"), then "has payments".

## Acceptance checks (used by the tests and the quickstart)

1. Grand total 230.00: pay 100.00, then 130.00: `Sent` with 130.00 remaining, then `Paid` with 0.00 remaining.
2. Pay 130.01 when 130.00 remains: `400` on `amount`, nothing saved; pay exactly 130.00: `Paid`.
3. Amounts `0`, `-1`, `10.005` and none: `400` on `amount`; a method of `cash` or `Card` or none: `400` on `method`.
4. A date tomorrow (Saudi), a date before the invoice was sent, no date and `2030-02-30`: `400` on `date`; today: accepted.
5. Draft, Cancelled and Paid invoices: `409` naming the status, even with a body that is also invalid.
6. An Admin cancelling a Sent invoice with a payment: `409 invoice_has_payments`; with none: `200`; an Agent: `403`.
7. Two payments at the same balance from two sessions: one `201`, one `409 invoice_changed`; the invoice is never overpaid.
8. After the invoice's product is repriced, `remaining` and `totals` are unchanged.
9. No token: `401` on the new route; a route list containing only the one new route.

**A note on the error codes.** `invoice_payment_not_allowed` and `invoice_has_payments` (like 007's `invoice_status_change_not_allowed` and
`invoice_not_draft`) are the `Error.Code` values inside the service, used by its tests. The HTTP body is `ProblemDetails` with `title`,
`detail` and, for a `400`, `errors`; it carries no separate `code` property. A client tells the refusals apart by the status and the
`title`.
