# Quickstart: Invoice Status

**Feature**: 007-invoice-status | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 006 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-006 working: you can sign in as `admin@crm.local` (Admin) and `agent1@crm.local` (Agent) (see CLAUDE.md, "Seed Users"). You
need one active customer (for example **ABC**) and an active product `CNS-001` (price 100.00, VAT 15). The invoices used below are
made in the checks themselves. To see an Overdue invoice without waiting, use the "past due" check in section 3 (it needs one SQL line).

## 1. Add the status columns

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies `AddInvoiceStatuses` (status values, sent and cancelled times, frozen amounts, a row version). Existing invoices are Drafts and
stay Drafts. No new secrets or settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate in the browser (maps to the spec's acceptance scenarios)

Use two browser windows (or sign out and in) for the Admin and the Agent parts.

### A. Send, and the lock (Agent or Admin)

| Check | Expected |
|-------|----------|
| Sign in as the **Agent**. **Create invoice** for ABC, `CNS-001` quantity `2`, a due date next week, Save. Open it | Status **Draft**, grand total 230.00; **Edit**, **Send** offered; **Cancel** is **not** offered (US2.4) |
| Click **Send**, then **Yes, send** in the inline question ("Send this invoice? This locks it and it cannot be edited afterwards." with **Yes, send** and **Keep it**) | Status **Sent**, the time sent is shown, grand total still 230.00 (US1.1); **Edit** and **Send** are gone (US1.3) |
| Type the invoice's edit address (`/invoices/<id>/edit`) | "Only a Draft can be edited" with a link back, not a form (US1.3, FR-013) |
| As Admin, change `CNS-001`'s price to 150, reopen the invoice | Amounts unchanged (US1.4, SC-006) |
| Try to send a Draft whose due date has gone past | The form never lets you save a *new* past due date, so this case is checked through the API in section 4 (US1.6) |

### B. Cancel (Admin only)

| Check | Expected |
|-------|----------|
| Sign in as **Admin**. Open a Draft, **Cancel**, **Yes, cancel** | Status **Cancelled**, time cancelled shown, number, lines and amounts kept; still in the list (US2.1, US2.7) |
| Open a **Sent** invoice, **Cancel**, confirm | **Cancelled** (US2.2) |
| Open the Cancelled invoice | No **Send**, **Cancel** or **Edit**; its edit address shows "Only a Draft can be edited" (US2.6) |
| As the **Agent**, open any invoice | No **Cancel** button (US2.4). The API check in section 4 shows `403` |

### C. Overdue

| Check | Expected |
|-------|----------|
| Send an invoice due **tomorrow** | Shows **Sent** in the details and the list (US3.1) |
| Make a Sent invoice late (dev database only): in `sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d <your database name>` or SSMS run `UPDATE Invoices SET DueDate = DATEADD(day, -2, CAST(SYSDATETIME() AS date)) WHERE Number = 123` (use the digits of its number: `INV-000123` is `123`; two days back stays late whatever your machine's time zone) | Reload: it shows **Overdue** in the details and the list (US3.2); an Admin can still cancel it; **Send** is not offered (US3.5) |
| A Draft and a Cancelled invoice with a past due date (use the same `UPDATE` on them) | Never shown as Overdue (US3.4) |
| Sign out and open an invoice | Sent to sign-in (US1.9, SC-008) |

## 4. Validate the API directly

```powershell
# Sign in for a token per user (the response has accessToken), then:
curl.exe -i -X POST http://localhost:5080/api/invoices/<ID>/send   -H "Authorization: Bearer <TOKEN>"
curl.exe -i -X POST http://localhost:5080/api/invoices/<ID>/cancel -H "Authorization: Bearer <TOKEN>"
```

| Request | Expected |
|---------|----------|
| `send` on a Draft (either token) | `200`, `"status":"Sent"`, `sentAtUtc` set, `"isEditable":false` |
| `send` again | `409`, "An invoice that is Sent cannot be sent." |
| `cancel` with the **Agent** token, any id (even a made-up one) | `403` |
| `cancel` with the **Admin** token on a Draft or Sent invoice | `200`, `"status":"Cancelled"`, `cancelledAtUtc` set |
| `cancel` again | `409`, "An invoice that is Cancelled cannot be cancelled." |
| `PUT /api/invoices/<sent id>` with any body | `409`, "Only a Draft invoice can be edited. This invoice is Sent." |
| `send` on a Draft whose due date you set to the past in SQL | `400` with `errors.dueDate` |
| `send` on an unknown id | `404` |
| Either route with no token | `401` |

## 5. Automated

```powershell
dotnet test backend/BillingCrm.sln
cd frontend; ng test --watch=false; ng build
```

All pass (SC-009). The Infrastructure tests need LocalDB and delete only the throwaway `BillingCrm_Tests_*` database they create.
