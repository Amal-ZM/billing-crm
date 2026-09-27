# Quickstart: Dashboard

**Feature**: 011-dashboard | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 010 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-010 working: you can sign in as `admin@crm.local` (Admin) and `agent1@crm.local` (Agent). You need one active customer and one
active product to make invoices. There is **no migration** for this spec: nothing new to apply to the database.

## 1. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 2. Validate in the browser (maps to the spec's acceptance scenarios)

| Check | Expected |
|-------|----------|
| With no invoices at all, sign in and open Home | All five cards read 0, and the unpaid amount reads SAR 0.00 |
| **Create invoice** and Save (leave it a Draft) | The Draft card goes up by 1 |
| **Send** it, keep its due date in the future | The Draft card goes back down by 1; the Unpaid card goes up by 1; Overdue stays the same |
| Create and send a second invoice with a due date already in the past | The Unpaid card and the Overdue card both go up by 1; the unpaid
  amount sum includes its remaining balance |
| **Record payment** for the full grand total of a third, Sent invoice, dated today | The Unpaid and Overdue cards (if it was overdue)
  go down; the Paid this month card goes up by 1; the unpaid amount sum drops by that invoice's balance |
| Reopen Home (the request's own example): one overdue invoice and one invoice paid this month, nothing else | Overdue reads 1, Paid this
  month reads 1, and the unpaid amount sum is exactly the overdue invoice's own remaining balance |
| **Cancel** a Draft or a Sent invoice with no payment | It stops being counted anywhere on the dashboard |
| Click the **Draft** card | The invoice list opens showing only Draft invoices |
| Click the **Unpaid** card, then separately the **unpaid amount** card | Both open the invoice list showing the same set: every Sent
  invoice, on-time and overdue alike |
| Click the **Overdue** card | The invoice list opens showing only the Sent invoices whose due date has passed |
| Click the **Paid this month** card | The invoice list opens showing only the invoices that became Paid this calendar month |
| Sign in as the **Agent** | The same five cards, the same figures, the same links: the dashboard is not Admin-only |

## 3. Validate the API directly

```powershell
# Sign in for a token (the response has accessToken), then:
curl.exe -i http://localhost:5080/api/invoices/summary -H "Authorization: Bearer <TOKEN>"
curl.exe -i "http://localhost:5080/api/invoices?status=Sent&overdue=true" -H "Authorization: Bearer <TOKEN>"
curl.exe -i "http://localhost:5080/api/invoices?status=not-a-status" -H "Authorization: Bearer <TOKEN>"
```

| Request | Expected |
|---------|----------|
| `GET /api/invoices/summary` with either token | `200`, `draftCount`, `unpaidCount`, `overdueCount`, `paidThisMonthCount`, `unpaidAmount` |
| `GET /api/invoices/summary` with no token | `401` |
| `GET /api/invoices?status=Sent&overdue=true` | `200`, only overdue Sent invoices, in the usual paged shape |
| `GET /api/invoices?status=not-a-status` | `400`, a field error naming `status` |

## 4. Automated

```powershell
dotnet test backend/tests/BillingCrm.Application.Tests
dotnet test backend/tests/BillingCrm.Api.Tests
dotnet test backend/tests/BillingCrm.Infrastructure.Tests      # needs LocalDB
cd frontend; ng test --watch=false; ng build
```

Run the three backend projects one at a time on a busy machine. All must pass.
