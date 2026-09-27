---

description: "Task list for 003-customers"
---

# Tasks: Customers

**Input**: Design documents from `/specs/003-customers/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/customers-api.md, quickstart.md

**Tests**: Included. FR-018 requires automated tests, and contracts/customers-api.md lists contract
tests that map to test tasks below. Write each story's tests first and confirm they fail.

**Organization**: Grouped by user story (US1 add a customer and see it in the list, US2 find
customers quickly, US3 edit a customer and change its active state).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run
from the repository root unless a task says otherwise; run frontend commands from PowerShell.

**Rules that apply to every task**: use the `minimal-api-conventions`, `angular-ui-conventions` and
`sql-migrations` skills for matching edits; expected failures (validation, duplicate email, not found)
are `Result` → `ProblemDetails`, never exceptions; no connection string, key or credential in any
committed file (the integration tests build a credential-free LocalDB connection in code, never a
literal); every API endpoint declares its access explicitly; **there is no delete operation anywhere**;
nothing about invoices, shop, cart, payments, tickets or WhatsApp; Arabic and English text must
round-trip unchanged.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and the new test project. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the six items in plan.md "Patterns and decisions to confirm": (1) `/api/customers` CRUD endpoint pattern with no delete, (2) shared `PagedResult<T>`, (3) `Try*` store port, (4) the LocalDB integration-test project and its credential-free in-code connection, (5) the Angular URL-state list / one form / problem-details helper patterns, (6) "delete means deactivate" and the spec defaults. If (4) is rejected, change T002, T010, T034, T044 and the README note; if (6) changes the spec (for example a delete is wanted), stop and run `/speckit-clarify` first. Record the answers in `specs/003-customers/research.md` (R2, R5, R6, R11, R12)
- [X] T002 [P] Create test project `backend/tests/BillingCrm.Infrastructure.Tests` (xUnit, net8.0) referencing `BillingCrm.Infrastructure`, `BillingCrm.Application` and `BillingCrm.Domain`; add it to `backend/BillingCrm.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain type, shared types, port, table, and the three test harnesses.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 [P] Add `CustomerType` (enum `Company`, `Individual`) in `backend/src/BillingCrm.Domain/CustomerType.cs` and `EmailNormalizer.Normalize(string)` (trim + upper-invariant) in `backend/src/BillingCrm.Domain/EmailNormalizer.cs`; make `StaffUser.NormalizeEmail` in `backend/src/BillingCrm.Domain/StaffUser.cs` call it so both use one implementation
- [X] T004 Add the `Customer` entity in `backend/src/BillingCrm.Domain/Customer.cs` per data-model.md: private setters and an EF constructor; `Create(name, type, email, phone, vatNumber, address, nowUtc)` (active, `CreatedAtUtc == UpdatedAtUtc == nowUtc`, sets `NormalizedEmail`); `Update(...)` and `SetActive(bool, nowUtc)` that change `UpdatedAtUtc` only when a value really changes and return whether it did (depends on T003)
- [X] T005 [P] Add `PagedResult<T>(Items, Page, PageSize, TotalCount, TotalPages)` in `backend/src/BillingCrm.Application/Common/PagedResult.cs`
- [X] T006 [P] Add the customer DTOs in `backend/src/BillingCrm.Application/Customers/CustomerModels.cs`: `CustomerDto` (all fields plus timestamps), `CustomerListItem`, `CustomerInput` (one record for add and edit: name, type, email, phone, vatNumber, address; all nullable strings as they come from JSON; the task originally named separate create/update requests), `SetActiveRequest(bool? IsActive)`, `CustomerQuery(string? Q, string? Type, string? Active, string? Page, string? PageSize)` (all strings so a bad value is a named field error) plus the validated form `CustomerSearch(string? Text, CustomerType? Type, bool? IsActive, int Page, int PageSize)`
- [X] T007 Add the port `ICustomerStore` in `backend/src/BillingCrm.Application/Customers/ICustomerStore.cs`: `FindByIdAsync(Guid)`, `EmailExistsAsync(string normalizedEmail, Guid? excludeId)`, `TryAddAsync(Customer)` and `TryUpdateAsync(Customer)` (both return `false` only for a duplicate normalized email, otherwise `true`), `SearchAsync(CustomerSearch)` → `PagedResult<Customer>` (depends on T004, T005, T006)
- [X] T008 Add `DbSet<Customer>` to `backend/src/BillingCrm.Infrastructure/Persistence/BillingDbContext.cs` and `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/CustomerConfiguration.cs`: table `Customers`, columns per data-model.md (`Name` nvarchar(200), `Type` stored as the enum name nvarchar(20) with check constraint `CK_Customers_Type`, `Email`/`NormalizedEmail` nvarchar(256), `Phone` nvarchar(20), `VatNumber` nvarchar(30) nullable, `Address` nvarchar(500), `IsActive` bit, `CreatedAtUtc`/`UpdatedAtUtc` datetime2), unique index `IX_Customers_NormalizedEmail`, index `IX_Customers_Name` (depends on T004)
- [X] T009 Create migration `AddCustomers` with `dotnet ef migrations add AddCustomers --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api --output-dir Persistence/Migrations`, review it against data-model.md, apply with `dotnet ef database update ...`, and confirm with sqlcmd that `Customers` exists with the unique index, the name index and the check constraint, and that no invoice/product table exists (depends on T008)
- [X] T010 [P] Add the integration-test harness in `backend/tests/BillingCrm.Infrastructure.Tests/LocalDbFixture.cs`: an xUnit class fixture that builds a credential-free LocalDB connection **in code** with `SqlConnectionStringBuilder` (server `(localdb)\MSSQLLocalDB`, integrated security, generated database name `BillingCrm_Tests_<guid>`; optional override server from env var `BILLINGCRM_TEST_SERVER`), applies the real migrations, exposes `CreateContext()`, and deletes only that generated database on dispose; if LocalDB is unreachable it fails with a message saying so (never skips). Add `MigrationSmokeTests.cs` asserting the `Customers` table, the unique index and the check constraint exist (depends on T002, T009)
- [X] T011 [P] Add `FakeCustomerStore` (in-memory, tracks calls, can be told to return `false` from `TryAddAsync`/`TryUpdateAsync` to simulate the race) in `backend/tests/BillingCrm.Application.Tests/Fakes/FakeCustomerStore.cs` (depends on T007)
- [X] T012 [P] Add `InMemoryCustomerStore : ICustomerStore` in `backend/tests/BillingCrm.Api.Tests/InMemoryCustomerStore.cs` (LINQ-based: unique normalized email, contains/ignore-case search over name/email/phone/VAT, filters, sort by name then id, paging), register it in `ApiFactory` (`RemoveAll<ICustomerStore>` then singleton), and add `CreateAuthorizedClient(this ApiFactory, string role = "Agent")` in `backend/tests/BillingCrm.Api.Tests/AuthorizedClientExtensions.cs` using `TestTokens` (depends on T007)
- [X] T013 [P] Add `frontend/src/app/core/customers/customer.models.ts`: `CustomerType`, `Customer`, `CustomerListItem`, `CustomerInput`, `CustomerListQuery` (`q`, `type`, `active`, `page`, `pageSize`), `PagedResult<T>`
- [X] T014 [P] Add `frontend/src/app/core/http/problem-details.ts` and `problem-details.spec.ts`: `applyProblemToForm(form, error)` maps a 400 `errors` object to the matching form controls (camelCase names), a 409 to the `email` control using the API's `detail`, and returns a top-level message for anything else (including status 0 and unexpected bodies); never throws on a malformed body

