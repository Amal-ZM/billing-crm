# Research: Customers

**Feature**: 003-customers | **Date**: 2026-09-24

No `NEEDS CLARIFICATION` markers remain in the spec. The items below record the decisions the plan
depends on. Items marked **Confirm** are new patterns or judgement calls that need the developer's
answer before code is generated (Constitution V) and are repeated in plan.md.

## R1. What "CRUD" means — no permanent delete (confirmed 2026-09-24)

- **Decision**: Create, read (list + single), update, and change the active state. There is no
  delete operation. Retiring a customer means marking it inactive (spec FR-009).
- **Rationale**: Invoices (spec 005+) will reference customers; deleting a customer with billing
  history would orphan or destroy those records. A reversible flag is what the request itself
  describes ("Inactive stays listed").
- **Alternatives considered**: hard delete (rejected: data loss, later referential problems); soft
  delete with a hidden flag (rejected: it is the same as inactive but hides the row, which the
  spec forbids).

## R2. Duplicate email must survive a race — confirmed 2026-09-24

- **Decision**: Two layers. (1) `CustomerService` checks `EmailExistsAsync(normalized, excludeId)`
  first so the common case gets a clean message without touching the database's error path.
  (2) A **unique index** on `NormalizedEmail` is the real guarantee. The Infrastructure store's
  `TryAddAsync` / `TryUpdateAsync` catch the SQL Server unique-violation errors (2601, 2627) and
  return `false`; the service turns `false` into the same `Conflict` result. No exception crosses
  the layer boundary for this expected business outcome.
- **Rationale**: Constitution III (expected failures are `Result`s) and spec edge case "two staff
  save at the same moment: exactly one succeeds". A pre-check alone has a race window; an index
  alone would surface as an exception.
- **Alternatives considered**: catching `DbUpdateException` in the service (rejected: leaks EF into
  Application); serialisable transactions (rejected: heavier than an index).

## R3. Email normalisation

- **Decision**: Reuse the 002 rule: trim, then upper-case invariant, stored in `NormalizedEmail`
  next to the email as entered (trimmed). Uniqueness and lookups use `NormalizedEmail`.
- **Rationale**: Independent of the database's collation; consistent with `StaffUser`.
- **Note**: The display `Email` keeps the person's casing; only comparison ignores it.

## R4. Validation lives in Application and returns field errors

- **Decision**: A `CustomerValidator` in Application trims every text field, applies the FR-005
  rules, and returns a `Validation` `Result` whose `errors` dictionary is keyed by camelCase field
  name (`name`, `type`, `email`, `phone`, `vatNumber`, `address`). The endpoint maps it with the
  existing `ToProblem()`.
- **Rationale**: Business rules belong in Application services (Minimal API skill); the same shape
  as 002's sign-in validation, so the UI can show errors per field.
- **Rules (from spec FR-005)**: name required ≤ 200; type ∈ {Company, Individual}; email required
  ≤ 256 and shaped like an email (one `@`, non-empty local part and a domain containing a dot,
  no spaces); phone required, 7–20 chars of digits, spaces, `+`, `-`, `(`, `)`; address required
  ≤ 500; VAT number optional ≤ 30 chars of letters, digits, spaces, hyphens (empty → null).
- **Email shape check**: a small explicit rule, not a full RFC 5322 parser; a real address is
  confirmed by use, not by regex.

## R5. Search implementation — confirmed 2026-09-24 (integration-tested against real SQL Server)

- **Decision**: One query in the EF store: filter by `IsActive` and `Type` when given; for text `q`
  (trimmed, whitespace-only = none) apply `EF.Functions.Like` with a `%q%` pattern against `Name`,
  `Email`, `Phone` and `VatNumber`, escaping `%`, `_` and `[` in `q` so they match literally; order
  by `Name` then `Id`; `Skip/Take` for the page; a separate `Count` for `totalCount`.
- **Rationale**: SQL Server's default collation is case-insensitive, so `LIKE` gives case-insensitive
  matching for Latin text; Arabic text is stored as `nvarchar` and matches by substring. At about
  10,000 rows a scan is well inside 1 s (SC-003), so no full-text index is needed yet.
