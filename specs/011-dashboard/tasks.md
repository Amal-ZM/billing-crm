---

description: "Task list for 011-dashboard"
---

# Tasks: Dashboard

**Input**: Design documents from `/specs/011-dashboard/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/dashboard-api.md, quickstart.md

**Tests**: Included, following every prior spec's own habit. Write each phase's tests first and confirm they fail (a test that does not
compile yet counts as failing).

**Organization**: Grouped by user story (US1 see the five figures on Home, US2 click a card to reach its matching invoices). There is no
separate Foundational phase this time: US1 only *adds* a new store member (`GetSummaryAsync`) and US2 only *widens* an existing one
(`SearchAsync`) — the two touch different members of the same three store implementations, so nothing blocks either story beyond a green
baseline (Setup). No new entity, no new table, no migration.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2 (setup and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run from the repository root unless a task
says otherwise (`cd /c/Users/somam/source/repos/billing-crm` first in Bash: the shell's directory drifts); run frontend commands from
`frontend/` (`npx --yes npm@11 exec -- ng test --watch=false`). Run the three backend test projects **one at a time**. Stop the API and
`ng serve` before `dotnet build` or `dotnet test`; **do not start them again unless the developer asks**. Edit code files with the Edit and
Write tools (shell string replacement and long heredocs have mangled code and quotes before).

**Rules that apply to every task**: use the `minimal-api-conventions` and `angular-ui-conventions` skills for matching edits; **every
figure is computed fresh on each read, never cached, precomputed or stored**; a **Cancelled invoice is never counted anywhere and never
appears in any filtered view**; **Overdue is `Status = Sent AND DueDate < today`**, never a stored value, exactly as 007 already defines
it; **the settling payment of a Paid invoice is its own last-recorded payment** (`Payments.RecordedAtUtc` descending, the first one),
whatever its `PaidOn` date, and that date alone decides "this month" (never an earlier part payment's date); **"today" and "this month"**
use the same fixed Saudi UTC+03:00 date the rest of the invoice feature already uses (`InvoiceService.Today`); money is `decimal(18,2)` SAR
end to end, never `float`/`double`, and the unpaid amount is always a sum of figures `InvoiceMoney`/`Invoice.Remaining` already produce,
never recalculated independently; an unrecognized `status` value is a 400 field error naming `status`, exactly like every other typed list
filter in this codebase; `overdue`/`paidMonth` combined with the "wrong" `status` (or with none) are silently ignored, never a different
error; every endpoint declares its access explicitly (any signed-in Admin or Agent, no new policy); the invoice list gains **no new search
or filter control of its own** — the three new query values exist only so a dashboard card's link can reach the right set; no connection
string, key or credential in any committed file; do not change customer or product behaviour, and `GET /api/invoices` with none of the new
query values must answer exactly as it did before this spec.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the new items in plan.md's Constitution
  Check: (1) **`GET /api/invoices/summary`**, inside the existing `/api/invoices` group and `InvoiceService`, answering an aggregate of
  five figures with no id and no paging — the first read of this shape in the codebase (every other `GET` returns one resource, a page of
  resources, or one resource's history); (2) **three new optional query values on `GET /api/invoices`** — `status`, `overdue`, `paidMonth`
  — reusing the typed-list-filter pattern customers' and products' own filters already established, carried by a new `InvoiceSearchFilter`
  passed to a widened `IInvoiceStore.SearchAsync`; (3) **"the settling payment"** of a Paid invoice is its own last-recorded payment, and
  that decides "Paid this month" — no new stored column; (4) **no migration, no new entity**: both figures and filters are read live from
  the existing `Invoices` and `Payments` tables every time; (5) **Angular**: a new `core/dashboard/` service and model (matching every
  other `core/<domain>` service) and five cards added to the existing `HomeComponent` (not a new screen), each a plain router link
  carrying the query values from contracts/dashboard-api.md's table; `invoice-list.component.ts` reads those same values from its own
  address alongside `page`/`pageSize`, gaining no filter UI of its own. Record the confirmations in research.md under "Confirmations
  (T001)". Do not proceed until confirmed; if the developer changes any item, update plan.md, research.md, data-model.md and the contract
  first
- [X] T002 Baseline check before any change: stop the API and `ng serve` if running, then `dotnet test` for each of the three backend test
  projects in turn and (in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` and `ng build` pass, confirming 010's green state
  is unchanged

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None. US1 only adds `IInvoiceStore.GetSummaryAsync` (a brand-new member); US2 only widens the existing `SearchAsync`. They
touch different methods of the same three store implementations and can each be built, tested and shipped without the other once T002 is
green — there is nothing here that blocks both stories at once.

