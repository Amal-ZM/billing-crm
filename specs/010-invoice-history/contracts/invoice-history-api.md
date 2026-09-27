# Contract: Invoice History (changes to `/api/invoices`, and the History screen)

**Feature**: 010-invoice-history | One new read-only route, one new screen, one new link. Every existing route keeps its request and response;
each write route (create, edit, send, record a payment, cancel) additionally writes one history entry in the same save. Everything not mentioned
here is as in [005](../../005-invoice-create/contracts/invoices-api.md) to [008](../../008-payments/contracts/payments-api.md).

| Operation | Who | Success | Errors |
|-----------|-----|---------|--------|
| `GET /api/invoices/{id}/history` | any signed-in Admin or Agent | `200` + the history | `401`, `404` |
| `POST/PUT/PATCH/DELETE` on `/api/invoices/{id}/history` or on a single entry | nobody | there is no such route | `404`/`405` as for any unknown route |
| `GET` of a single entry, a list across invoices, search, filter, sort, paging | nobody | not offered; a query string on the history route is ignored | - |
| create, edit, send, record a payment, cancel | as before | as before; **one entry written in the same save** | as before; **no entry on any refusal** |

The `id` is judged as for the other invoice reads: a malformed or unknown id is `404` "Invoice not found". Reading history writes nothing.

## `GET /api/invoices/{id}/history`

```json
{
  "number": "INV-000123",
  "startsWithCreation": true,
  "entries": [
    {
      "id": "…",
      "kind": "Sent",
      "occurredAtUtc": "2030-06-02T09:00:00Z",
      "actorEmail": "agent1@crm.local",
      "actorRole": "Agent",
      "changes": [ { "field": "Status", "before": "Draft", "after": "Sent" } ]
    },
    {
      "id": "…",
      "kind": "Created",
      "occurredAtUtc": "2030-06-01T10:00:00Z",
      "actorEmail": "agent1@crm.local",
      "actorRole": "Agent",
      "changes": [
        { "field": "Customer", "before": null, "after": "ABC" },
        { "field": "Due date", "before": null, "after": "2030-07-01" },
        { "field": "Line: Consulting hour (CNS-001)", "before": null, "after": "2.00 x SAR 100.00, VAT 15.00%" }
      ]
    }
  ]
}
```

- `entries` are **newest first** (the database's own order counter, highest first), so two entries at the same instant keep the order they were applied in.
- `kind` is exactly one of `Created`, `DraftEdited`, `Sent`, `Payment`, `Cancelled`.
- `actorEmail` and `actorRole` (`Admin` or `Agent`) are the signed-in user's email and role **at the time of the change**, copied from the token; they never
  change afterwards. A request cannot set them.
- `changes`: each item is a named thing that changed with its value before (`null` if it did not exist: a Created entry, an added line) and after (`null`
  if removed or cleared). **Every value is text as it was at the time**: names as they were then, dates `yyyy-MM-dd`, money as exact two-decimal text
  (`100.00`), quantities and percentages with two decimals, statuses by their stored name (never `Overdue`). Nothing is recalculated when it is read.
- `startsWithCreation` is `true` exactly when the invoice's history contains a `Created` entry. An invoice made before this feature has none, so it is
  `false`, and its `entries` are only what happened after recording began (possibly none). The browser shows a note for `false` and never works it out.
- `number` is the invoice's number as text (`INV-000123`).
- The response contains no ids other than each entry's own `id`; it contains no customer, product or user record, only the text copied at the time.

| Situation | Answer |
|-----------|--------|
| An invoice with entries | `200`, entries newest first |
| An invoice with no entries (older, untouched since) | `200`, `startsWithCreation: false`, `entries: []` |
| Unknown or malformed id | `404` "Invoice not found" |
| No token | `401` |

## What each write route now records

| Route | Kind | Changes |
|-------|------|---------|
| `POST /api/invoices` | `Created` | `Customer`, `Due date`, `Notes` (if any), `Discount (SAR)` (if above 0), and `Line: {product} ({sku})` for each line |
| `PUT /api/invoices/{id}` (only if it changes something) | `DraftEdited` | only the changed ones of `Customer`, `Due date`, `Notes`, `Discount (SAR)`, `Line added: ...`, `Line removed: ...`, `Line quantity: ...` |
| `POST /api/invoices/{id}/send` | `Sent` | `Status`: `Draft` -> `Sent` |
| `POST /api/invoices/{id}/payments` | `Payment` | `Payment date`, `Payment amount (SAR)`, `Payment method`, `Payment note` (if any), `Amount paid (SAR)` before -> after, `Amount remaining (SAR)` before -> after, and `Status`: `Sent` -> `Paid` when it settles the invoice |
| `POST /api/invoices/{id}/cancel` | `Cancelled` | `Status`: `Draft` or `Sent` -> `Cancelled` |

A refused request (any `400`, `403`, `404` or `409`), a save that changes nothing, and a save that loses a race write **no** entry.

## The History screen

Address `/invoices/:id/history` (under the signed-in shell, lazy), reached from a **History** link (`data-testid="history"`) in the details' action row for
every invoice, whatever its status and role.

```text
h1   History                       data-testid="history-title"   (with the invoice number, dir=ltr, data-testid="history-number")
     Back to invoice               data-testid="back"
     note (only when startsWithCreation is false)   data-testid="history-note"
          "This invoice was made before history was recorded, so events before then are not shown."
     empty message (no entries)     data-testid="history-empty"   "No history has been recorded for this invoice."
     entries, newest first          data-testid="history-entry"   (one per entry)
        time           data-testid="entry-time"     date: 'medium', dir=ltr
        user           data-testid="entry-actor"    "<email> (<Role>)", dir=ltr
        kind           data-testid="entry-kind"     Created | Draft edited | Sent | Payment | Cancelled
        table Field | Before | After   one row per change (data-testid="change-row"), "—" for none, text dir=auto, wraps
```

States: loading (`detail-loading`), not found ("Invoice not found"), error ("The invoice history could not be loaded." + the API's detail, `Retry`), loaded.
The screen has **no button, form, menu or control that could change history**: its only controls are the back link and Retry; it calls no method that writes.
The screen formats nothing but the time: every value is shown exactly as received.

## Acceptance checks (used by the tests and the quickstart)

1. Send an invoice as the Agent, `GET` its history: a `Sent` entry, `actorEmail` the Agent's, `actorRole` `Agent`, `Status` `Draft` -> `Sent`.
2. Create, edit (change the due date and add a line), send, pay 100.00 then 130.00, cancel another invoice: one entry each, right kind, user and before/after.
3. An edit that changes nothing, a locked edit, an illegal send, an overpayment, a validation error and a lost race: no entry.
4. Rename the customer and reprice the product afterwards: the entries read the same.
5. Two entries written in quick succession keep their order; the list is newest first.
6. An invoice with no `Created` entry: `startsWithCreation: false`; the screen shows the note (or the empty message when there are no entries).
7. No `POST`, `PUT`, `PATCH` or `DELETE` under history; the exact route list has one new `GET`.
8. Directly `UPDATE` or `DELETE` a row in the database: refused; `INSERT` works.
9. No token: `401`; unknown or malformed id: `404`; both roles can read.
10. The History screen shows no control that could change an entry.
