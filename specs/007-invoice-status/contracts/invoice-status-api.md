# Contract: Invoice Status (changes to `/api/invoices`)

**Feature**: 007-invoice-status | Two new routes and additions to the invoice bodies. Everything not mentioned here is as in
[005](../../005-invoice-create/contracts/invoices-api.md) and [006](../../006-invoice-totals/contracts/invoice-totals-api.md).

| Operation | Who | Success | Errors |
|-----------|-----|---------|--------|
| `POST /api/invoices/{id}/send` | any signed-in Admin or Agent | `200` + the invoice (status `Sent`) | `400` (`dueDate`), `401`, `404`, `409` |
| `POST /api/invoices/{id}/cancel` | **Admin only** | `200` + the invoice (status `Cancelled`) | `401`, `403`, `404`, `409` |
| `PUT /api/invoices/{id}` | any signed-in Admin or Agent | as before (Draft only) | adds `409 invoice_not_draft` |
| `GET /api/invoices/{id}` | any signed-in Admin or Agent | the invoice with the additions below | as before |
| `GET /api/invoices` | any signed-in Admin or Agent | rows gain `isOverdue` | as before |
| `DELETE` anything | nobody | there is no delete | `405`/`404` as before |

Neither action has a request body. The `id` is checked after the role: an Agent calling `cancel` gets `403` even for an unknown or
malformed id. A malformed or unknown `id` is `404` for everyone else.

## Invoice body additions (create, get, edit, send, cancel)

```json
{
  "id": "…", "number": "INV-000123", "status": "Sent",
  "isOverdue": false,
  "sentAtUtc": "2030-06-01T10:00:00Z",
  "cancelledAtUtc": null,
  "allowedTransitions": ["Cancelled"],
  "isEditable": false,
  "…": "all the 006 fields (customer, dueDate, notes, discount, totals, lines with net / discountShare / vat)"
}
```

- `status` is the **stored** status: `Draft`, `Sent`, `Paid` or `Cancelled`. **Overdue is never a `status`**: `isOverdue` is `true` when
  `status` is `Sent` and `dueDate` is before today's date in Saudi Arabia (UTC+03:00), and `false` for every other case (including the
  due date itself). A client shows "Overdue" when `isOverdue` is true.
- `sentAtUtc` is set once the invoice has been sent (also on Paid); `cancelledAtUtc` is set once it is cancelled. Both are `null` otherwise.
  An invoice cancelled straight from Draft has `sentAtUtc: null`.
- `allowedTransitions` lists the statuses the **stored status** permits, before the caller's role: Draft gives `["Sent","Cancelled"]`,
  Sent (and so Overdue) gives `["Cancelled"]`, Paid and Cancelled give `[]`. A client offers Cancel only to an Admin.
- `isEditable` is `true` only for a Draft.
- For a Sent, Paid or Cancelled invoice the amounts (`discount`, `totals`, each line's `net`, `discountShare`, `vat`) are the **frozen**
  numbers written when it left Draft; they never change.
- List row additions: `isOverdue`.

## `POST /api/invoices/{id}/send`

Draft to Sent. Sets `sentAtUtc`, freezes the amounts, changes nothing else.

| Situation | Answer |
|-----------|--------|
| Draft with a due date of today or later | `200`, `status: "Sent"`, `isOverdue: false` |
| Draft with a due date before today | `400` `errors.dueDate`: "Due date is in the past. Change the due date before sending."; the invoice stays a Draft |
| Sent (also Overdue), Paid or Cancelled | `409`, title "Illegal status change", detail "An invoice that is Sent cannot be sent." (the current status: `Draft`, `Sent`, `Overdue`, `Paid`, `Cancelled`) |
| Unknown or malformed id | `404` "Invoice not found" |
| Another action on the same invoice happened in the same instant | `409` "Invoice changed": "This invoice was changed by someone else. Reload it and try again." |
| No token | `401` |

## `POST /api/invoices/{id}/cancel`

Admin only. Draft or Sent (including Overdue) to Cancelled. Sets `cancelledAtUtc`; freezes the amounts if the invoice was still a Draft.

| Situation | Answer |
|-----------|--------|
| Draft, Sent or Overdue, caller is an Admin | `200`, `status: "Cancelled"`, number, lines and amounts kept |
| Caller is an Agent | `403` (before anything is looked up; nothing changes) |
| Paid or Cancelled | `409`, detail "An invoice that is Paid cannot be cancelled." |
| Unknown or malformed id (Admin) | `404` |
| Lost a race | `409` "Invoice changed" |
| No token | `401` |

## `PUT /api/invoices/{id}` on an invoice that is not a Draft

`409`, title "Invoice is locked", detail "Only a Draft invoice can be edited. This invoice is Sent." (`Overdue` for a late one). It is
judged **before** the body, so any body gives the same answer, and nothing changes. Nothing about a Sent, Paid or Cancelled invoice can
be edited: lines, discount, customer, due date and notes are all locked.

## Acceptance checks this contract must satisfy (mapped to tests)

1. `send` on a Draft: `200`, `Sent`, `sentAtUtc` set, number and amounts unchanged, `isEditable` false, `allowedTransitions` `["Cancelled"]`.
2. `send` as an Agent and as an Admin both succeed.
3. `send` on Sent, Paid and Cancelled: `409` naming the status; on an Overdue invoice the detail says "Overdue".
4. `send` on a Draft due yesterday: `400` on `dueDate`, still a Draft.
5. `cancel` as an Admin from Draft (no `sentAtUtc`), Sent and Overdue: `200`, `Cancelled`, `cancelledAtUtc` set, amounts kept.
6. `cancel` as an Agent: `403`, also for an unknown id and for a Cancelled invoice; nothing changes.
7. `cancel` on Paid and Cancelled: `409`.
8. `PUT` on Sent, Paid and Cancelled with any body: `409 invoice_not_draft`, nothing changes.
9. `isOverdue`: false for a Sent invoice due today, true due yesterday (and for a Saudi date just past midnight), false for Draft, Paid and
   Cancelled with a past due date.
10. After a product is repriced or deactivated, a Sent invoice returns the same amounts.
11. Unknown and non-GUID ids: `404` on `send`; `401` without a token on both routes.
12. Two `send` (or `cancel`) calls racing: exactly one `200`, the other `409`.