**Checkpoint**: proceed straight to US1 (and US2 may start in parallel on a different member of the same files if staffed).

---

## Phase 3: User Story 1 - See the invoice picture at a glance (Priority: P1) 🎯 MVP

**Goal**: Home shows five figures — Draft count, Unpaid count, Overdue count, Paid-this-month count and the unpaid amount sum — computed
live from the current invoices and payments, for any signed-in Admin or Agent.

**Independent Test**: seed a Draft, an on-time Sent, an overdue Sent, a Paid invoice settled this month and a Cancelled invoice; open Home
(or call `GET /api/invoices/summary` directly); each figure matches a hand count of those invoices. Nothing needs to be clickable yet.

### Tests for User Story 1 (write first; they fail until T006-T013)

- [X] T003 [P] [US1] Write the service tests first in `backend/tests/BillingCrm.Application.Tests/InvoiceServiceSummaryTests.cs` (fakes,
  `FixedTimeProvider`): seed a Draft, an on-time Sent, an overdue Sent, a Cancelled invoice, a Paid invoice whose only (settling) payment
  is recorded this month, and a Paid invoice with an earlier part payment last month followed by its settling payment this month;
  `InvoiceService.GetSummaryAsync` returns `draftCount` 1, `unpaidCount` 2 (both Sent invoices, on-time and overdue), `overdueCount` 1,
  `paidThisMonthCount` 2 (both Paid invoices count by their settling payment's own date, not an earlier part payment's), and
  `unpaidAmount` equal to the two Sent invoices' `Invoice.Remaining` added together; an empty store gives every count 0 and `unpaidAmount`
  `0m`; the Cancelled invoice affects nothing; reading the summary calls no save and adds nothing to the store's pending history
- [X] T004 [P] [US1] Write the real-SQL tests first in `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreSummaryTests.cs`
  (throwaway LocalDB): the same figures against the real `EfInvoiceStore.GetSummaryAsync`, plus a calendar-month boundary (a settling
  payment dated the last day of a Saudi month counts for that month; one dated the first day of the next month does not) and a Paid
  invoice whose part payment and settling payment are two calendar months apart
