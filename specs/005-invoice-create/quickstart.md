# Quickstart: Create Draft Invoices

**Feature**: 005-invoice-create | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 004 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-004 working: you can sign in as `admin@crm.local` / `agent1@crm.local` (see CLAUDE.md, "Seed Users"). You need one
active customer (for example **ABC**) and one active product (for example `CNS-001`, price 100, VAT 15); add them in the app if
they do not exist.

## 1. Create the invoice tables and the number sequence

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies the `AddInvoices` migration (sequence `InvoiceNumbers`, tables `Invoices` and `InvoiceLines`). No new secrets or
settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate in the browser (maps to the spec's acceptance scenarios)

| Check | Expected |
|-------|----------|
| Sign in as either user, open **Invoices** | Empty state ("No invoices yet") with **Create invoice** (US3.2) |
| Create invoice: customer **ABC**, a due date, notes, **Add line**, product `CNS-001`, quantity `2`, **Save** | Saved; the details page shows status **Draft**, a number like `INV-000001`, customer ABC, exactly one line: Consulting hour, `CNS-001`, Hour, SAR 100.00, VAT 15%, quantity 2 (US1.1, US1.2, SC-008) |
| Create a second invoice | Its number is the next one, `INV-000002` (US1.2) |
| As Admin, change `CNS-001`'s price to 150 and VAT to 0 (Products), then reopen the first invoice | The line still shows 100.00 and 15% (US1.3, SC-003) |
| Create a new invoice with `CNS-001` again | The new line shows 150.00 and 0% (edge case: same product at different prices) |
| Deactivate `CNS-001` (Admin), then try **Create invoice** | The product picker does not offer it (US1.5); the first invoice still shows its line |
| Deactivate customer ABC, then **Create invoice** | The customer picker does not offer ABC (US1.4); ABC's existing invoices still open, with an **Inactive** mark on the customer (US3.4) |
| Enter quantity `0`, `-1`, `0.001`, `abc`, or leave it empty | The quantity field shows "greater than zero with at most two decimal places"; nothing is saved (US1.6). `1.5` is accepted |
| Leave the customer or due date empty, choose a past date, or save with no lines | Errors on those fields; nothing saved (US1.7); all entered values stay on screen (US1.8) |
| Open a Draft, **Edit**: change a quantity, **Add line**, **Remove** another line, change due date and notes, **Save** | Everything is saved; kept lines keep their saved price (US2.1-2.3, 2.5) |
| Edit a Draft that has one line and **Remove** it | Refused: a Draft needs at least one line (US2.4) |
| Edit a Draft whose product was deactivated | The existing line is still shown and can be kept, changed or removed; the picker will not offer the product (US2.6) |
| Open the list with more than 20 invoices (or `?pageSize=2` in the address) | Paginator with the total; newest first (US3.1) |
| Open `/invoices/00000000-0000-0000-0000-000000000000` | "Invoice not found" (US2.10) |
| Sign out and open `/invoices` | Sent to sign-in (US1.9, SC-007) |

## 4. Validate the API directly

```powershell
# Sign in for a token (the response has accessToken), then:
curl -i -X POST http://localhost:5080/api/invoices -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" -d "{\"customerId\":\"<CUSTOMER-ID>\",\"dueDate\":\"2030-01-31\",\"notes\":\"Net 30\",\"lines\":[{\"productId\":\"<PRODUCT-ID>\",\"quantity\":2}]}"
```

| Request | Expected |
|---------|----------|
| The call above | `201`, `Location`, `"status":"Draft"`, one line with `quantity` 2 and the product's saved values, a number `INV-0000NN` |
| The same call with `"quantity":0` or `"dueDate":"2000-01-01"` | `400` with `errors["lines[0].quantity"]` / `errors.dueDate`; nothing created |
| `PUT /api/invoices/{id}` with `lines` containing the existing line `{"id":"<LINE-ID>","quantity":3}` | `200`; quantity 3, same saved price |
| `GET /api/invoices` with no token | `401` |
| `DELETE /api/invoices/{id}` | `405`/`404`: there is no delete |

## 5. Automated tests

```powershell
dotnet test backend/BillingCrm.sln
cd frontend; ng test --watch=false
```

All pass (FR-020, SC-008). The Infrastructure tests need LocalDB and create and remove their own `BillingCrm_Tests_*`
database; they include the parallel invoice-number test and the 5,000-invoice list timing.

Contract details: [contracts/invoices-api.md](contracts/invoices-api.md). Data model: [data-model.md](data-model.md).
