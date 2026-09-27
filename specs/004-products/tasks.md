---

description: "Task list for 004-products"
---

# Tasks: Products

**Input**: Design documents from `/specs/004-products/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/products-api.md, quickstart.md

**Tests**: Included. FR-019 requires automated tests, and contracts/products-api.md lists contract tests that
map to test tasks below. Write each story's tests first and confirm they fail.

**Organization**: Grouped by user story (US1 Admin adds a product and sees it in the list, US2 everyone browses
and searches the price list, US3 Admin edits a product and deactivates it to hide it).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run from the
repository root unless a task says otherwise; run frontend commands from PowerShell.

**Rules that apply to every task**: use the `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations`
and `invoice-money` skills for matching edits; expected failures (validation, duplicate SKU, not found) are
`Result` → `ProblemDetails`, never exceptions; money is `decimal` everywhere (`decimal(18,2)` for the price) and
never `float`/`double`, and a value with more than two decimals is refused, never rounded; no connection string,
key or credential in any committed file; every API endpoint declares its access explicitly; **the three write
endpoints are Admin-only and read endpoints are open to any signed-in user**; **there is no delete operation
anywhere**; **no stock, quantity or inventory field anywhere**; nothing about invoices, shop, cart, payments,
tickets or WhatsApp; Arabic and English names must round-trip unchanged; do not modify customer behaviour
(003 is closed).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the six new items in plan.md "Patterns and decisions to confirm": (1) mixed-role `/api/products` group (reads any signed-in user, writes `AdminOnly`), (2) role-dependent visibility passed as an `includeInactive` boolean (Agent never sees inactive, 404 by id), (3) "deactivate to hide" with the `status=active|inactive|all` filter, (4) the money pattern (`decimal?` in JSON, refuse a third decimal, VAT 15 on add only, `VatPercent` as `decimal(5,2)`), (5) SKU stored normalized with the unique index directly on `Sku` and A-Z only, (6) Angular role-aware screens with the customer list copied, not generalized. If (3) is rejected in favour of "inactive stays listed", stop and run `/speckit-clarify` first (it changes FR-010/FR-011 and US2/US3); if (4) or (5) change, adjust T005, T007, T014, T019 and the contract. Record the answers in `specs/004-products/research.md` (R1-R3, R6, R7, R11)
- [X] T002 Baseline check before any change: `dotnet test backend/BillingCrm.sln` and (from PowerShell, in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` pass, `sqllocaldb i` lists `MSSQLLocalDB`, and `dotnet ef migrations list --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api` shows `AddCustomers` applied. Stop the background API before running `dotnet ef`/tests if it locks the build output

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain type, shared types, port, table, the three test harnesses, and the one shared helper change.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 [P] Add `ProductUnit` (enum `Hour`, `Item`, `Service`) in `backend/src/BillingCrm.Domain/ProductUnit.cs`
- [X] T004 Add the `Product` entity in `backend/src/BillingCrm.Domain/Product.cs` per data-model.md: private setters and an EF constructor; `Create(name, sku, unit, unitPrice, vatPercent, nowUtc)` (active, `CreatedAtUtc == UpdatedAtUtc == nowUtc`; the caller passes the already-normalized SKU); `Update(...)` and `SetActive(bool, nowUtc)` that change `UpdatedAtUtc` only when a value really changes and return whether it did (as `Customer`). `UnitPrice` and `VatPercent` are `decimal`; no stock or quantity members (depends on T003)
- [X] T005 [P] Add the product types in `backend/src/BillingCrm.Application/Products/ProductModels.cs`: `ProductDto` (all fields plus timestamps), `ProductListItem` (id, name, sku, unit, unitPrice, vatPercent, isActive), `ProductInput` (one record for add and edit: `string? Name`, `string? Sku`, `string? Unit`, `decimal? UnitPrice`, `decimal? VatPercent`), `ProductActiveRequest(bool? IsActive)` (a product-side twin; customers' `SetActiveRequest` is left untouched), `ProductQuery(string? Q, string? Unit, string? Status, string? Page, string? PageSize)` (all strings so a bad value is a named field error), the enum `ProductStatusFilter { Active, Inactive, All }` and the validated `ProductSearch(string? Text, ProductUnit? Unit, ProductStatusFilter Status, int Page, int PageSize)`. Reuse `PagedResult<T>` from `Common` unchanged (depends on T003)
- [X] T006 Add the port `IProductStore` in `backend/src/BillingCrm.Application/Products/IProductStore.cs`: `FindByIdAsync(Guid)`, `SkuExistsAsync(string sku, Guid? excludeId)`, `TryAddAsync(Product)` and `TryUpdateAsync(Product)` (both return `false` only for a duplicate SKU, otherwise `true`), `SearchAsync(ProductSearch)` → `PagedResult<Product>` (depends on T004, T005)
- [X] T007 Add `DbSet<Product>` to `backend/src/BillingCrm.Infrastructure/Persistence/BillingDbContext.cs` and `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/ProductConfiguration.cs`: table `Products`, columns per data-model.md (`Name` nvarchar(200), `Sku` nvarchar(50), `Unit` stored as the enum name nvarchar(20) with check constraint `CK_Products_Unit`, `UnitPrice` **decimal(18,2)** with check `CK_Products_UnitPrice` (`>= 0`), `VatPercent` **decimal(5,2)** with check `CK_Products_VatPercent` (`BETWEEN 0 AND 100`), `IsActive` bit, `CreatedAtUtc`/`UpdatedAtUtc` datetime2 tagged UTC on read as in `CustomerConfiguration`), unique index `IX_Products_Sku`, index `IX_Products_Name` (depends on T004)
- [X] T008 Create migration `AddProducts` with `dotnet ef migrations add AddProducts --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api --output-dir Persistence/Migrations`, review it against data-model.md (exact column types, the three check constraints, both indexes; nothing unrelated), apply with `dotnet ef database update ...`, and confirm with sqlcmd that `Products` exists with the unique index, the name index and the check constraints, that `Customers` and `StaffUsers` are untouched, and that no invoice table or stock column exists (depends on T007)
- [X] T009 [P] Add `FakeProductStore` (in-memory, tracks calls, can be told to return `false` from `TryAddAsync`/`TryUpdateAsync` to simulate the race, records the last `ProductSearch`) in `backend/tests/BillingCrm.Application.Tests/Fakes/FakeProductStore.cs` (depends on T006)
- [X] T010 [P] Add `InMemoryProductStore : IProductStore` in `backend/tests/BillingCrm.Api.Tests/InMemoryProductStore.cs` (LINQ-based: unique SKU, contains/ignore-case search over name and SKU, unit and status filters, sort by name then id, paging) and register it in `ApiFactory` (`RemoveAll<IProductStore>` then singleton) exactly as the customer store is; the existing `CreateAuthorizedClient(role)` and `TestTokens.Admin()/Agent()` are reused (depends on T006)
- [X] T011 [P] Extend `backend/tests/BillingCrm.Infrastructure.Tests/MigrationSmokeTests.cs`: the `Products` table, `IX_Products_Sku` (unique), `IX_Products_Name` and the three check constraints exist, `UnitPrice` is `decimal(18,2)` and `VatPercent` is `decimal(5,2)` (read from `INFORMATION_SCHEMA`), and no column named like stock/quantity exists (depends on T008)
- [X] T012 [P] Add `frontend/src/app/core/products/product.models.ts`: `ProductUnit` (`'Hour' | 'Item' | 'Service'`), `Product`, `ProductListItem`, `ProductInput`, `ProductListQuery` (`q`, `unit`, `status`, `page`, `pageSize`), `ProductStatus` (`'active' | 'inactive' | 'all'`); reuse the existing `PagedResult<T>` type (import it, do not copy it) if it is exported, otherwise move nothing and re-declare the same shape once here
- [X] T013 [P] Generalize `frontend/src/app/core/http/problem-details.ts` without changing customer behaviour: `applyProblemToForm(form, error, options?)` gets an optional `{ conflictField?: string; subject?: string }` defaulting to `{ conflictField: 'email', subject: 'customer' }` (the 409 message goes on `conflictField`; the fallback 400 message uses `subject`). Extend `frontend/src/app/core/http/problem-details.spec.ts`: all existing cases still pass unchanged, and with `{ conflictField: 'sku', subject: 'product' }` a 409 lands on the `sku` control and the fallback text says "product"

**Checkpoint**: `dotnet build backend/BillingCrm.sln` succeeds; every existing back-end and front-end test still passes (in particular all customer tests); `Products` exists in LocalDB; the migration smoke test passes.

---

## Phase 3: User Story 1 - Admin adds a product and sees it in the list (Priority: P1) MVP

**Goal**: An Admin adds a product (SKU `CNS-001`, price 100, VAT 15) and sees it in the list and on its details
page; a second product with the same SKU (any case, any spacing) is refused with a clear message and creates
nothing; invalid values are refused per field; an Agent cannot add a product (403 from the API, access-denied
page in the UI).

**Independent Test**: Sign in as Admin, add `CNS-001` at 100 / VAT 15, refresh the list and see it; add the same
SKU again and get the clear message with the typed values still on screen; sign in as Agent and see the add page
refused and `POST /api/products` return 403.

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [X] T014 [P] [US1] `ProductValidator` tests in `backend/tests/BillingCrm.Application.Tests/ProductValidatorTests.cs`: name required, 200 accepted, 201 refused; SKU required, trimmed and upper-cased (`"  cns-001 "` → `CNS-001`), 50 accepted and 51 refused, allowed characters only (space inside, `#`, `.`, Arabic letters and `é` refused; `_` and `-` accepted); unit required and only `Hour`/`Item`/`Service` in any letter case (`"1"` and `"Box"` refused); unit price required, `0` accepted, `-0.01` refused, `999999999.99` accepted, `1000000000` refused, `10.005` refused (never rounded), `100`, `100.5`, `100.50`, `100.500` accepted with the value unchanged; VAT: null on **add** → 15, `0` kept as 0, `100` accepted, `100.01` and `-1` refused, `15.005` refused, null on **edit** → required error; several bad fields reported together with camelCase keys `name`, `sku`, `unit`, `unitPrice`, `vatPercent`; a test that the validator never converts through `double` (for example `0.10m` and `123456789.12m` come back identical)
- [X] T015 [P] [US1] `ProductService` create/get/list tests in `backend/tests/BillingCrm.Application.Tests/ProductServiceCreateTests.cs` (with `FakeProductStore` and a fixed `TimeProvider`): creates an active product with equal timestamps, trimmed name, upper-cased SKU and VAT 15 when omitted; duplicate SKU pre-check → `Conflict` "SKU already in use" for exact, different case and surrounding spaces; the SKU of an **inactive** product still conflicts; `TryAddAsync` returning false (race) → the same `Conflict`; nothing stored on any refusal; `GetAsync(id, includeInactive: true)` returns any product; `GetAsync(id, includeInactive: false)` of an inactive product → `NotFound` identical to an unknown id; unknown id → `NotFound`; list with default paging returns active products only when `includeInactive` is false
- [X] T016 [P] [US1] Endpoint tests in `backend/tests/BillingCrm.Api.Tests/ProductCreateEndpointTests.cs` (contracts/products-api.md tests 1-4, 9-10 for these routes): Admin `POST /api/products` with SKU `CNS-001`, price 100, VAT 15 → 201 with `Location`, body active, `sku` `CNS-001`; it then appears in `GET /api/products`; VAT omitted → 15, VAT `0` → 0; duplicate SKU (exact, `cns-001`, ` CNS-001 `) → 409 problem details with the exact title and detail and the list count unchanged; each validation rule → 400 naming the field (three-decimal price, negative price, over-maximum price, VAT 100.01, bad SKU characters, unknown unit, unit given as a number); empty and malformed body → 400; a body containing `isActive: false` still creates an active product; `GET /api/products/{id}` 200, unknown id 404, non-GUID id 404; **Agent token `POST` → 403 (title `Forbidden`) with a valid body, with an invalid body and with a malformed body, and nothing is created**; Agent `GET` list and `GET /{id}` → 200; every operation without a token → 401
- [X] T017 [P] [US1] Store tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfProductStoreCreateTests.cs` (using `LocalDbFixture`): `TryAddAsync` persists every field including an Arabic name exactly; `100.10` and `999999999.99` round-trip as exactly the same decimals (`UnitPrice`) and `15.00`/`0.00` for `VatPercent`; a second add with the same SKU returns false and stores nothing; the unique index itself rejects a duplicate inserted with raw SQL (proves the guarantee without the pre-check) and also a duplicate differing only in letter case; the check constraints reject a negative price, VAT 101 and an unknown unit inserted with raw SQL; `SearchAsync` with default paging returns rows ordered by name then id
- [X] T018 [P] [US1] Angular specs: `frontend/src/app/core/products/product.service.spec.ts` (create posts the input, get, list builds the query string), `frontend/src/app/features/products/product-form.component.spec.ts` (VAT pre-filled with 15; required errors and nothing sent; a negative price and a price with three decimals are refused in the form; a 409 puts the API message on the **SKU** field and keeps every typed value; a 400 maps to fields; the submit button is disabled while saving; success navigates to the new product's details), `product-list.component.spec.ts` (loading, empty "No products match", error with retry, price shown as SAR with two decimals, an **Inactive** marker, paginator total, **Add product** visible for Admin and absent for Agent), `product-detail.component.spec.ts` (shows every field, active badge, created/changed times, price with two decimals; unknown id shows "Product not found"), and route-guard checks in `frontend/src/app/app.routes.spec.ts` or the existing routing spec (Agent navigating to `/products/new` ends on the access-denied page; Admin reaches the form)

### Implementation for User Story 1 (back end)

- [X] T019 [US1] Implement `ProductValidator` in `backend/src/BillingCrm.Application/Products/ProductValidator.cs`: trims all text, upper-cases the SKU and checks it against `A-Z 0-9 - _` on the ASCII range only (not `char.IsLetter`), parses `Unit` case-insensitively from the three names only, checks scale with `value == decimal.Round(value, 2)` and the ranges (price 0 to 999,999,999.99; VAT 0 to 100), takes a flag for "default the VAT to 15 when null" (true for add, false for edit), and returns a validated value object or a `Validation` `Error` with camelCase keys. No `double`/`float` anywhere (depends on T005)
- [X] T020 [US1] Implement `ProductService` in `backend/src/BillingCrm.Application/Products/ProductService.cs`: `CreateAsync` (validate → `SkuExistsAsync` pre-check → `Product.Create` with `TimeProvider` → `TryAddAsync`; both duplicate paths return the same `Error.Conflict("sku_in_use", "SKU already in use", "Another product already uses this SKU.")`), `GetAsync(id, includeInactive)` (`NotFound` "Product not found" / "No product exists with this id." for unknown **and** for inactive-when-not-allowed), `SearchAsync(query, includeInactive)` (validates `page` ≥ 1 and `pageSize` 1-100 with defaults 1 and 20, same parsing style as `CustomerService`, returns `PagedResult<ProductListItem>`; `unit`, `status` and `q` are added in US2, until then the default is active-only when `includeInactive` is false); register the service in `backend/src/BillingCrm.Application/DependencyInjection.cs` (depends on T006, T019)
- [X] T021 [P] [US1] Implement `EfProductStore` in `backend/src/BillingCrm.Infrastructure/Products/EfProductStore.cs`: `FindByIdAsync`, `SkuExistsAsync`, `TryAddAsync` (catch `DbUpdateException` whose inner `SqlException.Number` is 2601 or 2627 **and whose message names `IX_Products_Sku`** → detach the entity and return false; anything else rethrows), `SearchAsync` with paging and the active-only default (order by `Name` then `Id`, long-arithmetic offset as `EfCustomerStore`, separate count); register it scoped in `backend/src/BillingCrm.Infrastructure/DependencyInjection.cs` (depends on T006, T007)
- [X] T022 [US1] Add `backend/src/BillingCrm.Api/Endpoints/ProductEndpoints.cs`: `MapGroup("/api/products").RequireAuthorization()`; `POST /` with `.RequireAuthorization(AuthorizationPolicies.AdminOnly)` (201 with `Location: /api/products/{id}`, failures via `ToProblem()`); `GET /` (binds `page` and `pageSize`) and `GET /{id}` (route value parsed as a GUID; a non-GUID is the same 404 problem) open to any signed-in user; both reads compute `includeInactive` from `ClaimsPrincipal.IsInRole(nameof(StaffRole.Admin))` and pass only that boolean to the service; map the group in `Program.cs` next to `MapCustomerEndpoints()` (depends on T020, T021)
- [X] T023 [US1] After T022, explain the product endpoints to the developer in 5 lines using MVC words (per `minimal-api-conventions` skill), including why the Agent's 403 happens before the body is read; no file output

### Implementation for User Story 1 (front end)

- [X] T024 [US1] Add `frontend/src/app/core/products/product.service.ts` (HttpClient service per resource): `create`, `get`, `list(query)` against `{apiBaseUrl}/api/products` (depends on T012)
- [X] T025 [US1] Add `frontend/src/app/features/products/product-form.component.ts`: standalone Material reactive form (name, SKU with `dir="ltr"`, unit select with the three units, unit price, VAT percent pre-filled with 15), `dir="auto"` on the name, per-field error messages (required, price ≥ 0, at most two decimals, VAT 0-100), the price and VAT sent exactly as typed and never re-rounded, uses `applyProblemToForm(form, error, { conflictField: 'sku', subject: 'product' })`, keeps all values on any refusal, disables the submit button while saving; add mode now (edit mode arrives in US3), navigates to `/products/:id` on success (depends on T013, T024)
- [X] T026 [US1] Add `frontend/src/app/features/products/product-detail.component.ts`: shows every field, price as SAR with two decimals (Angular decimal formatting, display only), an active/inactive badge, created and changed times (local time), loading and "Product not found" states (Edit and Deactivate arrive in US3) (depends on T024)
- [X] T027 [US1] Add `frontend/src/app/features/products/product-list.component.ts`: `mat-table` (name, SKU, unit, unit price in SAR, VAT %, **Inactive** marker), `mat-paginator` driven by the `page`/`pageSize` query parameters (unsafe values clamped, as the customer list does), an **Add product** button shown only when `AuthService.role()` is `Admin`, loading / empty ("No products match") / error-with-retry states, previous rows kept while a page loads and one `switchMap` stream so a slow old response never wins; rows link to details; search and filters arrive in US2 (depends on T024)
- [X] T028 [US1] Add routes `products`, `products/new`, `products/:id` as children of the signed-in shell in `frontend/src/app/app.routes.ts` (`products/new` before `products/:id`, and `products/new` carries `canActivate: [roleGuard('Admin')]` so an Agent lands on the existing access-denied page), and a **Products** link in `frontend/src/app/core/layout/shell.component.ts` (inside the existing `<nav>`, shown to every signed-in user); update `shell.component.spec.ts` and `app.spec.ts` for the link and a products route (depends on T025-T027)
- [X] T029 [US1] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false` in `frontend/` (from PowerShell); fix until T014-T018 pass and all customer tests still pass
- [X] T030 [US1] Manual check on the real database and API with curl: sign in as Admin and Agent, Admin `POST` `CNS-001` / 100 / VAT 15 (201) and it appears in `GET /api/products`; the same SKU again, as `cns-001` and as ` CNS-001 ` (409); an invalid body (400 with field names, including `unitPrice` 10.005); **Agent `POST` (403, including with a malformed body) and confirm nothing was created**; no token (401); Arabic name round-trip. Record results in `specs/004-products/research.md` (verification log) and delete any test rows you created (depends on T022)

**Checkpoint**: US1 acceptance scenarios 1-7 pass via tests; the browser check is in T056.

---

## Phase 4: User Story 2 - Everyone can browse and search the price list (Priority: P2)

**Goal**: Any signed-in user searches by name or SKU, filters by unit, pages through results and opens a product;
inactive products are hidden by default; an Admin can reveal them; Agents never see them and see no change controls.

**Independent Test**: With several products of all three units (some inactive), search name and SKU fragments,
filter by unit, combine them, switch the Admin status filter, and confirm an Agent sees neither inactive
products nor any Add/Edit/Deactivate control.

### Tests for User Story 2

- [X] T031 [P] [US2] Query-validation tests in `backend/tests/BillingCrm.Application.Tests/ProductServiceSearchTests.cs`: `page` < 1, `pageSize` 0 and 101, unknown `unit`, unknown `status` each produce a `Validation` failure naming that field; valid values (including `pageSize` 100, `unit` in any case, `status` `active`/`inactive`/`all`) pass; whitespace-only `q` means no text; `q` over 100 characters → `q` error; **with `includeInactive: false` the search passed to the store is always `Active` whatever `status` was sent, and an unknown `status` from a caller who may not use it is ignored rather than refused**; with `includeInactive: true` the requested status is passed through and the default is `Active`; results map to `ProductListItem`
- [X] T032 [P] [US2] Search endpoint tests in `backend/tests/BillingCrm.Api.Tests/ProductSearchEndpointTests.cs` (contract tests 5-7): `q` matches fragments of name and of SKU case-insensitively; `%` and `_` literal; `unit` filter alone and combined with `q`; default returns only active products; Admin `status=inactive` and `status=all`; **Agent `status=all` and `status=inactive` return only active products**; Admin bad `status` → 400 naming `status`; response shape `items/page/pageSize/totalCount/totalPages` with price and VAT as numbers; no match → 200 with empty items and totals 0; a page beyond the last (including 2147483647) → 200 empty items with the real total; invalid `unit`, `page`, `pageSize` → 400 naming the field; a blank `q` = no search
- [X] T033 [P] [US2] Store search tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfProductStoreSearchTests.cs`: contains and case-insensitive matching on name and SKU (this also fails loudly if the database collation is case-sensitive); `%`, `_` and `[` in `q` match literally; an Arabic substring of an Arabic name matches; unit filter; status `Active`/`Inactive`/`All`; combinations; ordering stable across pages (no duplicates or gaps); `totalCount`; page beyond the end and a huge page number give an empty page with the real total; **5,000 products seeded in batches, first page of a `q` search returns in under 1 second after one warm-up query** (SC-006)
- [X] T034 [P] [US2] Angular specs: extend `product-list.component.spec.ts` (typing in the search box updates the `q` query parameter after a debounce and resets `page` to 1; the unit select updates `unit`; for Admin the Status select updates `status`, for Agent the Status select and Add button are absent; opening `/products?q=cns&unit=Hour&status=all&page=2` restores the controls and requests exactly that; for an Agent a `status` in the address is not sent; "No products match" appears for an empty result and the typed text stays), `product.service.spec.ts` (all filters appear in the request and blank ones do not), and `product-detail.component.spec.ts` (an Agent sees no Edit/Deactivate controls; an inactive product for an Agent shows "Product not found")

### Implementation for User Story 2

- [X] T035 [US2] Extend `ProductService.SearchAsync` in `backend/src/BillingCrm.Application/Products/ProductService.cs`: parse and validate `unit` and `status` (`active`/`inactive`/`all`, any case), trim `q` (blank = none, max 100), force `ProductStatusFilter.Active` when `includeInactive` is false, and pass a `ProductSearch` to the store (depends on T020)
- [X] T036 [US2] Extend `EfProductStore.SearchAsync` in `backend/src/BillingCrm.Infrastructure/Products/EfProductStore.cs`: filter by status and unit; for text `q` apply `EF.Functions.Like` with pattern `%q%` (escape `\`, `%`, `_` and `[` with an escape character) across `Name` and `Sku`; keep order and paging (depends on T021)
- [X] T037 [US2] Extend `GET /api/products` in `backend/src/BillingCrm.Api/Endpoints/ProductEndpoints.cs` to bind `q`, `unit`, `status` (as strings so bad values become field errors) in addition to `page` and `pageSize`, still passing only the `includeInactive` boolean derived from the caller's role (depends on T022, T035)
- [X] T038 [US2] Extend `frontend/src/app/features/products/product-list.component.ts` and `product.service.ts`: debounced (about 300 ms) search box, unit select (All/Hour/Item/Service), a Status select (Active/Inactive/All) shown **only for Admin**, all state in the URL query string via `Router`/`ActivatedRoute`, page reset to 1 on any change, requests derived from the query parameters, `status` omitted from the request for an Agent (the detail page's Edit/Deactivate controls do not exist until US3, so there is nothing to hide there yet) (depends on T027, T036)
- [X] T039 [US2] Run all back-end and front-end tests; fix until T031-T034 pass
- [X] T040 [US2] Manual check on the real database and API with curl: search by name, SKU and Arabic fragments, `%` literal, the unit filter and its combination, `status=inactive|all` with the Admin token, and **`status=all` with the Agent token returns only active products**; record the 5,000-row timing printed by the T033 test in `specs/004-products/research.md` (depends on T037)

**Checkpoint**: US2 acceptance scenarios 1-10 pass via tests (scenario 8's "no Edit/Deactivate controls" is completed with T049).

---

## Phase 5: User Story 3 - Admin edits a product and deactivates it to hide it (Priority: P3)

**Goal**: An Admin corrects a product's details (its own SKU allowed, someone else's refused) and deactivates or
reactivates it; deactivated products leave the default list and every Agent's view but keep all data and their SKU.

**Independent Test**: As Admin, edit a product, try to take another product's SKU, deactivate it and see it leave
the default list, reveal it with the Status filter, reactivate it; as Agent, be refused (403 / access denied) on
every one of those.

### Tests for User Story 3

- [X] T041 [P] [US3] Service tests in `backend/tests/BillingCrm.Application.Tests/ProductServiceUpdateTests.cs`: valid update changes fields and `UpdatedAtUtc`; saving with the product's own SKU (any case) is not a duplicate; another product's SKU → `Conflict` and the product is unchanged; `TryUpdateAsync` false (race) → the same `Conflict`; a save that changes nothing leaves `UpdatedAtUtc` alone; validation as on create but a missing `vatPercent` → `vatPercent` error (no default on edit); unknown id → `NotFound`; an inactive product can be edited; `SetActiveAsync` deactivate/reactivate, idempotent (no timestamp change when already in that state), unknown id → `NotFound`, missing `isActive` → `Validation`; a deactivated product's SKU still refuses a new product
- [X] T042 [P] [US3] Endpoint tests in `backend/tests/BillingCrm.Api.Tests/ProductUpdateEndpointTests.cs` (contract tests 2, 3, 8-10): Admin `PUT /api/products/{id}` 200, own SKU 200, another's SKU (exact and different case) 409 and unchanged, 400 per rule, VAT omitted → 400, 404 unknown and non-GUID; `PUT /api/products/{id}/active` true/false 200, idempotent, missing or non-boolean body 400, 404; deactivated product leaves the default list, appears with `status=inactive` and `status=all` for Admin, Admin `GET /{id}` still 200 and can edit it; Agent `GET /{id}` of it → 404 with the same body as an unknown id; reactivating brings it back; **Agent `PUT /{id}` and `PUT /{id}/active` → 403 (valid, invalid and malformed bodies) and the product is unchanged**; `DELETE /api/products/{id}` is not allowed (405 or 404); every route without a token → 401
- [X] T043 [P] [US3] Store tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfProductStoreUpdateTests.cs`: `TryUpdateAsync` persists changes including Arabic text and a changed price (`100.10` → `250.55` exactly); updating to another product's SKU returns false and the stored row keeps its old SKU (reload to prove it); saving a product's own SKU succeeds; a deactivated product's SKU stays reserved (`TryAddAsync` with it returns false); the row and all fields survive deactivation
- [X] T044 [P] [US3] Angular specs: `product-form.component.spec.ts` edit mode (pre-filled from `get`, saves with `PUT`, a 409 on SKU keeps values and shows the message, unknown id shows "Product not found"), `product-detail.component.spec.ts` (for Admin: Deactivate then Activate call the API and update the badge, Edit link goes to `/products/:id/edit`, failures show an error and keep the previous state; for Agent: neither control is rendered), `product.service.spec.ts` (`update` and `setActive` requests), and the route guard on `/products/:id/edit` (Agent → access denied) (Angular spec files may be split as `product-form-edit`, `product-detail-toggle`, `product.service.update` as in 003)

### Implementation for User Story 3

- [X] T045 [US3] Add `UpdateAsync` and `SetActiveAsync` to `backend/src/BillingCrm.Application/Products/ProductService.cs` (find → `NotFound`; validate with no VAT default; pre-check with `excludeId` = the product's own id; `Product.Update`/`SetActive`; `TryUpdateAsync` false → `Conflict`; unchanged → return the product without saving; an inactive product is editable because the caller is an Admin) (depends on T020)
- [X] T046 [US3] Implement `TryUpdateAsync` in `backend/src/BillingCrm.Infrastructure/Products/EfProductStore.cs`: save the tracked entity; on SQL error 2601/2627 naming `IX_Products_Sku` reset the entity to its stored values (reload) and return false; anything else rethrows (depends on T021)
- [X] T047 [US3] Add `PUT /{id}` and `PUT /{id}/active` to `backend/src/BillingCrm.Api/Endpoints/ProductEndpoints.cs`, **both with `.RequireAuthorization(AuthorizationPolicies.AdminOnly)`** (same group; no delete route is mapped) (depends on T022, T045)
- [X] T048 [US3] After T047, explain the two endpoints to the developer in 5 lines using MVC words; no file output
- [X] T049 [US3] Frontend: add `update` and `setActive` to `product.service.ts`; add edit mode to `product-form.component.ts` (route `products/:id/edit`, pre-filled, same error handling); add the Edit link and the Deactivate/Activate button to `product-detail.component.ts`, rendered only when `AuthService.role()` is `Admin`; add the `products/:id/edit` route in `app.routes.ts` with `canActivate: [roleGuard('Admin')]` (keep `products/new` listed before the `:id` routes) (depends on T025, T026)
- [X] T050 [US3] Run all back-end and front-end tests; fix until T041-T044 pass
- [X] T051 [US3] Manual check on the real database and API with curl: Admin edits a product, saves with its own SKU (200), takes another's SKU (409, unchanged), omits VAT on edit (400), deactivates (gone from the default list, present with `status=all`, Agent `GET /{id}` 404), reactivates, a new product with the deactivated SKU (409), `DELETE` rejected, unknown id (404); **Agent `PUT` and `PUT /active` → 403 and the product is unchanged**. Record results in `specs/004-products/research.md` and delete any test rows

**Checkpoint**: US3 acceptance scenarios 1-8 pass via tests.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T052 [P] Update `README.md`: the `AddProducts` migration step, the Products screens and what to check as Admin and as Agent, the products API sentence (reads any signed-in user, writes Admin only, 409 duplicate SKU, no delete), the "no product tables yet" wording removed, troubleshooting for "The products could not be loaded" (missing table)
- [X] T053 [P] Update `CLAUDE.md` project line, layout and run commands: `Product`/`ProductUnit` in Domain, `Products/` in Application and Infrastructure, `/api/products` (mixed-role group, `AdminOnly` writes, `status` filter, `includeInactive` passed from the role), the money rule as first applied (refuse a third decimal), the routes `/products`, `/products/new`, `/products/:id`, `/products/:id/edit` and that products are never deleted
- [X] T054 [P] Secret and hygiene scan of everything committable: no connection-string literal or credential in the new files, no `float`/`double` on any product or money member (search `backend/src` and `frontend/src` for `double`, `float`, `Math.round` near price), no stock/quantity member, no leftover throwaway databases (`BillingCrm_Tests_%`) or test products in the developer's `BillingCrm` database
- [X] T055 [P] Confirm the access-declaration test (every endpoint declares access; the anonymous set is exactly `/health` and login) passes with the product routes, that exactly the three write routes require `AdminOnly` (add a test asserting the endpoint metadata if none exists), and that no route or UI control can delete a product (search `backend/src` and `frontend/src`)
- [X] T056 Run the full quickstart validation (`specs/004-products/quickstart.md`) on the developer's machine, including all browser checks as Admin (add `CNS-001`, refresh and see it, duplicate SKU message with values kept, validation messages, Arabic name, search and unit filter, edit, deactivate/hide, Status filter, reactivate, not found) and as Agent (read-only list and details, no controls, `/products/new` and `/products/:id/edit` refused, inactive product not found); the developer confirms it is running (Constitution II)
- [X] T057 Run `api-reviewer`, `angular-reviewer`, `security-reviewer` and `spec-auditor`; fix or explicitly record every finding (Constitution workflow gate). Give `security-reviewer` the Admin-only enforcement, the Agent's inactive-product invisibility and the absence of any detail leak in the 403/404 bodies as focus areas
- [X] T058 Re-run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; all pass (SC-008)
- [X] T059 Mark spec 004 done only after T056 and T057 are complete, and update `specs/004-products/spec.md` status; do not start spec 005 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates all code generation; T002 is independent.
- **Foundational**: T003 → T004 → (T006, T007); T005 parallel with T004 after T003; T006 needs T004 and T005; T008 after T007; T009 and T010 after T006; T011 after T008; T012 and T013 independent of the back end.
- **US1**: after Foundational. Back end (T019-T023) and front end (T024-T028) are independent tracks; T030 needs T022.
- **US2**: after US1 (extends the same service, store, endpoint and list component); its tests T031-T034 can be written as soon as US1 is done.
- **US3**: after US1; independent of US2 except that both edit `ProductEndpoints.cs`, `EfProductStore.cs`, `ProductService.cs`, `product.service.ts`, `product-list.component.ts`/`product-detail.component.ts` (do them in order: US2 then US3).
- **Polish**: after all stories. T056 and T057 are the done-ness gate.

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories.
- **US2 (P2)**: builds on US1's list and service.
- **US3 (P3)**: builds on US1's details page and form; shares files with US2.

### Within Each Story

Tests first and failing → implementation → run tests. Back end: validator → service → store → endpoint. Front end: service → components → routes/shell.

### Parallel Opportunities

- Foundational: T003 with T012 and T013; T005 with T004; T009, T010, T011 once their dependencies are done.
- US1: tests `T014-T018` together; then `T019` and `T021` together, and `T024` alongside the back-end tasks.
- US2: tests `T031-T034` together.
- US3: tests `T041-T044` together.
- Polish: T052-T055.

---

## Parallel Example: User Story 1

```text
# Tests, all at once:
T014 ProductValidatorTests.cs   T015 ProductServiceCreateTests.cs   T016 ProductCreateEndpointTests.cs
T017 EfProductStoreCreateTests.cs   T018 product service/form/list/detail specs + route guard

# Then two tracks:
Back end:   (T019 || T021) -> T020 -> T022 -> T023 -> T030
Front end:  T024 -> (T025 || T026 || T027) -> T028
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001-T002) and Foundational (T003-T013).
2. US1 (T014-T030).
3. Stop and validate: as Admin add `CNS-001` at 100 / VAT 15 and see it listed after a refresh, see the duplicate-SKU
   refusal, and as Agent see `POST` return 403 and the add page refused. These are the request's two stated
   acceptance scenarios.

### Incremental Delivery

1. Add US2 (T031-T040): search, unit and status filters, Agent read-only view.
2. Add US3 (T041-T051): edit and deactivate/reactivate.
3. Polish, four reviewers, developer verification (T052-T059).

---

## Notes

- T001 is a hard gate; do not generate code before the developer answers.
- Invoices offering only active products and copying price and VAT into invoice lines belong to the invoice specs;
  this spec only stores and exposes `isActive`, `unitPrice` and `vatPercent`.
- `security-reviewer` is included here (unlike 003) because this is the first per-operation Admin/Agent split on a
  business resource.
- Commit only when the developer asks; there are no commits yet.