- [X] T005 [P] [US1] Write the API tests first in `backend/tests/BillingCrm.Api.Tests/InvoiceSummaryEndpointTests.cs`: `GET
  /api/invoices/summary` is `200` with exactly `draftCount`, `unpaidCount`, `overdueCount`, `paidThisMonthCount`, `unpaidAmount` (no other
  property, no id, no paging) for an Admin token and for an Agent token; `401` with no token; all-zero figures for an empty store; update
  `backend/tests/BillingCrm.Api.Tests/InvoiceAccessTests.cs`'s pinned route list to add exactly one new route, `"GET
  /api/invoices/summary"` (its ordinal position is **before** `"GET /api/invoices/{id}"`: `'s'` sorts before `'{'`), and assert `GET
  /api/invoices/summary` still needs a signed-in user like every other invoice route

### Implementation for User Story 1

- [X] T006 [US1] `backend/src/BillingCrm.Application/Invoices/InvoiceModels.cs`: add `DashboardSummaryDto(int DraftCount, int
  UnpaidCount, int OverdueCount, int PaidThisMonthCount, decimal UnpaidAmount)`. `backend/src/BillingCrm.Application/Invoices/IInvoiceStore.cs`:
  add `Task<DashboardSummaryDto> GetSummaryAsync(DateOnly today, CancellationToken)` with a summary explaining it is read-only, has no
  paging and is computed fresh on every call. `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs`: add
  `GetSummaryAsync(CancellationToken)` returning `Result.Success(await invoices.GetSummaryAsync(Today(time), cancellationToken))` — no
  validation, always succeeds, any signed-in user. `backend/tests/BillingCrm.Application.Tests/Fakes/FakeInvoiceStore.cs` and
  `backend/tests/BillingCrm.Api.Tests/InMemoryInvoiceStore.cs`: implement `GetSummaryAsync` in memory with the same semantics (Sent count
  for unpaid; `DueDate < today` for overdue; each Paid invoice's own last-recorded payment's `PaidOn` month for paid-this-month; the sum of
  `Invoice.Remaining` for the amount). Make T003 pass
- [X] T007 [US1] `backend/src/BillingCrm.Infrastructure/Invoices/EfInvoiceStore.cs`: implement `GetSummaryAsync` per research R2 as five
  independent queries — a Draft count, a Sent count, a Sent-and-overdue count, a Paid-and-this-month count (each Paid invoice's own
  last-recorded payment, by `RecordedAtUtc`, whose `PaidOn` falls in `today`'s Saudi calendar month), and the SAR sum, over Sent invoices,
  of `FrozenGrandTotal` minus the sum of that invoice's own payments. Make T004 pass
- [X] T008 [US1] `backend/src/BillingCrm.Api/Endpoints/InvoiceEndpoints.cs`: `group.MapGet("/summary", GetSummaryAsync)`, registered
  before `/{id}` for readability (a comment notes that ASP.NET Core's routing already prefers a literal segment over a route parameter
  regardless of registration order, so `/summary` can never be read as an id); no extra policy, so any signed-in Admin or Agent reads it
  like the rest of the group. Explain the endpoint in 5 MVC lines per the `minimal-api-conventions` skill. Update the route-list assertion
  in `InvoiceAccessTests.cs`. Make T005 pass
- [ ] T009 [US1] Run `dotnet build backend/BillingCrm.sln` and `dotnet test` for each of the three backend test projects in turn; all pass
  (depends on T003-T008)
- [X] T010 [P] [US1] Write the Angular service test first in `frontend/src/app/core/dashboard/dashboard.service.spec.ts`: `summary()` is
  a `GET` to `/api/invoices/summary` with no body and no query string, returning the response exactly as received
- [X] T011 [P] [US1] Write the Home tests first in `frontend/src/app/features/home/home.component.spec.ts` (additions): a stub
  `DashboardService` with only `summary` (so any other call would throw); each card shows the returned figure
  (`data-testid="dashboard-draft"`, `dashboard-unpaid`, `dashboard-overdue`, `dashboard-paid-month` as plain counts,
  `dashboard-unpaid-amount` as `SAR 1,234.50` via the existing money-formatting convention); while the summary is pending the cards show a
  quiet loading state that does not block or hide the existing API-status card; on a failure the cards show a quiet unavailable message
  (mirroring the health card's own graceful wording, e.g. "Dashboard unavailable.") with **no** error banner and **no** Retry button (Home
  degrades quietly, it is not a form); `summary` is called exactly once per component creation
- [X] T012 [US1] `frontend/src/app/core/dashboard/dashboard.models.ts`: `DashboardSummary` (camelCase mirror of `DashboardSummaryDto`).
  `frontend/src/app/core/dashboard/dashboard.service.ts`: `summary(): Observable<DashboardSummary>` (`GET`, no params). Make T010 pass
- [X] T013 [US1] `frontend/src/app/features/home/home.component.ts`: inject `DashboardService`; a second state signal (`loading | loaded
  | unavailable`), independent of the existing health-card state, populated in `ngOnInit`; five small stat elements below the existing
  card showing the five figures with the `data-testid`s from T011 (money via `DecimalPipe` `'1.2-2'` with an `SAR` prefix, matching the
  rest of the app; no arithmetic in the template). No link yet (added in US2). Make T011 pass
- [X] T014 [US1] Run `npx --yes npm@11 exec -- ng test --watch=false` and `ng build` (from `frontend/`); both pass (depends on T010-T013)

**Checkpoint**: User Story 1 is fully functional and independently testable — Home shows five correct, live figures for any signed-in
user, backed by a tested endpoint. Nothing is clickable yet; that is User Story 2.

---

## Phase 4: User Story 2 - Jump straight to the matching invoices (Priority: P2)

**Goal**: each of the five cards links to the invoice list already narrowed to exactly its own set.

**Independent Test**: with known invoices in each bucket, call `GET /api/invoices?status=...` (each of the four combinations in
contracts/dashboard-api.md) and confirm the response contains exactly that bucket and nothing else; then click each card in the browser
and confirm the same.

### Tests for User Story 2 (write first; they fail until T020-T025)

- [X] T015 [P] [US2] Write the service tests first in `backend/tests/BillingCrm.Application.Tests/InvoiceServiceSearchFilterTests.cs`
  (fakes, alongside the existing `InvoiceServiceSearchTests.cs`'s style): `status=Draft` returns only Draft invoices; `status=Sent`
  returns every Sent invoice, on-time and overdue alike; `status=Sent, overdue=true` returns only the overdue ones; `status=Paid,
  paidMonth=true` returns only invoices whose settling payment falls in the current month; an unrecognized `status` is a 400 field error
  naming `status`; `overdue`/`paidMonth` paired with a non-matching (or absent) `status` changes nothing, still `200`; paging and
  newest-first ordering are preserved within a filtered set; no filter values at all reproduces exactly today's unfiltered behaviour
- [X] T016 [P] [US2] Write the real-SQL tests first in `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreSearchFilterTests.cs`
  (throwaway LocalDB): the same four combinations against real SQL Server, plus the calendar-month boundary for `paidMonth`, and paging
  (page 2 of a filtered set contains no invoice from page 1 and none outside the filter)
- [X] T017 [P] [US2] Write the API tests first in `backend/tests/BillingCrm.Api.Tests/InvoiceSearchFilterEndpointTests.cs`: `GET
  /api/invoices?status=Draft` (and the other three combinations from the contract) returns exactly the matching invoices in the existing
  `PagedResult` shape; `?status=not-a-status` is `400` naming `status`; `?overdue=true` with no `status` (or the wrong one) changes
  nothing; combined with `page`/`pageSize`, filtering and paging both apply; `GET /api/invoices` with none of the three new values is
  identical to its response before this spec (a regression check)
- [X] T018 [P] [US2] Write the Angular list tests first: `frontend/src/app/core/invoices/invoice.service.spec.ts` (additions): `list()`
  adds `status`/`overdue`/`paidMonth` to the query string only when given, left out entirely otherwise, mirroring `page`/`pageSize`.
  `frontend/src/app/features/invoices/invoice-list.component.spec.ts` (additions): a `status`/`overdue`/`paidMonth` present in the address
  is read into the request sent to `InvoiceService.list`; it survives a page change (`onPage`) so paging never silently drops the active
  filter; with none present the list behaves exactly as it did before this spec (a regression check)
- [X] T019 [P] [US2] Write the Home link tests first in `frontend/src/app/features/home/home.component.spec.ts` (additions): each of the
  five `data-testid`s from T011 is (or is wrapped by) a link to `/invoices` whose `queryParams` exactly match contracts/dashboard-api.md's
  table (Draft → `{status: 'Draft'}`; Unpaid **and** the unpaid-amount figure → `{status: 'Sent'}`; Overdue → `{status: 'Sent', overdue:
  'true'}`; Paid this month → `{status: 'Paid', paidMonth: 'true'}`), opening in the same tab (no `target`)

### Implementation for User Story 2

- [X] T020 [US2] `backend/src/BillingCrm.Application/Invoices/InvoiceModels.cs`: extend `InvoiceQuery` with `Status`, `Overdue`,
  `PaidMonth` (all `string?`, read as text like `Page`/`PageSize` already are); add `InvoiceSearchFilter(InvoiceStatus? Status, bool
  Overdue, bool PaidThisMonth)` for the store. `backend/src/BillingCrm.Application/Invoices/IInvoiceStore.cs`: widen `SearchAsync` to
  `SearchAsync(InvoiceSearchFilter filter, DateOnly today, int page, int pageSize, CancellationToken)`.
  `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs`'s `SearchAsync`: parse `Status` into `InvoiceStatus?` (an unrecognized,
  non-empty value is a 400 field error on `status`, using the same errors-dictionary pattern `ProductService.TryParseStatus` already
  uses), parse `Overdue`/`PaidMonth` so that anything other than exactly `"true"` behaves as absent (never an error), and pass
  `Today(time)` down with the resulting `InvoiceSearchFilter`. Update `backend/tests/BillingCrm.Application.Tests/Fakes/FakeInvoiceStore.cs`,
  `backend/tests/BillingCrm.Api.Tests/InMemoryInvoiceStore.cs`, and the existing call sites in
  `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStorePerformanceTests.cs` and `EfInvoiceStoreSearchTests.cs` for the widened
  signature (an empty filter must reproduce exactly today's behaviour). Make T015 pass
- [X] T021 [US2] `backend/src/BillingCrm.Infrastructure/Invoices/EfInvoiceStore.cs`: apply the filter inside `SearchAsync`'s existing
  query, before its count-then-page steps, so both the total count and the page reflect the narrowed set: `Status` equality when given;
  `DueDate < today` added only when `Overdue` is set; each Paid invoice's own last-recorded payment's `PaidOn` within `today`'s Saudi
  calendar month, added only when `PaidThisMonth` is set. Make T016 pass
- [X] T022 [US2] `backend/src/BillingCrm.Api/Endpoints/InvoiceEndpoints.cs`'s `SearchAsync` handler: add `string? status, string? overdue,
  string? paidMonth` parameters, passed into `InvoiceQuery` alongside `page`/`pageSize`. Make T017 pass
- [X] T023 [US2] Run `dotnet build backend/BillingCrm.sln` and `dotnet test` for each of the three backend test projects in turn; all pass
  (depends on T015-T022)
- [X] T024 [P] [US2] `frontend/src/app/core/invoices/invoice.models.ts`: `InvoiceListQuery` gains `status?: InvoiceStatus`, `overdue?:
  boolean`, `paidMonth?: boolean`. `frontend/src/app/core/invoices/invoice.service.ts`'s `list()`: add them to the `HttpParams` only when
  present, exactly like `page`/`pageSize`. `frontend/src/app/features/invoices/invoice-list.component.ts`'s `queryFrom(params)`: read
  `status`/`overdue`/`paidMonth` from the address alongside `page`/`pageSize`, and keep them in the address across `onPage` (a page change
  must never drop the active filter). No new filter control is added to the screen. Make the `invoice.service.spec.ts` and
  `invoice-list.component.spec.ts` assertions from T018 pass
- [X] T025 [US2] `frontend/src/app/features/home/home.component.ts`: wrap each of the five stat elements in an `[routerLink]="['/invoices']"`
  with `[queryParams]` from the table in contracts/dashboard-api.md (as in T019). Make the `home.component.spec.ts` link assertions from
  T019 pass
- [X] T026 [US2] Run `npx --yes npm@11 exec -- ng test --watch=false` and `ng build` (from `frontend/`); both pass (depends on T018,
  T024, T025)

**Checkpoint**: both user stories work together — Home shows the five figures and every card opens the invoice list narrowed to exactly
that card's own set.

---

## Phase 5: Polish and Cross-Cutting Concerns

- [X] T027 [P] Update `README.md`: the five dashboard cards on Home (what each counts or sums, and what each links to), that the figures
  are computed live on every load (never cached), that there is **no migration** for this spec, and a pointer to
  `specs/011-dashboard/quickstart.md`
- [X] T028 [P] Update `CLAUDE.md` (touch only what 011 adds): the Project paragraph (Spec 011 added the Home dashboard cards and the
  invoice list's `status`/`overdue`/`paidMonth` filters), the frontend layout line (`core/dashboard`, `home.component` gains cards), and a
  "Dashboard (spec 011)" bullet under Run Commands: `GET /api/invoices/summary` (any signed-in user, the five figures, no query string,
  no caching), the three new `GET /api/invoices` filter values and exactly which status each depends on, how "this month" and "the
  settling payment" are decided, no migration
- [X] T029 Run the whole suite: `dotnet test` for each of the three backend projects in turn, `ng test --watch=false`, `ng build`; all
  pass; search the files changed by this spec for `Password`, `Secret`, `SigningKey`, `Server=`, `innerHTML`, `bypassSecurityTrust` and
  confirm no hit in a committed file; confirm no other existing behaviour changed (`GET /api/invoices` with no `status`/`overdue`/`paidMonth`
  returns exactly what it did before this spec)
- [X] T030 [P] Walk through `specs/011-dashboard/quickstart.md` sections 2 and 3 against the automated tests and report which rows are
  covered by a test and which are manual only (the browser card layout and the visual click-through); add any missing automated test for
  a row that can be automated
- [X] T031 Run the full quickstart validation (`specs/011-dashboard/quickstart.md`) on the developer's machine, including every browser
  check (the request's own example: one overdue and one paid-this-month invoice, cards read 1 and 1, the unpaid amount excludes the paid
  invoice; every card's click-through; both roles; an empty database showing five zeros and SAR 0.00); the developer starts the servers
  and confirms it is running (Constitution II)
- [X] T032 Run `api-reviewer`, `angular-reviewer` and `spec-auditor`; fix or explicitly record every finding in
  `specs/011-dashboard/research.md` (a "Review disposition" section like 010's). Give `api-reviewer`: that every figure and every filtered
  read is computed fresh, never cached; that a Cancelled invoice is excluded from every figure and every filtered view; that "the settling
  payment" is genuinely always the last-recorded payment of a Paid invoice (never a part payment, whatever its own date); that `GET
  /api/invoices` with none of the new values returns exactly what it always has; that an unrecognized `status` is a 400 naming its own
  field and `overdue`/`paidMonth` never produce a different error; that `/summary`'s authorization matches the rest of the group and
  cannot be shadowed by `/{id}`; and tests that claim more than they prove. Give `angular-reviewer`: no recalculation of the money figure
  on the client; the cards' loading/unavailable states degrade quietly, never blocking or hiding the existing health card; each card's
  `queryParams` exactly match the contract's table; the invoice list keeps an incoming filter across a page change; accessibility of the
  new cards (readable text, not colour-only, sensible reading order); and weak or overclaiming tests. Give `spec-auditor` the whole spec
  (US1.1-US1.4, US2.1-US2.4, edge cases, FR-001..FR-013, SC-001..SC-004) with the browser-only rows marked Unknown until T031
- [X] T033 Re-run `dotnet test` for each backend project, `ng test --watch=false` and `ng build` after any review fixes; all pass
- [X] T034 Mark spec 011 done only after T031 and T032 are complete, and update `specs/011-dashboard/spec.md` status to Done; do not start
  spec 012 before then

---

## Dependencies and Execution Order

- **Setup** (T001-T002) first. T001 blocks all code.
- **Foundational**: none — proceed straight to the stories once T002 is green.
- **US1** (T003-T014): T003-T005 (tests, different projects) in parallel; T006 (needs T003), T007 (needs T004, and T006's `DashboardSummaryDto`),
  T008 (needs T005, T006); T009 last for the backend half. T010-T011 (Angular tests) in parallel; T012 (needs T010), T013 (needs T011,
  T012); T014 last. Needs Setup only.
- **US2** (T015-T026): T015-T019 (tests, different files) in parallel; T020 (needs T015), T021 (needs T016, T020), T022 (needs T017,
  T020); T023 last for the backend half. T024 (needs T018, T020) and T025 (needs T019, T013) may run in parallel; T026 last. Needs Setup
  only; independent of US1 except that its own tests and its Home-card links build on US1's cards existing.
- **Polish** (T027-T034): T027, T028 and T030 in parallel; T029 before T031; T031 and T032 are the done-ness gate (T031 needs the servers
  started by the developer); T033 after fixes; T034 last.

### Parallel example (US1 and US2)

```text
T003 service tests    T004 real-SQL tests    T005 API tests          T010 Angular service test   T011 Home tests
        \                    |                    |                          \                        |
              T006 (DTO, store member, service, fakes)  ->  T007 (EF store)  ->  T008 (endpoint)  ->  T009
                                                                                                          \
T015 service tests  T016 real-SQL tests  T017 API tests  T018 Angular list tests  T019 Home link tests    T012 -> T013 -> T014
        \                 |                  |                    |                     |
              T020 (query+filter, store member, service, fakes)  ->  T021 (EF store)  ->  T022 (endpoint)  ->  T023
                                                                                                                   \
                                                                                          T024 (list) + T025 (home links)  ->  T026
```

## Implementation Strategy

- **MVP**: Setup plus User Story 1 (T001-T014): Home shows the five live figures for any signed-in user — the request's own worked
  example ("cards show 1 and 1 and unpaid excludes paid") is already fully checkable at this point, just not yet by clicking.
- **Incremental delivery**: add User Story 2 (T015-T026) once US1 is verified, so every card also opens the matching invoice list; then
  Polish (T027-T034), ending with the reviewer pass and the developer's own quickstart run before the spec is marked Done.