- **Risk**: correctness depends on the database collation being case-insensitive (the LocalDB
  default is). An integration test asserts case-insensitive matching so a different collation
  fails loudly.
- **Alternatives considered**: `ToUpper().Contains` (translates to `UPPER`/`CHARINDEX`, defeats any
  index and still scans; no benefit); SQL full-text search (overkill at this size); client-side
  filtering (rejected: pages must come from the database).

## R6. List contract: paging, sort, limits (confirmed 2026-09-24, incl. shared PagedResult<T>)

- **Decision**: `GET /api/customers?q=&type=&active=&page=&pageSize=`. `page` ≥ 1 (default 1),
  `pageSize` 1–100 (default 20); out-of-range values are a **400** with field errors, not silently
  clamped. Response `{ items, page, pageSize, totalCount, totalPages }`. `type` accepts `Company` or
  `Individual`; `active` accepts `true` or `false`; omitting `active` returns both (spec FR-011).
  A new shared `PagedResult<T>` type lives in Application.
- **Rationale**: A clear contract for the UI and for later lists (products, invoices) that will
  reuse `PagedResult<T>` — a **new shared abstraction** (**Confirm**).
- **Alternatives considered**: cursor paging (rejected: need "page N of M" and total count);
  clamping bad values (rejected: hides client bugs).

## R7. Active state endpoint

- **Decision**: `PUT /api/customers/{id}/active` with `{ "isActive": true|false }`, idempotent:
  setting the current value returns 200 with the unchanged customer and does not touch
  `UpdatedAtUtc`; a real change updates it.
- **Rationale**: A separate, explicit operation keeps "edit details" and "retire/restore" distinct
  and easy to authorize separately later if the business wants Admin-only reactivation.
- **Alternatives considered**: `PATCH /{id}` with partial fields (rejected: partial-update semantics
  add rules nothing needs); `POST /{id}/deactivate` + `/activate` (two routes for one flag).

## R8. Timestamps

- **Decision**: `CreatedAtUtc` and `UpdatedAtUtc` (`datetime2`) set by the service from `TimeProvider`
  (already registered in 001/002). Editing details or changing the active state updates
  `UpdatedAtUtc`; a no-op save (no field changed) does not.
- **Rationale**: Testable time; UTC everywhere, converted for display in the browser.

## R9. Authorization

- **Decision**: The whole `/api/customers` group is `RequireAuthorization()` (any signed-in Admin or
  Agent); no role policy. Signed-out requests get 401 from the 002 pipeline. The existing test that
  every endpoint declares access covers the new routes.
- **Rationale**: Spec assumption: Admin and Agent have the same customer permissions.

## R10. Concurrency

- **Decision**: Last save wins; no `rowversion`/ETag in this spec (spec assumption). The unique
  index still prevents duplicate emails under concurrent saves.
- **Deferred**: optimistic concurrency can be added later without changing the contract's
  success paths (a 409/412 for a stale write).

## R11. Testing strategy — confirmed 2026-09-24 (LocalDB integration project)

- **Decision**: Three levels.
  1. **Application unit tests** (fake store): validation rules, trimming, duplicate pre-check, race
     (`TryAdd` returns false → conflict), own-email edit, not found, status idempotence, timestamps.
  2. **API tests** (`WebApplicationFactory`, in-memory fake store): HTTP contract, status codes,
     `ProblemDetails` shapes, auth (401), paging validation, that every endpoint declares access.
  3. **Infrastructure integration tests** (new project `BillingCrm.Infrastructure.Tests`) against a
     **throwaway LocalDB database**: apply the real migrations, unique index rejects duplicate
     normalized emails, check constraint on type, `LIKE` search semantics (contains, case, `%`/`_`
     literals, Arabic), filters and their combination, paging and stable order, 10,000-row search
     under 1 s.
- **Rationale**: Search semantics, the unique index, and Arabic handling can only be proven against
  SQL Server; the in-memory fake would prove nothing about them.