**Checkpoint**: `dotnet build backend/BillingCrm.sln` succeeds; every existing back-end and front-end test still passes (in particular the 002 tests using `StaffUser.NormalizeEmail`); `Customers` exists in LocalDB; the integration smoke test passes.

---

## Phase 3: User Story 1 - Add a customer and see it in the list (Priority: P1) MVP

**Goal**: A signed-in staff member adds a customer, sees it in the list and its details page; a second
customer with the same email (any case, any spacing) is refused with a clear message and creates nothing.

**Independent Test**: Sign in, add one customer and see it in the list and on its details page; add a
second with the same email and get the clear message with the typed values still on screen.

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [X] T015 [P] [US1] `CustomerValidator` tests in `backend/tests/BillingCrm.Application.Tests/CustomerValidatorTests.cs`: every FR-005 rule (name required and 200-character limit and exactly-200 accepted; type required and only `Company`/`Individual`; email required, 256 limit, no `@`, two `@`, no domain dot, spaces; phone required, 7 to 20 characters, allowed characters only; address required and 500 limit; VAT optional, 30 limit, disallowed characters), trimming of every field, empty VAT becomes null, several bad fields reported together with camelCase keys
- [X] T016 [P] [US1] `CustomerService` create/get/list tests in `backend/tests/BillingCrm.Application.Tests/CustomerServiceCreateTests.cs` (with `FakeCustomerStore` and a fixed `TimeProvider`): creates an active customer with equal timestamps and trimmed values; duplicate email pre-check → `Conflict` "Email already in use" for exact, different case and surrounding spaces; `TryAddAsync` returning false (race) → the same `Conflict`; nothing stored on any refusal; get returns the customer; get of an unknown id → `NotFound`; list with default paging
- [X] T017 [P] [US1] Endpoint tests in `backend/tests/BillingCrm.Api.Tests/CustomerCreateEndpointTests.cs` (contracts/customers-api.md tests 1-3, 8-9 for these routes): `POST /api/customers` → 201 with `Location`, body, `isActive` true; the new customer appears in `GET /api/customers`; duplicate email (exact, `  ACCOUNTS@... `) → 409 problem details with the exact title and detail and the list count unchanged; each validation rule → 400 naming the field; empty and malformed body → 400; a body containing `isActive: false` still creates an active customer; `GET /api/customers/{id}` 200, unknown id 404, non-GUID id 404; every operation without a token → 401; both Admin and Agent tokens work
- [X] T018 [P] [US1] Store tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfCustomerStoreCreateTests.cs` (using `LocalDbFixture`): `TryAddAsync` persists every field including an Arabic name and a multi-line address exactly; a second add with the same normalized email returns false and stores nothing; the unique index itself rejects a duplicate inserted with raw SQL (proves the guarantee without the pre-check); the type check constraint rejects an invalid value inserted with raw SQL; `SearchAsync` with default paging returns rows ordered by name then id
- [X] T019 [P] [US1] Angular specs: `frontend/src/app/core/customers/customer.service.spec.ts` (create posts the input, get, list builds the query string), `frontend/src/app/features/customers/customer-form.component.spec.ts` (required errors and nothing sent; a 409 puts the API message on the email field and keeps every typed value; a 400 maps to fields; the submit button is disabled while saving; success navigates to the new customer's details), `customer-list.component.spec.ts` (loading, empty "No customers match", error with retry, rows show an **Inactive** marker, paginator shows the total), `customer-detail.component.spec.ts` (shows every field, active badge, created/changed times; unknown id shows "Customer not found")

### Implementation for User Story 1 (back end)

- [X] T020 [US1] Implement `CustomerValidator` in `backend/src/BillingCrm.Application/Customers/CustomerValidator.cs`: trims all text, applies the rules in research R4 / contracts, returns a validated value object or a `Validation` `Error` with camelCase field keys; empty VAT → null (depends on T006)
- [X] T021 [US1] Implement `CustomerService` in `backend/src/BillingCrm.Application/Customers/CustomerService.cs`: `CreateAsync` (validate → normalize email → `EmailExistsAsync` pre-check → `Customer.Create` with `TimeProvider` → `TryAddAsync`; both duplicate paths return the same `Error.Conflict("email_in_use", "Email already in use", "Another customer already uses this email address.")`), `GetAsync` (`NotFound` "Customer not found"), `SearchAsync` (validates `page` ≥ 1 and `pageSize` 1-100 with defaults 1 and 20, returns `PagedResult<CustomerListItem>`; filters are added in US2); register the service in `backend/src/BillingCrm.Application/DependencyInjection.cs` (depends on T007, T020)
- [X] T022 [P] [US1] Implement `EfCustomerStore` in `backend/src/BillingCrm.Infrastructure/Customers/EfCustomerStore.cs`: `FindByIdAsync`, `EmailExistsAsync`, `TryAddAsync` (catch `DbUpdateException` whose inner `SqlException.Number` is 2601 or 2627 → detach the entity and return false; anything else rethrows), `SearchAsync` with paging only (order by `Name` then `Id`, `Skip/Take`, separate count); register it scoped in `backend/src/BillingCrm.Infrastructure/DependencyInjection.cs` (depends on T007, T008)
- [X] T023 [US1] Add `backend/src/BillingCrm.Api/Endpoints/CustomerEndpoints.cs`: `MapGroup("/api/customers").RequireAuthorization()`; `POST /` (201 with `Location: /api/customers/{id}`, failures via `ToProblem()`), `GET /` (binds `page` and `pageSize`), `GET /{id}` (route value parsed as a GUID; a non-GUID is a 404 problem); map it in `Program.cs` (depends on T021, T022)
- [X] T024 [US1] After T023, explain the customer endpoints to the developer in 5 lines using MVC words (per `minimal-api-conventions` skill); no file output

### Implementation for User Story 1 (front end)

- [X] T025 [US1] Add `frontend/src/app/core/customers/customer.service.ts` (HttpClient service per resource): `create`, `get`, `list(query)` against `{apiBaseUrl}/api/customers` (depends on T013)
- [X] T026 [US1] Add `frontend/src/app/features/customers/customer-form.component.ts`: standalone Material reactive form (name, type select, email, phone, VAT number, address textarea), `dir="auto"` on text controls, per-field error messages, uses `applyProblemToForm`, keeps all values on any refusal, disables the submit button while saving; add mode now (edit mode arrives in US3), navigates to `/customers/:id` on success (depends on T014, T025)
- [X] T027 [US1] Add `frontend/src/app/features/customers/customer-detail.component.ts`: shows every field, an active/inactive badge, created and changed times (local time), loading and "Customer not found" states (edit and toggle controls arrive in US3) (depends on T025)
- [X] T028 [US1] Add `frontend/src/app/features/customers/customer-list.component.ts`: `mat-table` (name, type, email, phone, VAT number, **Inactive** marker), `mat-paginator` driven by the `page`/`pageSize` query parameters, **Add customer** button, loading / empty ("No customers match") / error-with-retry states; rows link to details; search and filters arrive in US2 (depends on T025)
- [X] T029 [US1] Add routes `customers`, `customers/new`, `customers/:id` as children of the signed-in shell in `frontend/src/app/app.routes.ts` (put `customers/new` before `customers/:id`), and a **Customers** link in `frontend/src/app/core/layout/shell.component.ts` (inside the existing `<nav>`, shown to every signed-in user); update `shell.component.spec.ts` and `app.spec.ts` for the link and a customers route (depends on T026-T028)
- [X] T030 [US1] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false` in `frontend/` (from PowerShell); fix until T015-T019 pass
- [X] T031 [US1] Manual check on the real database and API: sign in with curl, `POST` a customer (201), the same email again (409), the same email with different case and spaces (409), an invalid body (400 with field names), `GET` it back and list it; record results and the Arabic round-trip in `specs/003-customers/research.md` (depends on T023)

