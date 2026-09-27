# Quickstart: Invoice History

**Feature**: 010-invoice-history | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 009 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-009 working: you can sign in as `admin@crm.local` (Admin) and `agent1@crm.local` (Agent) (see CLAUDE.md, "Seed Users"). You need one active
customer (for example **ABC**) and an active product `CNS-001` (price 100.00, VAT 15). Invoices made **before** this spec have no history: keep at
least one of them (any existing invoice will do) to check the older-invoice note. The invoices used below are made in the checks themselves.

## 1. Add the history table

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies `AddInvoiceHistory` (one new table and one trigger; nothing else changes). No new secrets or settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate in the browser (maps to the spec's acceptance scenarios)

Use two browser windows (or sign out and in) for the Admin and the Agent parts.

### A. The request's example, and each kind of entry

| Check | Expected |
|-------|----------|
| Sign in as the **Agent**. **Create invoice** for ABC, `CNS-001` quantity 2, a due date next week, notes `Net 30`, Save. Open it and click **History** | The History screen opens with the invoice's number; one **Created** entry with `agent1@crm.local (Agent)`, the time, and the customer, due date, notes and the line as they were (US1.1, US2.3) |
| **Edit** the Draft: change the due date, change the quantity to `5`, **Add line** with another product, Save; open **History** | One new **Draft edited** entry listing each change with **Before** and **After** (due date, the quantity, the line added) (US1.2) |
| **Edit** and Save **without changing anything**; open **History** | No new entry (US1.3) |
| **Send** it; open **History** | A **Sent** entry with `agent1@crm.local (Agent)`, the time and `Status: Draft -> Sent`; newest first, so it is at the top (US1.4, US2.1, the request's example) |
| **Record payment** of `100`, then of the rest | A **Payment** entry for each: date, amount, method, note, `Amount paid` and `Amount remaining` before and after; the last one also shows `Status: Sent -> Paid` and there is **no** separate Paid entry (US1.5) |
| As the **Admin**, send a second invoice and **Cancel** it; open its **History** | A **Cancelled** entry with `admin@crm.local (Admin)` and `Status: Sent -> Cancelled` (US1.6, US1.9) |

### B. Refusals write nothing; values as they were

| Check | Expected |
|-------|----------|
| On a Sent invoice try a payment of more than what remains (the form refuses it), then look at **History** | No new entry (US1.7) |
| Open `/invoices/<a Sent invoice id>/edit` and (if you can) save; or send a Sent invoice through the API | Refused; **History** unchanged (US1.7) |
| Rename the customer ABC to `ABC Trading` and change `CNS-001`'s price to 150; reopen **History** of the invoices above | Every entry still says `ABC` and the old price, as when written (US2.8, US3.5) |
| Look at **History** of an invoice with several entries | Newest first; long or Arabic text wraps and reads in the right direction; amounts and times read left to right (US2.2, US2.7) |

### C. Older invoices, states, signed out

| Check | Expected |
|-------|----------|
| Open **History** of an invoice made before this spec that you have not touched since | "No history has been recorded for this invoice." (US2.4) |
| **Cancel** or **Send** one of those older invoices, then open its **History** | The new entry, and a note that events before recording began are not shown; no invented Created entry (US2.4, FR-012) |
| Type `/invoices/00000000-0000-0000-0000-000000000000/history` | "Invoice not found" and a way back (US2.5) |
| Stop the API and reload a History page | "The invoice history could not be loaded." with **Retry**; start the API and press **Retry**: it loads (US2.5) |
| Sign out, then open a History address | Sent to sign-in; after signing in you are returned to it (US2.6) |
| As the **Agent**, open a **History** | It opens (any signed-in user may read it) |

### D. History cannot be changed

| Check | Expected |
|-------|----------|
| Look over the History screen | No edit, delete, hide, reorder or add control anywhere: only **Back to invoice** and, on an error, **Retry** (US3.1) |
| Send, pay and cancel other invoices, then reopen an old History | The earlier entries are exactly as they were; only new ones were added (US3.3) |

## 4. Validate the API and the database directly

```powershell
# Sign in for a token per user (the response has accessToken), then:
curl.exe -i http://localhost:5080/api/invoices/<ID>/history -H "Authorization: Bearer <TOKEN>"
curl.exe -i -X DELETE http://localhost:5080/api/invoices/<ID>/history -H "Authorization: Bearer <TOKEN>"
curl.exe -i -X PUT http://localhost:5080/api/invoices/<ID>/history -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" -d "{}"
```

| Request | Expected |
|---------|----------|
| `GET .../history` with either token | `200`, `number`, `startsWithCreation`, `entries` newest first, each with `actorEmail`, `actorRole` and `changes` |
| `DELETE` or `PUT .../history` (either token) | `404` or `405`: there is no such operation; nothing changes |
| `GET` for an unknown id | `404` |
| `GET` with no token | `401` |

In `sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d <your database name>` (dev database only):

| Statement | Expected |
|-----------|----------|
| `SELECT TOP 3 Sequence, Kind, ActorEmail, ActorRole FROM InvoiceHistoryEntries ORDER BY Sequence DESC` | The latest entries with the users' emails and roles |
| `UPDATE InvoiceHistoryEntries SET ActorEmail = 'x'` | Error 51000 "Invoice history entries cannot be changed or deleted."; nothing changed |
| `DELETE FROM InvoiceHistoryEntries` | The same error; nothing deleted |

## 5. Automated

```powershell
dotnet test backend/tests/BillingCrm.Application.Tests
dotnet test backend/tests/BillingCrm.Api.Tests
dotnet test backend/tests/BillingCrm.Infrastructure.Tests      # needs LocalDB
cd frontend; ng test --watch=false; ng build
```

Run the three backend projects one at a time on a busy machine. All must pass.
