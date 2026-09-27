# Quickstart: Products

**Feature**: 004-products | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001, 002 and 003 (LocalDB, connection string, signing key in
user-secrets).

## Prerequisites

Specs 001-003 working: you can sign in as `admin@crm.local` and `agent1@crm.local` (see CLAUDE.md, "Seed Users").

## 1. Create the products table

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies the `AddProducts` migration. No new secrets or settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate as Admin (maps to the spec's acceptance scenarios)

| Check | Expected |
|-------|----------|
| Sign in as `admin@crm.local`, open **Products** | Empty state ("No products match") with **Add product** and a Status filter (US2.5) |
| Add name "Consulting hour", SKU `CNS-001`, unit Hour, price 100, VAT 15; then refresh the list (F5) | Saved, details page shows **Active**, `CNS-001`, Hour, `100.00`, 15%; it is in the list after the refresh (US1.1, SC-001) |
| Add another product and leave VAT empty | Saved with VAT 15. With VAT `0` it saves as 0 (US1.2, US1.3) |
| Add a product with SKU `cns-001`, then ` CNS-001 ` | Refused on the SKU field: "Another product already uses this SKU."; everything typed stays; list unchanged (US1.4, US1.7, SC-002) |
| Save with an empty name, price `-1`, price `10.005`, VAT `101`, SKU `AB CD` | Field errors, nothing saved (US1.6) |
| Add "استشارة" (Arabic name) and search for part of it; search `cns`; filter unit = Hour | Found each time; searches combine (US2.3, US2.4, US2.10) |
| Edit a product's price and VAT | Saved; "last changed" moves (US3.1); saving with its own SKU is fine (US3.2) |
| Change one product's SKU to another's | Refused with the same message; SKU unchanged (US3.3) |
| **Deactivate** a product | It leaves the default list; Status = Inactive or All shows it marked **Inactive**; fields unchanged (US3.4, SC-004) |
| Try to add a product with the deactivated product's SKU | Refused: the SKU is still reserved (US3.6) |
| **Activate** it | Back in the default list (US3.5) |
| Open `/products/00000000-0000-0000-0000-000000000000` | "Product not found" (US3.8) |

## 4. Validate as Agent

| Check | Expected |
|-------|----------|
| Sign out, sign in as `agent1@crm.local`, open **Products** | Same list, **no** Add button, **no** Status filter, no inactive products (US2.1, US2.8) |
| Open a product | Details only; no Edit or Deactivate (US2.7, US2.8) |
| Type `http://localhost:4200/products/new` (and `/products/<id>/edit`) | "You do not have access to this area." (US1.5) |
| Open the address of a deactivated product (copied as Admin) | "Product not found" (US2.8) |

## 5. Validate the API directly

```powershell
# Get an Admin and an Agent token (the sign-in response has accessToken); then:
curl -i -X POST http://localhost:5080/api/products -H "Authorization: Bearer <AGENT-TOKEN>" -H "Content-Type: application/json" -d "{\"name\":\"X\",\"sku\":\"X-1\",\"unit\":\"Item\",\"unitPrice\":1}"
```

| Request | Expected |
|---------|----------|
| The call above (Agent token) | `403`, and no `X-1` product exists afterwards (US1.5, SC-003) |
| Same body with the Admin token | `201` with `Location` |
| `GET /api/products?status=all` with the Agent token | Only active products |
| `GET /api/products` with **no** token | `401` (SC-007) |
| `DELETE /api/products/{id}` (Admin) | `405`/`404`: there is no delete (FR-012) |

## 6. Automated tests

```powershell
dotnet test backend/BillingCrm.sln
cd frontend; ng test --watch=false
```

All pass (FR-019, SC-008). The Infrastructure tests need LocalDB and create and remove their own
`BillingCrm_Tests_*` database.

Contract details: [contracts/products-api.md](contracts/products-api.md). Data model:
[data-model.md](data-model.md).
