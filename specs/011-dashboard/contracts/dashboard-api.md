# Contract: Dashboard (changes to `/api/invoices`, and the Home cards)

**Feature**: 011-dashboard | One new read route and three new optional filter values on the existing invoice list read. Everything not
mentioned here is as in [005](../../005-invoice-create/contracts/invoices-api.md) to [010](../../010-invoice-history/contracts/invoice-history-api.md).

| Operation | Who | Success | Errors |
|-----------|-----|---------|--------|
| `GET /api/invoices/summary` | any signed-in Admin or Agent | `200` + the five figures | `401` |
| `GET /api/invoices?status=...&overdue=...&paidMonth=...` | any signed-in Admin or Agent | `200` + the matching page | `400` on `status`, `401` |

## `GET /api/invoices/summary`

```json
{
  "draftCount": 3,
  "unpaidCount": 5,
  "overdueCount": 2,
  "paidThisMonthCount": 1,
  "unpaidAmount": 1234.50
}
```

- `draftCount`, `unpaidCount`, `overdueCount`, `paidThisMonthCount` are non-negative integers.
- `unpaidAmount` is a SAR amount with two decimals, the sum of the remaining balance of every invoice counted in `unpaidCount`.
- `unpaidCount` includes every invoice counted in `overdueCount` (Overdue is a subset of Unpaid, never a separate stored status).
- No query string is accepted; anything sent is ignored, exactly as 010's history read ignores one.
- No token is `401`. There is no `403`: any signed-in Admin or Agent reads the same figures.
- Reading the summary changes nothing.

| Situation | Answer |
|-----------|--------|
| Any invoices in any mix of statuses | `200`, the five figures for the whole database |
| No invoices at all | `200`, all four counts `0`, `unpaidAmount` `0.00` |
| No token | `401` |

## `GET /api/invoices` — new filter values

Existing behaviour (newest first, `page`, `pageSize`, `PagedResult<T>` shape) is unchanged. Three new, optional query values:

| Value | Accepted | Effect |
|-------|----------|--------|
| `status` | `Draft`, `Sent`, `Paid`, `Cancelled` | Narrows the list to that stored status. Any other text is a `400` field error naming `status` |
| `overdue` | `true` | Combined with `status=Sent`, narrows further to invoices whose due date has passed. Ignored (not an error) with any other `status`, or with none |
| `paidMonth` | `true` | Combined with `status=Paid`, narrows further to invoices whose settling payment falls in the current Saudi calendar month. Ignored (not an error) with any other `status`, or with none |

All three are optional and independent of paging; combined with `page`/`pageSize` they narrow the same paged result, not a second list.

| Situation | Answer |
|-----------|--------|
| `?status=Draft` | `200`, only Draft invoices |
| `?status=Sent` | `200`, every Sent invoice, on-time and overdue alike |
| `?status=Sent&overdue=true` | `200`, only Sent invoices whose due date has passed |
| `?status=Paid&paidMonth=true` | `200`, only invoices whose settling payment falls in the current Saudi calendar month |
| `?status=bad-value` | `400`, field error on `status` |
| `?overdue=true` (no `status`) | `200`, identical to no filter at all (ignored) |
| `?status=Cancelled&paidMonth=true` | `200`, only Cancelled invoices (`paidMonth` ignored: it only applies to `status=Paid`) |

## The Home cards

Five cards below Home's existing content, for any signed-in Admin or Agent, reading `GET /api/invoices/summary` once when Home loads:

```text
[ Draft: 3 ]   [ Unpaid: 5 ]   [ Overdue: 2 ]   [ Paid this month: 1 ]   [ Unpaid amount: SAR 1,234.50 ]
```

Each card is a link to `/invoices` carrying the query string from the table above (Draft → `?status=Draft`; Unpaid and the unpaid amount
card → `?status=Sent`; Overdue → `?status=Sent&overdue=true`; Paid this month → `?status=Paid&paidMonth=true`). The invoice list reads
these from its own address (it already keeps its filters there since 005) and shows exactly that narrowed, paged list — the same screen,
reached with a query string, not a new one.

## Acceptance checks (used by the tests and the quickstart)

1. Seed a Draft, an on-time Sent, an overdue Sent and an invoice paid in full this month; open Home: the five cards read 1, 2, 1, 1 and the
   overdue invoice's remaining balance.
2. An invoice paid this month and an overdue invoice, nothing else: Overdue and Paid this month each read 1, and the unpaid amount sum is
   exactly the overdue invoice's own remaining balance (the paid invoice never enters it) — the request's own example.
3. No invoices at all: every card reads 0, the amount reads SAR 0.00.
4. A Cancelled invoice, in any state before cancelling: never counted on any card, never in any card's list.
5. Click each of the five cards: the invoice list opens narrowed to exactly that card's invoices.
6. A payment recorded this month that settles an invoice whose earlier part payment was last month: counted as Paid this month once, this
   month, not last month.
7. `GET /api/invoices/summary` with no token: `401`. `GET /api/invoices?status=bad`: `400` naming `status`.
