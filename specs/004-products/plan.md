# Implementation Plan: Products

**Branch**: `004-products` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-products/spec.md`

## Summary

Add the price list. A `Products` table (migration `AddProducts`) with a unique index on the already-normalized
SKU and check constraints on price, VAT and unit; a `ProductService` in Application that validates, normalizes,
detects duplicates and returns `Result`s; an EF store whose `TryAdd`/`TryUpdate` turn a unique-index violation
into a conflict instead of an exception; a `/api/products` group where reading is open to any signed-in user and
the three write operations are Admin-only (403 for an Agent); a default of "active only" for the list, with an
Admin-only status filter to reveal deactivated products; and Angular list/detail/form screens that hide the
change controls from Agents. It is the first spec with money, so exact-decimal handling (refuse a third
decimal, never round) is set here for invoices to copy. No permanent delete: "deactivate to hide". Decisions
are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new packages. `EF.Functions.Like` (already used by customers); Angular Material
table, paginator, select, chips/badge, already installed

**Storage**: SQL Server LocalDB; new table `Products` via migration `AddProducts`

**Testing**: xUnit Application unit tests (fake store), API tests (`WebApplicationFactory`, in-memory fake store,
Admin and Agent tokens from `TestTokens`), the existing `BillingCrm.Infrastructure.Tests` project against a
throwaway LocalDB database; Vitest for Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: first page of a search over 5,000 products in under 1 s (SC-006); add a product in under
1 minute (SC-001)

**Constraints**: duplicate SKU guaranteed by a database unique index, not only a pre-check; no exception for
expected failures; every endpoint declares access; Agent cannot change anything (403 before body binding);
`decimal(18,2)` SAR price, never `float`/`double`; no rounding of a value the person typed; Arabic and English
names; no delete; no inventory

**Scale/Scope**: about 5,000 products; 5 API operations (3 Admin-only); 3 new screens plus a shell link

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design — still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Products are one of the three permitted domains. No inventory/stock, invoices, shop, cart, payments, tickets or WhatsApp (FR-020). |
| II. Sequential Spec Delivery | Pass | 003 was verified running by the developer on 2026-09-24 and closed. 004 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | Validation, duplicate SKU, not found are `Result` failures mapped to `ProblemDetails`; a unique-index violation is translated to a conflict `Result` inside Infrastructure. The Agent's 403 comes from the existing authorization pipeline, not an exception. |
| IV. Financial Data Integrity | Pass (one reading to confirm) | First money field: `UnitPrice` is `decimal(18,2)` SAR end to end, a value with more than two decimals is refused rather than rounded, and no `float`/`double` is used at any layer. `VatPercent` is a percentage, stored as `decimal(5,2)` (R7): confirm this reading. No secrets; the Infrastructure tests keep using the credential-free LocalDB connection built in code. |
| V. Deliberate API/UI Design | Pass | The items below are to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations`, and `invoice-money`
for the money rules. Before the spec counts as done run `api-reviewer`, `angular-reviewer` and `spec-auditor`.
`security-reviewer` is also run: this is the first spec where the Admin/Agent split is enforced per operation on
a business resource, although no authentication code changes.

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (resource CRUD group with `201 + Location`, `Result` to `ProblemDetails`,
`PagedResult<T>`, `Try*` store port with the unique-index fallback, URL-driven list state, one add/edit form,
`problem-details.ts`, no delete) need no re-explanation. New in this spec:

1. **Mixed-role endpoint group** — `/api/products`: group requires sign-in, the three writes add
   `AuthorizationPolicies.AdminOnly` (R2). First group where the role decides per operation.
2. **Role-dependent read visibility** — the endpoint turns `IsInRole("Admin")` into a plain
   `includeInactive` boolean for the service; an Agent never sees inactive products and gets 404 for one by id (R3).
3. **"Deactivate to hide"** — default list is active only; `status=active|inactive|all` for Admins (R1, R4).
   Different from customers, whose inactive rows stay listed.
4. **Money pattern** — `decimal?` in the JSON body, `decimal(18,2)` for price, refuse more than two decimals,
   VAT default 15 on add only, and `VatPercent` as `decimal(5,2)` because it is a percentage (R7).
5. **SKU stored normalized** — upper-case, `A-Z 0-9 - _` only, unique index directly on `Sku`, no second
   column (R6). Arabic letters are not allowed in a SKU (the spec's assumption was tightened for this).
6. **Angular role-aware screens** — `roleGuard('Admin')` on `/products/new` and `/products/:id/edit`;
   Add/Edit/Deactivate and the Status filter hidden from Agents; price shown with two decimals for display only.
   The customer list is copied and adapted, **not** generalized into a shared base component (R11).
7. **Defaults in the spec's Assumptions** — no permanent delete, name ≤ 200 / SKU ≤ 50, price 0 allowed,
   maximum 999,999,999.99, last save wins, no seed products.

## Project Structure

### Documentation (this feature)

```text
specs/004-products/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── products-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 003 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── ProductUnit.cs                     # enum Hour, Item, Service
│   └── Product.cs                         # entity: Create/Update/SetActive
├── BillingCrm.Application/
│   └── Products/
│       ├── IProductStore.cs               # FindById, SkuExists, TryAdd, TryUpdate, Search
│       ├── ProductService.cs              # Create, Update, Get, Search, SetActive -> Result
│       ├── ProductValidator.cs            # trims, normalizes SKU, money rules -> field errors
│       └── ProductModels.cs               # DTOs / ProductInput / ProductQuery / ProductSearch
├── BillingCrm.Infrastructure/
│   ├── Persistence/
│   │   ├── BillingDbContext.cs            # + DbSet<Product>
│   │   ├── Configurations/ProductConfiguration.cs
│   │   └── Migrations/                    # AddProducts
│   ├── Products/EfProductStore.cs         # LIKE search, paging, unique-violation -> false
│   └── DependencyInjection (existing)     # register IProductStore
└── BillingCrm.Api/
    ├── Endpoints/ProductEndpoints.cs      # /api/products group, writes AdminOnly
    └── Program.cs                         # MapProductEndpoints(); ProductService DI (with customers')

backend/tests/
├── BillingCrm.Application.Tests/          # ProductService / validator tests
├── BillingCrm.Api.Tests/                  # ApiFactory gains InMemoryProductStore; Agent 403 / Admin contract tests
└── BillingCrm.Infrastructure.Tests/       # unique SKU index, check constraints, decimals, search, 5,000-row timing

frontend/src/app/
├── core/
│   ├── products/{product.service.ts, product.models.ts}
│   └── layout/shell.component.ts          # + Products link
├── features/products/
│   ├── product-list.component.ts          # copied/adapted from customers; role-aware controls
│   ├── product-form.component.ts          # add + edit
│   └── product-detail.component.ts
└── app.routes.ts                          # /products, /products/new, /products/:id, /products/:id/edit (new/edit: roleGuard('Admin'))

README.md, CLAUDE.md                       # products table step, products API, layout
```

**Structure Decision**: Extend the 003 layout in place, one folder per concern per layer, exactly as customers.
No new projects, packages or shared abstractions.

## Known gaps carried forward

- Invoices offering only active products and copying price and VAT into invoice lines (specs 005/006); permanent
  deletion, optimistic concurrency, categories, price history, import/export, user-selectable sort, full RTL layout.

## Complexity Tracking

No constitution violations to justify. The only departures from the customers pattern (SKU stored normalized,
status filter, role-dependent visibility, `decimal(5,2)` VAT) are each listed for confirmation above.
