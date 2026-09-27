# Quickstart: Customers

**Feature**: 003-customers | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 and 002 (LocalDB, connection string, signing key in
user-secrets).

## Prerequisites

Specs 001 and 002 working: you can sign in as `admin@crm.local` / `agent1@crm.local`.

## 1. Create the customers table

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies the `AddCustomers` migration. No new secrets or settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate (maps to the spec's acceptance scenarios)

| Check | Expected |
|-------|----------|
| Sign in as either user, open **Customers** | Empty state ("No customers match" / nothing yet) with an **Add customer** button (US2.5) |
| Add "Acme Trading", Company, `accounts@acme.example`, a phone, an address, no VAT number | Saved, shown on its details page as **Active**, with created and changed times; listed (US1.1, US1.2, US1.7) |
| Add a second customer with the **same** email | Refused; the email field shows "Another customer already uses this email address."; everything typed is still on screen; the list still has one customer (US1.3, US1.6, SC-002) |
| Repeat with `  ACCOUNTS@ACME.EXAMPLE ` | Refused the same way (US1.4) |
| Save with an empty phone, or email `not-an-email` | Field errors, nothing saved (US1.5) |
| Add an Arabic-named customer (for example "شركة الأمل") and search for part of the name | Found; text aligns naturally (US2.7, FR-017) |
| Add a few more, then search by name, email, phone and VAT fragments; use the type and status filters; combine them | Correct results each time; blank result set shows "No customers match" (US2.1-2.5) |
| Add more than 20 customers (or set page size lower in the URL) | Paginator shows total and page; Back button after changing a filter restores the previous list (US2.6) |
| Edit a customer's phone and address | Saved; "last changed" time moves (US3.1) |
| Edit a customer without touching its email | Saves (not a duplicate of itself) (US3.2) |
| Change one customer's email to another's | Refused with the same message; email unchanged (US3.3) |
| **Deactivate** a customer | Still listed with an **Inactive** marker; all fields unchanged; **Activate** restores it (US3.4, US3.5, SC-004) |
| Open `/customers/00000000-0000-0000-0000-000000000000` | "Customer not found" (US3.6) |
| Sign out and open `/customers` | Sent to sign-in (SC-006) |
| Same requests to the API with no token, e.g. `curl http://localhost:5080/api/customers` | `401` (SC-006) |
| Confirm there is no way to delete a customer in the UI or API | No delete control; `DELETE /api/customers/{id}` returns `405`/`404` (FR-009) |
| `dotnet test backend/BillingCrm.sln`; `cd frontend; ng test --watch=false` | All pass (FR-018, SC-007). The integration tests need LocalDB and create and remove their own `BillingCrm_Tests_*` database |

Contract details: [contracts/customers-api.md](contracts/customers-api.md). Data model:
[data-model.md](data-model.md).