- **Trade-off**: `dotnet test` now needs LocalDB (already a documented prerequisite). Tests create a
  database named `BillingCrm_Tests_<guid>`, apply migrations, and delete **only that** database
  afterwards; the app database is never touched.
- **Connection string (Constitution IV boundary)**: the throwaway-database connection is *built in
  code* from parts (server `(localdb)\MSSQLLocalDB`, integrated security, generated database name),
  with an optional override from the environment variable `BILLINGCRM_TEST_SERVER`. LocalDB with
  Windows authentication has no credentials, so nothing secret exists, and no connection-string
  literal is committed. If LocalDB cannot be reached the tests fail with a message saying so rather
  than being skipped.
- **Alternatives considered**: EF InMemory or SQLite (rejected: different `LIKE`, collation and
  unique-violation behaviour, so they would give false confidence); skipping when LocalDB is absent
  (rejected: silently untested).

## R12. Angular structure — confirmed 2026-09-24

- **Decision**:
  - `core/customers/customer.service.ts` and `customer.models.ts` (HttpClient service per resource).
  - `features/customers/`: `customer-list.component.ts` (search box with debounce, type and status
    filters, `mat-table`, `mat-paginator`, loading/empty/error states), `customer-form.component.ts`
    (**one** component for both add and edit), `customer-detail.component.ts` (all fields, created and
    changed times, active toggle, edit link).
  - **List state lives in the URL query string** (`q`, `type`, `active`, `page`, `pageSize`) so the
    Back button and bookmarks restore the same view; the component reads the query params and the
    service is called from them.
  - A small shared helper `core/http/problem-details.ts` (**new shared abstraction**) that maps a
    `ProblemDetails` error to form-field errors: 400 `errors` → per field; 409 → the `email` field
    with the API's message; anything else → a top-level message.
  - Routes under the existing signed-in shell: `/customers`, `/customers/new`, `/customers/:id`,
    `/customers/:id/edit`; a **Customers** link in the shell for every signed-in user.
- **Rationale**: Reuses the 002 patterns (functional guards on the shell, HttpClient service per
  resource, Material only); URL-driven list state is the least code and gives Back-button correctness
  for free.
- **Alternatives considered**: component-local list state (rejected: Back loses the search); a
  state-management library (rejected: needs its own discussion per `.claude/rules/ui.md`); separate
  create and edit forms (rejected: duplicated validation messages).

## R13. Arabic and mixed text

- **Decision**: Columns are `nvarchar`; text inputs and table cells use `dir="auto"` so Arabic text
  aligns naturally without changing the rest of the page. Search is substring only (no
  diacritic/letter-variant folding). A full right-to-left UI is out of scope.
- **Rationale**: Spec FR-017 asks for correct storage, display and search, not a localised UI.
- **Known limit**: Arabic letter variants (for example أ / ا) are different characters to a substring
  search; folding them is a later enhancement.

## R14. Data and migration

- **Decision**: One migration `AddCustomers` creating `Customers` (see data-model.md) with a unique
  index on `NormalizedEmail`, an index on `Name` for the sort, and a check constraint limiting
  `Type` to `Company` / `Individual`. Applied with the existing `dotnet ef database update` step.
- **Rationale**: `sql-migrations` skill: add a migration, never edit an applied one, do not drop the
  database. No money columns exist.

## R15. Deliberately deferred

- Enforcing "inactive customers cannot get new invoices" (spec 005/006 — this spec only exposes
  `isActive`), permanent deletion, import/export, structured addresses, concurrency warnings, full
  RTL layout, Arabic letter-variant folding, user-selectable sort.

## Verification log

### T031 — US1 against the real LocalDB and API (2026-09-24)

- Signed in as the Agent; `POST /api/customers` with a multi-line address and an empty VAT number →
  201, `Location: /api/customers/{id}`, `vatNumber: null`, `isActive: true`, address returned with its
  line break.
- The same email again → 409 `Email already in use` / `Another customer already uses this email
  address.`; the same email in UPPERCASE with surrounding spaces → the same 409. The database held
  exactly 1 row for that normalized email.