**Checkpoint**: US1 acceptance scenarios 1-7 pass via tests; the browser check is in T057.

---

## Phase 4: User Story 2 - Find customers quickly (Priority: P2)

**Goal**: Search by name, email, phone and VAT fragment; filter by type and by active status; combine
them; page through results; Arabic and English both work.

**Independent Test**: With several customers of both types (some inactive), search each field, apply
both filters and their combination, and see the empty-result message when nothing matches.

### Tests for User Story 2

- [X] T032 [P] [US2] Query-validation tests in `backend/tests/BillingCrm.Application.Tests/CustomerServiceSearchTests.cs`: `page` < 1, `pageSize` 0 and 101, unknown `type`, non-boolean `active` each produce a `Validation` failure naming that field; valid values (including `pageSize` 100 and `active` `true`/`false`) pass; whitespace-only `q` means no text; results map to `CustomerListItem`
- [X] T033 [P] [US2] Search endpoint tests in `backend/tests/BillingCrm.Api.Tests/CustomerSearchEndpointTests.cs` (contracts tests 4-6, fake store): `q` matches each of name, email, phone, VAT fragments case-insensitively; `type` and `active` filters and their combination with `q`; omitted `active` returns both active and inactive; response shape `items/page/pageSize/totalCount/totalPages`; no match → 200 with empty items and totals 0; a page beyond the last → 200 empty items with the real total; invalid `type`, `active`, `page`, `pageSize` → 400 naming the field
- [X] T034 [P] [US2] Store search tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfCustomerStoreSearchTests.cs`: contains and case-insensitive matching on name, email, phone and VAT (this also fails loudly if the database collation is case-sensitive); `%`, `_` and `[` in `q` match literally; an Arabic substring of an Arabic name matches; type filter; active filter; combination; ordering stable across pages (no duplicates or gaps); `totalCount`; page beyond the end; **10,000 customers seeded in batches, first page of a `q` search returns in under 1 second after one warm-up query**
- [X] T035 [P] [US2] Angular specs: extend `customer-list.component.spec.ts` (typing in the search box updates the `q` query parameter after a debounce and resets `page` to 1; the type and status selects update `type` and `active`; opening `/customers?q=ac&type=Company&active=true&page=2` restores the controls and requests exactly that; "No customers match" appears for an empty result and the typed text stays) and `customer.service.spec.ts` (all filters appear in the request and blank ones do not)

### Implementation for User Story 2

- [X] T036 [US2] Extend `CustomerService.SearchAsync` in `backend/src/BillingCrm.Application/Customers/CustomerService.cs` to parse and validate `type` (`Company`/`Individual`) and `active` (`true`/`false`), trim `q` (blank = none) and pass a `CustomerSearch` to the store (depends on T021)
- [X] T037 [US2] Extend `EfCustomerStore.SearchAsync` in `backend/src/BillingCrm.Infrastructure/Customers/EfCustomerStore.cs`: filter by `IsActive` and `Type`; for text `q` apply `EF.Functions.Like` with pattern `%q%` (escape `\`, `%`, `_` and `[` with an escape character) across `Name`, `Email`, `Phone` and `VatNumber`; keep order and paging (depends on T022)
- [X] T038 [US2] Extend `GET /api/customers` in `backend/src/BillingCrm.Api/Endpoints/CustomerEndpoints.cs` to bind `q`, `type`, `active` (as strings so bad values become field errors) in addition to `page` and `pageSize` (depends on T023, T036)
- [X] T039 [US2] Extend `frontend/src/app/features/customers/customer-list.component.ts` and `customer.service.ts`: debounced (about 300 ms) search box, type select (All/Company/Individual), status select (Both/Active/Inactive), all state in the URL query string via `Router`/`ActivatedRoute`, page reset to 1 on any change, requests derived from the query parameters (depends on T028, T037)
- [X] T040 [US2] Run all back-end and front-end tests; fix until T032-T035 pass
- [X] T041 [US2] Manual check on the real database and API: search by name, email, phone and VAT fragments, an Arabic fragment, `%` literal, both filters and their combination via curl; record the 10,000-row timing printed by the T034 test in `specs/003-customers/research.md` (depends on T038)

**Checkpoint**: US2 acceptance scenarios 1-7 pass via tests.

---

## Phase 5: User Story 3 - Edit a customer and change its active state (Priority: P3)

**Goal**: Correct a customer's details (own email allowed, someone else's refused), and mark it
inactive or active again; inactive customers stay listed with everything intact.

**Independent Test**: Edit a customer, try to take another customer's email, deactivate and reactivate,
and see an unknown customer reported as not found.

### Tests for User Story 3

- [X] T042 [P] [US3] Service tests in `backend/tests/BillingCrm.Application.Tests/CustomerServiceUpdateTests.cs`: valid update changes fields and `UpdatedAtUtc`; saving with the customer's own email (any case) is not a duplicate; another customer's email → `Conflict` and the customer is unchanged; `TryUpdateAsync` false (race) → the same `Conflict`; a save that changes nothing leaves `UpdatedAtUtc` alone; validation as on create; unknown id → `NotFound`; `SetActiveAsync` deactivate/reactivate, idempotent (no timestamp change when already in that state), unknown id → `NotFound`, missing `isActive` → `Validation`
- [X] T043 [P] [US3] Endpoint tests in `backend/tests/BillingCrm.Api.Tests/CustomerUpdateEndpointTests.cs` (contracts tests 2, 3, 7, 8): `PUT /api/customers/{id}` 200, own email 200, another's email (exact and different case) 409 and unchanged, 400 per rule, 404 unknown and non-GUID; `PUT /api/customers/{id}/active` true/false 200, idempotent, missing or non-boolean body 400, 404; a deactivated customer still appears in the default list and in `active=false`; deactivated customer's email still refuses a new customer (409); `DELETE /api/customers/{id}` is not allowed (405 or 404); every route without a token → 401
- [X] T044 [P] [US3] Store tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfCustomerStoreUpdateTests.cs`: `TryUpdateAsync` persists changes including Arabic text; updating to another customer's normalized email returns false and the stored row keeps its old email (reload to prove it); saving a customer's own email succeeds; a deactivated customer's email stays reserved (`TryAddAsync` with it returns false); the row and all fields survive deactivation
- [X] T045 [P] [US3] Angular specs (written as `customer-form-edit.component.spec.ts`, `customer-detail-toggle.component.spec.ts` and `customer.service.update.spec.ts`): `customer-form.component.spec.ts` edit mode (pre-filled from `get`, saves with `PUT`, a 409 on email keeps values and shows the message, unknown id shows "Customer not found"), `customer-detail.component.spec.ts` (Deactivate then Activate call the API and update the badge; Edit link goes to `/customers/:id/edit`; failures show an error and keep the previous state), `customer.service.spec.ts` (`update` and `setActive` requests)

### Implementation for User Story 3

- [X] T046 [US3] Add `UpdateAsync` and `SetActiveAsync` to `backend/src/BillingCrm.Application/Customers/CustomerService.cs` (find → `NotFound`; validate; pre-check with `excludeId` = the customer's own id; `Customer.Update`/`SetActive`; `TryUpdateAsync` false → `Conflict`; unchanged → return the customer without saving) (depends on T021)
- [X] T047 [US3] Implement `TryUpdateAsync` in `backend/src/BillingCrm.Infrastructure/Customers/EfCustomerStore.cs`: save the tracked entity; on SQL error 2601/2627 reset the entity to its stored values (reload) and return false; anything else rethrows (depends on T022)
- [X] T048 [US3] Add `PUT /{id}` and `PUT /{id}/active` to `backend/src/BillingCrm.Api/Endpoints/CustomerEndpoints.cs` (same group, same access; no delete route is mapped) (depends on T023, T046)
- [X] T049 [US3] After T048, explain the two endpoints to the developer in 5 lines using MVC words; no file output
- [X] T050 [US3] Frontend: add `update` and `setActive` to `customer.service.ts`; add edit mode to `customer-form.component.ts` (route `customers/:id/edit`, pre-filled, same error handling); add the Edit link and the Deactivate/Activate button to `customer-detail.component.ts`; add the `customers/:id/edit` route in `app.routes.ts` (keep `customers/new` listed before the `:id` routes) (depends on T026, T027)
- [X] T051 [US3] Run all back-end and front-end tests; fix until T042-T045 pass
- [X] T052 [US3] Manual check on the real database and API with curl: edit a customer, save with its own email (200), take another customer's email (409, unchanged), deactivate (still listed, `isActive` false), reactivate, `DELETE` (rejected), unknown id (404); record results in `specs/003-customers/research.md`

**Checkpoint**: US3 acceptance scenarios 1-6 pass via tests.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T053 [P] Update `README.md`: the `AddCustomers` migration step, the Customers screens and what to check, that the integration tests need LocalDB and create/remove their own `BillingCrm_Tests_*` database, troubleshooting for "LocalDB unreachable" in the new test project
- [X] T054 [P] Update `CLAUDE.md` layout and run commands: `Customers` in Domain/Application/Infrastructure/Api, `PagedResult<T>`, the `Try*` store pattern, `BillingCrm.Infrastructure.Tests` (needs LocalDB), the customer routes, the rule that customers are never deleted
- [X] T055 [P] Secret and hygiene scan of everything committable: no connection-string literal in the new test project (only the code-built LocalDB parts), no credentials, no leftover throwaway databases in LocalDB (`sqlcmd` list databases named `BillingCrm_Tests_%`; drop only stragglers created by failed runs)
- [X] T056 [P] Confirm the access-declaration tests (every endpoint declares access; the anonymous set is exactly `/health` and login) still pass with the customer routes, and that no route or UI control can delete a customer (search `backend/src` and `frontend/src` for delete operations on customers)
- [X] T057 Run the full quickstart validation (`specs/003-customers/quickstart.md`) on the developer's machine, including all browser checks (add, duplicate email message with values kept, Arabic name, search and filters, Back button restoring the list, edit, deactivate/reactivate, not found, signed-out redirect); the developer confirms it is running (Constitution II)
- [X] T058 Run `api-reviewer`, `angular-reviewer` and `spec-auditor`; fix or explicitly record every finding (Constitution workflow gate)
- [X] T059 Re-run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; all pass (SC-007)
- [X] T060 Mark spec 003 done only after T057 and T058 are complete; do not start spec 004 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates all code generation; T002 is independent.
- **Foundational**: T003 → T004 → (T007, T008); T005, T006 parallel with T003; T007 needs T004-T006; T009 after T008; T010 after T002 and T009; T011 and T012 after T007; T013, T014 independent of the back end.
- **US1**: after Foundational. Back end (T020-T024) and front end (T025-T029) are independent tracks; T031 needs T023.
- **US2**: after US1 (extends the same service, store, endpoint and list component); its tests T032-T035 can be written as soon as US1 is done.
- **US3**: after US1; independent of US2 except that both edit `CustomerEndpoints.cs`, `EfCustomerStore.cs` and `customer.service.ts` (do them in order: US2 then US3).
- **Polish**: after all stories. T057 and T058 are the done-ness gate.

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories.
- **US2 (P2)**: builds on US1's list.
- **US3 (P3)**: builds on US1's details page and form; shares files with US2.

### Within Each Story

Tests first and failing → implementation → run tests. Back end: validator → service → store → endpoint. Front end: service → components → routes/shell.

### Parallel Opportunities

- Setup/Foundational: T002 with T003; T005, T006; T010, T011, T012, T013, T014 once their dependencies are done.
- US1: tests `T015-T019` together; then `T020` and `T022` together, and `T025` alongside the back-end tasks.
- US2: tests `T032-T035` together.
- US3: tests `T042-T045` together.
- Polish: T053-T056.

---

## Parallel Example: User Story 1

```text
# Tests, all at once:
T015 CustomerValidatorTests.cs   T016 CustomerServiceCreateTests.cs   T017 CustomerCreateEndpointTests.cs
T018 EfCustomerStoreCreateTests.cs   T019 customer service/form/list/detail specs

# Then two tracks:
Back end:   (T020 || T022) -> T021 -> T023 -> T024 -> T031
Front end:  T025 -> (T026 || T027 || T028) -> T029
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001-T002) and Foundational (T003-T014).
2. US1 (T015-T031).
3. Stop and validate: add a customer, see it listed, and see the duplicate-email refusal. This is the
   request's stated acceptance scenario.

### Incremental Delivery

1. Add US2 (T032-T041): search, filters, paging.
2. Add US3 (T042-T052): edit and active state.
3. Polish, three reviewers, developer verification (T053-T060).

---

## Notes

- T001 is a hard gate; do not generate code before the developer answers.
- The invoice-time blocking of inactive customers belongs to the invoice spec; this spec only stores
  and exposes `isActive`.
- Commit only when the developer asks; there are no commits yet.
