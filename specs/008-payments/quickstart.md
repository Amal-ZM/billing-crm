# Quickstart: Payments

**Feature**: 008-payments | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 007 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-007 working: you can sign in as `admin@crm.local` (Admin) and `agent1@crm.local` (Agent) (see CLAUDE.md, "Seed Users"). You
need one active customer (for example **ABC**) and an active product `CNS-001` (price 100.00, VAT 15). The invoices used below are
made in the checks themselves: an invoice for `CNS-001` quantity 2 has a grand total of **230.00**.

## 1. Add the payments table

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies `AddPayments` (one new table; nothing else changes, existing invoices have no payments). No new secrets or settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate in the browser (maps to the spec's acceptance scenarios)

Use two browser windows (or sign out and in) for the Admin and the Agent parts.

### A. Record payments and settle (Agent or Admin)

| Check | Expected |
|-------|----------|
| Sign in as the **Agent**. Create an invoice for ABC, `CNS-001` quantity 2, a due date next week, **Send** it. Open it | **Sent**; **Paid 0.00**, **Remaining 230.00**, "No payments yet"; **Record payment** is offered (US1, US3.2) |
| Click **Record payment**. Enter today's date, amount `100`, method **Cash**, note `First half`, **Save** | Back on the invoice: still **Sent**, **Paid 100.00**, **Remaining 130.00**, the payment listed with its date, amount, method and note (US1.1, US1.4, US3.1) |
| **Record payment** again: amount `130`, method **Bank**, no note, **Save** | **Paid** status, **Paid 230.00**, **Remaining 0.00**, both payments listed; **Record payment** is gone (US1.2, US1.5, SC-002) |
| Open the invoice list | The invoice shows **Paid** (US3.6) |
| Try `/invoices/<its id>/payments/new` on the Paid invoice | A message that it cannot take a payment, not a form (US2.3) |

### B. Refusals (US2)

Make a second invoice (`CNS-001` quantity 2, sent) and open **Record payment**.

| Check | Expected |
|-------|----------|
| Amount `230.01` | A message on the amount saying how much remains (230.00); nothing saved (US2.4) |
| Amount `0`, `-5`, `10.005`, empty | A message on the amount each time; what you typed stays (US2.5) |
| Amount `230` with no method chosen | A message on the method; the amount you typed stays (US2.6) |
| A date tomorrow, and a date before the day the invoice was sent | A message on the date each time (US2.7) |
| A note of more than 1000 characters | A message on the note (US2.8) |
| Several of these wrong together (for example no method, a bad amount and a note that is too long) | The messages for everything the form can check itself appear together. The rules only the server can judge (a date in the future or before the invoice was sent, more than remains) appear after those are fixed, or together when nothing else is wrong (US2.9) |
| A **Draft** invoice's details | No **Record payment**, no payments section that invites adding one; `/invoices/<id>/payments/new` shows "cannot take a payment" (US2.1, US3.3) |
| Sign out and open the payment address | Sent to sign-in (US1.8) |

### C. Overdue, cancel and price change

| Check | Expected |
|-------|----------|
| Make a Sent invoice late (dev database only): in `sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d <your database name>` or SSMS run `UPDATE Invoices SET DueDate = DATEADD(day, -2, CAST(SYSDATETIME() AS date)) WHERE Number = 123` (use the digits of its number) | It shows **Overdue**; **Record payment** is still offered (US1.3) |
| Pay part of it, then the rest | After the part: still **Overdue**, remaining smaller; after the rest: **Paid** and no longer Overdue (US1.3) |
| Send a fresh invoice, record 100.00, then, as **Admin**, look at it | **Cancel invoice** is **not** offered (US2.11) |
| Send another fresh invoice with **no** payment, as Admin | **Cancel invoice** works as before (US2.11) |
| As Admin, change `CNS-001`'s price to 150, reopen a Sent invoice with a payment | Amounts, paid and remaining unchanged (edge case: frozen totals) |
| Look at any invoice with a payment | No way to edit or delete a payment anywhere (US2.12) |

## 4. Validate the API directly

```powershell
# Sign in for a token per user (the response has accessToken), then:
$today = Get-Date -Format yyyy-MM-dd
@{ date = $today; amount = 100.00; method = "Cash"; note = "first" } | ConvertTo-Json -Compress | Set-Content -Encoding utf8 payment.json
curl.exe -i -X POST http://localhost:5080/api/invoices/<ID>/payments -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" --data-binary "@payment.json"
```

The date is today's (the file above is made with it); any date from the day the invoice was sent up to today in Saudi Arabia is accepted. Delete `payment.json` afterwards.

| Request | Expected |
|---------|----------|
| The payment above on a Sent invoice of 230.00 | `201`, `Location: /api/invoices/<ID>`, `"paid":100.00`, `"remaining":130.00`, `"status":"Sent"` |
| A second with `"amount":130.00` | `201`, `"status":"Paid"`, `"remaining":0.00`, `"acceptsPayment":false` |
| A third payment of any amount | `409`, "An invoice that is Paid cannot take a payment." |
| `"amount":130.01` when 130.00 remains | `400`, `errors.amount` |
| `"amount":10.005`, `0`, or missing | `400`, `errors.amount` |
| `"method":"cash"` or `"Card"` | `400`, `errors.method` |
| A payment on a Draft or Cancelled invoice (even with a bad body) | `409` naming the status |
| `POST .../cancel` with the **Admin** token on an invoice with a payment | `409`, "An invoice that has payments cannot be cancelled." |
| `POST .../cancel` with the **Agent** token, any id | `403` |
| Any payment on an unknown id | `404` |
| No token | `401` |

## 5. Automated

```powershell
dotnet test backend/tests/BillingCrm.Application.Tests
dotnet test backend/tests/BillingCrm.Api.Tests
dotnet test backend/tests/BillingCrm.Infrastructure.Tests      # needs LocalDB
cd frontend; ng test --watch=false; ng build
```

Run the three backend projects one at a time on a busy machine (parallel runs cause timing failures). All must pass.