- An invalid body → 400 naming `name`, `type`, `email`, `phone` and `address`; an Arabic name and
  address round-tripped unchanged; unknown id and a non-GUID id → 404; `pageSize=101` → 400 naming
  `pageSize`; no token → 401.
- **Finding (fixed):** in `Development` a malformed request body made minimal APIs *throw*, and the
  developer exception page answered with a stack trace and the request headers, **including the
  Bearer token**. Production and the tests gave a clean 400. Fixed in `Program.cs` with
  `RouteHandlerOptions.ThrowOnBadRequest = false`; verified on a real Development run (malformed and
  empty bodies now return `{"title":"Bad Request","status":400}` only). Genuine unhandled bugs still
  get the developer page, which is intended.
- Test-data note: my first attempt used invalid JSON (a raw newline inside a string) because of shell
  quoting; the server correctly refused it.

### T041 — US2 search, filters and paging against the real LocalDB and API (2026-09-24)

- Name (lowercase), email (UPPERCASE), phone and VAT fragments each found exactly the one expected
  customer; a `%` in the search text matched only the customer whose name contains a literal `%`
  ("100% Pure", not "100X Blend").
- `type=Individual`, `active=false` (the deactivated row only), `active=true&type=Company` and their
  combination with `q` returned the expected sets; with no `active` filter the inactive customer was
  listed too; a no-match search returned `totalCount` 0; `pageSize=2&page=2` returned the second page
  with the right total; `type=Nope` and `active=yes` → 400.
- Arabic: `q` sent as percent-encoded UTF-8 (`%D8%A7%D9%84%D9%86%D9%88%D8%B1`) found "مؤسسة النور".
  **Test-harness note:** typing Arabic directly as a command-line argument in this Windows shell is
  turned into `?????` before curl sends it (the URL showed `%3f%3f%3f%3f%3f`), which gave a false
  "0 results" on the first attempt. Send percent-encoded bytes when scripting Arabic queries; a browser
  encodes correctly.
- 10,000-customer timing (`EfCustomerStorePerformanceTests`, real LocalDB, first page, after a
  warm-up; limit 1000 ms): name fragment 178 ms, email 151 ms, phone 146 ms, VAT 121 ms, Arabic 101 ms,
  no text 3 ms, filters only 50 ms, text + filters 19 ms.
- Test-data note: the search tests also caught two mistakes of mine (a store that did not treat
  whitespace-only text as "no search" — now fixed defensively — and a wrong email fragment in the
  performance test).

### T052 — US3 edit, duplicates on edit, and active state against the real LocalDB and API (2026-09-24)

- Edit of name, type, phone, VAT number and address → 200; saving a customer with its **own** email in a
  different case → 200 (the stored casing follows what was typed; comparison ignores it).
- Taking another customer's email, exact case and UPPERCASE with surrounding spaces → 409 both times
  with the standard message; afterwards the customer still had its old name and email.
- `PUT /{id}/active` `{"isActive":false}` → 200 `isActive:false`; the customer stayed in the unfiltered
  list and in `active=false`, and not in `active=true`; **its email stayed reserved** (a new customer
  with that email → 409); deactivating again → 200 and `updatedAtUtc` unchanged; reactivating → 200
  `isActive:true`.
- `DELETE /api/customers/{id}` → 405 and the customer was still there; unknown and non-GUID ids → 404 on
  edit and on `/active`; a body without `isActive` → 400 naming `isActive`.

### T009 — migration and database facts confirmed with sqlcmd (2026-09-24)

After `dotnet ef database update` the real `BillingCrm` database contained exactly `Customers`, `StaffUsers`
and `__EFMigrationsHistory` (no product or invoice table), the check constraint `CK_Customers_Type`, the unique
index `IX_Customers_NormalizedEmail` and the index `IX_Customers_Name`. Its collation is
`SQL_Latin1_General_CP1_CI_AS`, which is case-insensitive, as the `LIKE` search relies on.

### T055 — hygiene (2026-09-24)

The signing key value appeared in 0 of 217 committable files; the new work contains no connection-string
literal or credential (the integration tests build a Windows-authentication LocalDB connection from parts);
no `BillingCrm_Tests_*` database was left in LocalDB after any run. The 10 customers I had created in the
developer's `BillingCrm` database during manual checks were deleted (all 10 matched my test patterns; the 2
staff accounts were untouched) so the developer's walkthrough starts from an empty list.

## Review disposition (T058, 2026-09-24)

Three read-only reviews ran: `api-reviewer`, `angular-reviewer`, `spec-auditor`. None reported a functional
mismatch with the spec.

### Fixed

- **Blocker (api-reviewer): a huge `page` overflowed the paging offset** (`(page-1) * pageSize` in `int`), giving a
  500 (and, in Development, the developer page) instead of the contracted empty 200. The store now uses `long`
  arithmetic and answers "no rows" without asking the database when the offset is past the total;
  `PagedResult` computes total pages with integers. Tests: Application, API and real-SQL for
  `int.MaxValue` and `21474838` pages.
- Search text over 100 characters is refused (400 naming `q`); the duplicate-email detection now checks that
  the violated index is `IX_Customers_NormalizedEmail` (a different unique failure is no longer reported as
  "email in use", proven with a primary-key clash); CORS now allows only `GET, POST, PUT` (it still listed
  `PATCH` and `DELETE`); the customer endpoints share one id-parsing/not-found helper.
- **The "two staff save the same email at the same moment" edge case is now proven by a real concurrent run**
  (10 simultaneous adds → exactly one 201 and nine 409; two simultaneous edits to the same new email →
  exactly one wins) with the real `CustomerService` on the real store and database.
- `LocalDbFixture`: the safety guard now checks the database the context will really delete; cleanup is best
  effort and also runs when migrating fails part-way.
- Length-limit messages are now asserted by text (name/email/address/VAT/phone) and via the API; limits count
  trimmed text on the front end too; a route-metadata test proves no customer route accepts DELETE and lists
  the five documented operations; the Arabic-Indic digit choice for phones is a deliberate, tested decision.
- **Front end (angular-reviewer):** stale/out-of-order list responses can no longer overwrite newer ones (one
  `switchMap` stream); the previous rows and the paginator now stay on screen while the next page loads (no
  lost keyboard focus, no flicker); hand-edited `page`/`pageSize` values in the URL fall back to safe values
  instead of stranding the person in an error state; the API's message is shown on load failures; route ids are
  URL-encoded (a crafted `..%2F..%2Fadmin` id could otherwise reach another API path with the token); the form
  marks required fields, moves focus to the first invalid field after a refusal, shows a message when a 400
  names nothing usable, cancels an in-flight save if the person leaves, and uses `replaceUrl` after saving and
  for debounced search; the toggle announces its result and shows the API's detail; "1 customer" plural,
  "No customers on this page" for a page past the end, logical margins for Arabic names, an accessible table
  name, `aria-current` on the Customers link. New tests cover the URL synchronisation, stale responses, paging,
  clamping, focus, trimming, cancellation and id encoding.
- Docs: spec status and Assumptions, data-model (`CustomerInput`, string query), the contract, and the task
  file names now match the code.

### Deliberately not done (recorded)

| Item | Why |
|------|-----|
| Unsaved-changes guard, "Add your first customer" hint, per-route page titles, lazy-chunk failure UI | Polish beyond the spec |
| Subscribing to `paramMap` in the detail/form components (reused component between two customer pages) | Only reachable by history navigation between two customer pages; recorded for a later UI pass |
| `FakeCustomerStore.TryUpdate` does not roll back the mutated entity | The rollback is proven where it matters, against the real store |
| Hard 1-second timing assertion could be flaky on a very slow machine | Measured 101-178 ms; kept as the SC-003 evidence |
| `security-reviewer` for the Development malformed-body fix | No auth code changed; the change only removes information from responses and was verified on a real Development run |
| Concurrency token / "someone else changed this" warning | Spec assumption: last save wins |
