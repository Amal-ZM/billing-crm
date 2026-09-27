---

description: "Task list for 012-security"
---

# Tasks: Security Hardening

**Input**: Design documents from `/specs/012-security/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/security-hardening.md, quickstart.md

**Tests**: Included, following every prior spec's own habit. Write each phase's tests first and confirm they fail (a test that does not
compile yet, or that currently fails against the unmodified code, both count as failing).

**Organization**: Grouped by user story (US1 auth on every write, US2 consistent error shapes, US3 free text can't become a script, US5
close the deferred backlog, US4 `SECURITY.md`). **US4 is built last, out of its own P2 priority order**, because it documents what US1,
US2, US3 and US5 actually did — writing it before them would mean rewriting it once their real outcomes are known. There is no
Foundational phase: each story's own new code (if any) is independent of every other story's.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3, US5, US4 (setup and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md; `SECURITY.md` and `README.md` at the repository root. All
commands run from the repository root unless a task says otherwise (`cd /c/Users/somam/source/repos/billing-crm` first in Bash: the
shell's directory drifts); run frontend commands from `frontend/` (`npx --yes npm@11 exec -- ng test --watch=false`). Run the three
backend test projects **one at a time**. Stop the API and `ng serve` before `dotnet build` or `dotnet test`; **do not start them again
unless the developer asks**. Edit code files with the Edit and Write tools (shell string replacement and long heredocs have mangled code
and quotes before).

**Rules that apply to every task**: use the `minimal-api-conventions` and `angular-ui-conventions` skills for matching edits; every new
refusal that is a genuine business rule (the decimal converter, the free-text guard) stays a 400 naming its field through the existing
`Result`/`ProblemDetails` path — no new error shape; the body-size limit (413) and the rate limiter (429) are the one documented exception
(enforced by ASP.NET Core middleware before any endpoint runs); money stays `decimal(18,2)` SAR end to end, never `float`/`double`; no
connection string, key or credential in any committed file; do not change any existing screen's behaviour, any existing response field,
or any existing test's outcome — every check this spec adds is additive; the Payments trigger and the deadlock retry follow spec 010's
own, already-approved shapes exactly, so no new abstraction is introduced by either.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the five new items in plan.md's Constitution
  Check: (1) a custom global `DecimalJsonConverter` (the first custom `JsonConverter` in the API), refusing exponent notation and more
  than 28 significant digits on every `decimal`-bound field; (2) ASP.NET Core's built-in rate-limiting middleware (the first use of that
  feature), one named fixed-window policy applied only to `POST /api/auth/login`, partitioned by client address; (3) a small hand-written
  header middleware in `Program.cs` (the first one; everything so far has been endpoint policies), setting `X-Frame-Options: DENY` on
  every response and `Cache-Control: no-store` on every `/api` response; (4) a shared `FreeTextGuard` validation helper (the first
  cross-validator shared abstraction) called from `CustomerValidator`, `ProductValidator`, `InvoiceValidator` and `PaymentValidator`,
  refusing Unicode bidi/control characters in free text; (5) a database trigger on `Payments`, `TR_Payments_Immutable`, the exact shape of
  spec 010's `TR_InvoiceHistoryEntries_Immutable` applied to a different table (needs less explanation since it repeats an already-approved
  pattern). Also confirm the bounded scope of the deferred backlog this spec closes (research.md's Known Gaps: HTTPS/HSTS, payment
  idempotency, history redaction and the stale-role window all stay open, recorded with reasons in `SECURITY.md`, not implemented here) and
  that a SQL Server deadlock on an invoice save is caught and mapped straight to the existing `invoice_changed` 409, no retry (research R1b — this
  reuses the existing catch/409 shape, so it is confirmed alongside the others but is not itself a new pattern). Record the confirmations
  in research.md under "Confirmations (T001)". Do not proceed until confirmed; if the developer changes any item, update plan.md,
  research.md, data-model.md and the contract first
- [X] T002 Baseline check before any change: stop the API and `ng serve` if running, then `dotnet test` for each of the three backend test
  projects in turn and (in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` and `ng build` pass, confirming 011's green state
  is unchanged

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None. Each story below touches its own files (a new test file, a new small production file, or an existing file no other
story also edits); nothing here blocks more than one story at once.

**Checkpoint**: proceed straight to US1.

---

## Phase 3: User Story 1 - Every write needs a real, valid session (Priority: P1)

**Goal**: prove, in one place, that every documented write route across customers, products and invoices refuses a missing, expired or
tampered token with 401 before anything changes; that an Agent is refused with 403 on the three restricted actions and an Admin succeeds;
and that no delete route exists for any of the three resources, for either role.

**Independent Test**: run the new consolidated test file alone; every assertion in it passes against the code exactly as it stands after
011 (this story is proof, not new behaviour, except for anything T004 finds and must then fix).

### Tests for User Story 1 (write first; they should already pass — anything that does not is a genuine gap, fixed in T004)

- [X] T003 [US1] Write `backend/tests/BillingCrm.Api.Tests/SecurityRegressionTests.cs`: enumerate every write route (`POST`/`PUT`, any
  method other than `GET`) under `/api/customers`, `/api/products` and `/api/invoices` (reuse the `EndpointDataSource`/`RouteEndpoint`
  enumeration pattern `InvoiceAccessTests.cs` and `ProductAccessTests.cs` already use, generalized across all three resource groups in
  one list; exclude `/api/auth/login`, which is deliberately anonymous). For every one of those routes: a request with no `Authorization`
  header is `401` and the seeded data is unchanged afterwards (assert a before/after count or a specific row is untouched, per resource);
  the same with `TestTokens.Expired()` and with `TestTokens.Tampered()` is `401` too. For an Agent token: `POST /api/products`,
  `PUT /api/products/{id}`, `PUT /api/products/{id}/active` and `POST /api/invoices/{id}/cancel` are each `403`, judged before the body is
  read (a malformed body still answers 403, not 400), and nothing changes; the same four with an Admin token succeed (given a valid body).
  Assert the consolidated route list contains no `DELETE` under any of the three resource groups

### Implementation for User Story 1

- [X] T004 [US1] Run `dotnet test backend/tests/BillingCrm.Api.Tests --filter FullyQualifiedName~SecurityRegressionTests`. **Nothing to
  fix**: all 10 tests passed on the first run against the unmodified code — every write route across customers, products and invoices
  already required a signed-in user, no delete route existed anywhere, exactly the four routes already carried the Admin-only policy, and
  the Agent/Admin/no-token/expired-token/tampered-token behaviour all matched the spec exactly (depends on T003)

**Checkpoint**: User Story 1 is proven — every write route requires a real session and the three restricted actions are Admin-only,
everywhere, in one place, not just per-resource.

---

## Phase 4: User Story 2 - Expected problems always look the same (Priority: P1)

**Goal**: one 400/404/409 example per resource, all with the same `ProblemDetails` shape; and the one genuine gap in this story — a SQL
Server deadlock on an invoice save today surfaces as an unhandled 500 — closed so it answers the same `invoice_changed` 409 any other lost
race already does.

**Independent Test**: run the two new test files; the error-shape one should already pass (proof of existing behaviour), the deadlock one
should fail against today's code (a real gap) until T007 closes it.

### Tests for User Story 2 (write first)

- [X] T005 [P] [US2] Write `backend/tests/BillingCrm.Api.Tests/ErrorShapeRegressionTests.cs`: one 400 (an invalid field: a negative
  product price, a malformed invoice due date, or similar), one 404 (an unknown id) and one 409 (a duplicate customer email, a duplicate
  product SKU, and an illegal invoice status change) example, one per resource where it applies; every answer's body has exactly
  `title`/`detail`/`errors` (400) or `title`/`detail` (404, 409) and no other property, matching every existing resource's own contract
- [X] T006 [P] [US2] Write `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreDeadlockTests.cs` (throwaway LocalDB): force a
  genuine SQL Server deadlock through two real, concurrent `EfInvoiceStore.SaveChangesAsync` calls on two Sent invoices, each cancelling
  them in the opposite order under its own explicit transaction with a short delay between its two saves (so each holds one row's lock
  across the delay while it asks for the other's — the reliable way to make the engine's own deadlock monitor pick a victim; this test is
  slower than most in this suite by design and its timing, not its outcome, is the only thing that varies run to run): one of the two
  concurrent saves succeeds (and its transaction commits both cancels); the deadlock victim's save returns `false` (never throws) —
  the same answer as every other lost race, not a 500 — and its whole transaction is rolled back, so exactly one invoice ends up Cancelled

### Implementation for User Story 2

- [X] T007 [US2] `backend/src/BillingCrm.Infrastructure/Invoices/EfInvoiceStore.cs`'s `SaveChangesAsync`: add a private static
  `IsDeadlock(Exception ex)` (true when `ex` is a `DbUpdateException` whose base exception is a `SqlException` with `Number == 1205`);
  widen the existing `catch (DbUpdateConcurrencyException)` to also catch `DbUpdateException` when `IsDeadlock` is true, returning
  `false` for it exactly as a lost concurrency race already does — no retry, no loop. No other behaviour changes: a non-deadlock
  `DbUpdateException` still propagates as before. Make T006 pass (depends on T006)
- [X] T008 [US2] Run `dotnet test` for `BillingCrm.Api.Tests` (T005) and `BillingCrm.Infrastructure.Tests` (T006, needs LocalDB); both
  pass (depends on T005, T007). Confirmed: full `BillingCrm.Api.Tests` 557/557 passed (40s); full `BillingCrm.Infrastructure.Tests`
  279/279 passed (5s), including the deadlock test at real, non-flaky timing (`-v normal` showed the earlier terse "< 1 ms" readings
  were a display artifact of the minimal-verbosity summary line, not a skip: the per-test duration is genuinely ~0.9-5s across runs)

**Checkpoint**: every expected problem, across every resource, answers the same documented shape — including a database-level conflict,
which no longer becomes an unhandled error.

---

## Phase 5: User Story 3 - Typed text can never become a script (Priority: P2)

**Goal**: every free-text field refuses a bidi/control character outright (a new rule), and it is proven, once, that nothing in the
Angular app can ever turn any text into markup or script (an existing property, now guarded by an automated check instead of a one-off
manual `grep`).

**Independent Test**: submit a bidi/control character to each of the four validators and confirm each refuses it; run the new frontend
check and confirm it currently passes and would fail if an `innerHTML`/`bypassSecurityTrust*` binding were ever added.

### Tests for User Story 3 (write first; fail until T012-T013)

- [X] T009 [P] [US3] Write `backend/tests/BillingCrm.Application.Tests/FreeTextGuardTests.cs` (pure): `FreeTextGuard.Refuses` is `true`
  for each of `U+200E`, `U+200F`, `U+202A`-`U+202E`, `U+2066`-`U+2069`, and for another C0/C1 control character; `false` for tab, line
  feed, carriage return, ordinary Arabic and English text, emoji, and a long (1,000-character) string with none of the above
- [X] T010 [P] [US3] Add one new test case each to `backend/tests/BillingCrm.Application.Tests/{CustomerValidatorTests,
  ProductValidatorTests,InvoiceValidatorTests,PaymentValidatorTests}.cs`: a name/address/notes/note containing a bidi override character
  is refused with a 400 field error on that exact field (`name`, `address`, `notes`, `note`), and the existing length-limit test for the
  same field still passes unchanged
- [X] T011 [P] [US3] Write `frontend/src/app/security-no-unsafe-html.spec.ts`: reads every `.ts` file under `frontend/src` (Node `fs`, no
  Angular `TestBed` needed) and fails if `innerHTML`, `outerHTML` or `bypassSecurityTrust` appears in any of them; confirms it currently
  passes (the repo has none today). Adjusted from the original plan: excludes `*.spec.ts` files (a legitimate use in
  `invoice-print.component.spec.ts` reads a rendered element's own `innerHTML` to assert on output, not a vulnerability) and needed
  `@types/node` added as a dev dependency plus `"node"` added to `tsconfig.spec.json`'s `types` (not `tsconfig.app.json`, so the
  production bundle never depends on Node) because Node's `fs`/`path` had no type declarations available otherwise

### Implementation for User Story 3

- [X] T012 [US3] `backend/src/BillingCrm.Application/Common/FreeTextGuard.cs`: `public static class FreeTextGuard { public static bool
  Refuses(string value) }` per data-model.md/research R7 (the named bidi and C0/C1 control-character ranges, tab/LF/CR excepted). Make
  T009 pass
- [X] T013 [US3] Call `FreeTextGuard.Refuses` from `backend/src/BillingCrm.Application/Customers/CustomerValidator.cs` (`name`,
  `address`), `Products/ProductValidator.cs` (`name`), `Invoices/InvoiceValidator.cs` (`notes`) and `Invoices/PaymentValidator.cs`
  (`note`): when it returns `true`, add `"<field> contains a character that is not allowed."` to that field's existing errors array
  (appending, never replacing the length check's own message). Make T010 pass
- [X] T014 [US3] Run `dotnet test` for `BillingCrm.Application.Tests` (depends on T009, T012, T013) and, from `frontend/`, `npx --yes
  npm@11 exec -- ng test --watch=false` (depends on T011); both pass. Confirmed: 843/843 Application.Tests passed; 814/814 Angular tests
  passed across 48 files; `ng build` (production) also confirmed clean, since the new test file's Node-only import must never reach the
  production bundle

**Checkpoint**: every free-text field in the application refuses hostile control characters at the door, and an automated check — not a
one-off manual review — keeps every screen's output-safety guarantee true going forward.

---

## Phase 6: User Story 5 - Nothing deferred is silently forgotten (Priority: P3, built before US4)

**Goal**: close the five bounded items from the deferred backlog (decimal precision, anti-framing/no-store headers, a request body size
limit, login rate limiting, database-level immutability for `Payments`) — everything research.md decided was an in-repo change with no
missing infrastructure or open design question.

**Independent Test**: each of the five new test files below proves its own item in isolation; together they are the whole of what US4's
`SECURITY.md` will report as "closed."

### Tests for User Story 5 (write first; fail until T020-T024)

- [X] T015 [P] [US5] Write `backend/tests/BillingCrm.Api.Tests/DecimalPrecisionTests.cs`: a product's `unitPrice`/`vatPercent`, an
  invoice line's `quantity`, an invoice's `discount`, and a payment's `amount` each refuse `1e5`/`1E-3` (exponent notation) and a 29+
  significant-digit number with a 400 naming that field; an ordinary number within 28 significant digits and two decimals is still
  accepted on each, unchanged from before this spec. Confirmed: also exposed two pre-existing `InvoicePaymentEndpointTests.cs` tests
  that had documented the old gap by name (`..._a_known_gap_for_spec_012`) and asserted `201 Created`; both flipped to assert `400` now
  that the converter closes the gap they were written to describe (renamed to `..._now_refused_closing_the_spec_012_gap`)
- [X] T016 [P] [US5] Write `backend/tests/BillingCrm.Api.Tests/SecurityHeadersTests.cs`: `GET /health` (anonymous) and an authenticated
  `GET /api/invoices` both carry `X-Frame-Options: DENY`; the `/api/invoices` response also carries `Cache-Control: no-store`. Dropped a
  third planned case (asserting `/health` carries no `no-store`): ASP.NET Core's own health-check middleware already sets its own
  cache-prevention headers on `/health` for unrelated reasons, so it is not a clean venue to prove the `/api`-only scoping
- [X] T017 [P] [US5] Write `backend/tests/BillingCrm.Api.Tests/RequestBodySizeTests.cs`: a request body just over the configured limit
  (1 MiB) is refused (`413`); a normal-size request body (the existing `POST /api/invoices` fixtures) is unaffected. Confirmed:
  `WebApplicationFactory`'s in-memory `TestServer` does not route through Kestrel at all, so `KestrelServerOptions.Limits.MaxRequestBodySize`
  alone is silently unenforced under test (the first run answered `400`, not `413`) — added an explicit check of the request's declared
  `Content-Length` in the header middleware (before CORS), which is real defense-in-depth (a common request rejected before its body is
  read at all, under any host) with Kestrel's own limit remaining the deep backstop against a client that lies about `Content-Length`
- [X] T018 [P] [US5] Write `backend/tests/BillingCrm.Api.Tests/LoginRateLimitTests.cs`: the 11th `POST /api/auth/login` attempt from one
  source inside the configured window is `429` with a `Retry-After` header; the 10th (or fewer) still answers normally (401 for a wrong
  password, 200 for a right one); a request that looks like it comes from a different source (a different `RemoteIpAddress`, set through
  `WebApplicationFactory`'s connection features) is not throttled by another source's attempts. Confirmed two corrections to research R5's
  assumption ("the middleware's own default[s]"): the rejection status defaults to `503`, not `429` (`options.RejectionStatusCode` must be
  set explicitly), and a fixed-window limiter's rejected lease is not turned into a `Retry-After` response header automatically either
  (added `options.OnRejected` reading the lease's own `MetadataName.RetryAfter`). `Connection.RemoteIpAddress` is otherwise always null
  under the in-memory `TestServer` (no real socket), so `ApiFactory` gained a test-only `IHttpContextFactory` decorator that sets it from
  a test-only request header (`ApiFactory.TestRemoteIpHeader`), never touching production code
- [X] T019 [P] [US5] Write `backend/tests/BillingCrm.Infrastructure.Tests/EfPaymentImmutabilityTests.cs` (throwaway LocalDB, mirroring
  `EfInvoiceHistoryImmutabilityTests.cs`'s shape from spec 010): a raw `UPDATE`/`DELETE` against `Payments` (every row, one row by id) is
  refused with error 51000 and every row is unchanged; `ExecuteUpdateAsync`/`ExecuteDeleteAsync` and a tracked entity edited then
  `SaveChangesAsync` are refused the same way; recording a payment (`INSERT`, through `Invoice.RecordPayment` and the normal save) still
  works; a statement matching no row is refused too (the trigger fires per statement, not per row, as 010's own test already proved for
  history)

### Implementation for User Story 5

- [X] T020 [US5] `backend/src/BillingCrm.Infrastructure/Serialization/DecimalJsonConverter.cs`: `sealed class DecimalJsonConverter :
  JsonConverter<decimal>` reading the raw token via `Utf8JsonReader` (never `reader.GetDecimal()`), throwing `JsonException` for a token
  containing `e`/`E` or more than 28 significant digits, otherwise parsing normally; `Write` unchanged (default numeric writing).
  Register it once in `backend/src/BillingCrm.Api/Program.cs`'s existing `ConfigureHttpJsonOptions` call, alongside
  `NumberHandling = Strict`. Make T015 pass
- [X] T021 [US5] `backend/src/BillingCrm.Api/Program.cs`: a small `app.Use` middleware, placed before `app.UseCors()`, that sets
  `X-Frame-Options: DENY` on every response and additionally sets `Cache-Control: no-store` when the request path starts with `/api`.
  Make T016 pass. The same middleware also carries T022's Content-Length check (see below)
- [X] T022 [US5] `backend/src/BillingCrm.Api/Program.cs`: `builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize
  = 1_048_576);` (1 MiB; a comment says why this number), plus (found necessary at T017) an explicit `Content-Length > 1_048_576` check
  in the header middleware answering `413` directly, since Kestrel's own limit has no effect under the in-memory test host. Make T017 pass
- [X] T023 [US5] `backend/src/BillingCrm.Api/Program.cs`: `builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(
  "login", limiterOptions => { limiterOptions.PermitLimit = 10; limiterOptions.Window = TimeSpan.FromMinutes(1); }));` partitioned by
  `context.Connection.RemoteIpAddress` (a comment explains the in-memory, single-instance assumption from research R5); `app.UseRateLimiter()`
  added to the pipeline after `app.UseCors()`. `backend/src/BillingCrm.Api/Endpoints/AuthEndpoints.cs`: add `.RequireRateLimiting("login")`
  to the `POST /api/auth/login` route only. Make T018 pass. Adjusted from the plan: used `options.AddPolicy("login", ...)` with
  `RateLimitPartition.GetFixedWindowLimiter` rather than the non-partitioned `AddFixedWindowLimiter` (the latter has one global window,
  not one per source, which is what "partitioned by `RemoteIpAddress`" requires); also added `options.RejectionStatusCode` and
  `options.OnRejected` (see T018's own findings)
- [X] T024 [US5] `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`: add
  `builder.ToTable("Payments", table => table.HasTrigger("TR_Payments_Immutable"));` (adjusting the existing `ToTable` call rather than
  adding a second one). Run `dotnet ef migrations add AddPaymentsImmutability --project backend/src/BillingCrm.Infrastructure
  --startup-project backend/src/BillingCrm.Api`; in its (likely near-empty, since no column changes) `Up`, add `migrationBuilder.Sql(...)`
  creating `[dbo].[TR_Payments_Immutable]` (`INSTEAD OF UPDATE, DELETE`, `SET NOCOUNT ON; THROW 51000, 'Payments cannot be changed or
  deleted.', 1;`), the exact shape of migration `AddInvoiceHistory`'s trigger; in `Down`, `DROP TRIGGER` **before** anything else; check
  the generated file touches no column on `Payments` or any other table; never edit an already-applied migration. Then `dotnet ef database
  update` with the same arguments. Make T019 pass. Confirmed: the generated migration's `Up`/`Down` were entirely empty (EF recorded the
  trigger annotation in the model snapshot but cannot script a trigger itself), so both bodies were hand-written, mirroring
  `AddInvoiceHistory` exactly; all 14 `EfPaymentImmutabilityTests` pass against the applied migration on the real throwaway LocalDB
  database
- [X] T025 [US5] Run `dotnet build backend/BillingCrm.sln` and `dotnet test` for each of the three backend test projects in turn; all pass
  (depends on T015-T024). Confirmed: build clean; `BillingCrm.Application.Tests` 843/843, `BillingCrm.Api.Tests` 575/575 (including the
  two flipped decimal-gap documentation tests), `BillingCrm.Infrastructure.Tests` 293/293

**Checkpoint**: the five bounded backlog items are closed and proven; only the four items research.md's Known Gaps names remain open, on
purpose, with reasons.

---

## Phase 7: User Story 4 - One document answers "is this protected?" (Priority: P2, built last)

**Goal**: `SECURITY.md` at the repository root, naming every guarantee US1, US2, US3 and US5 just proved or closed, and every item left
open, with a reason.

**Independent Test**: hand `SECURITY.md` to someone who has not read this spec and confirm they can answer "does this check X?" for
authentication, authorization, input validation, output encoding, secrets and the known gaps, using only that document.

- [X] T026 [US4] Write `SECURITY.md` at the repository root per contracts/security-hardening.md and quickstart.md section 5: **Authentication
  & Sessions** (JWT bearer, `Jwt:ExpiryMinutes`, zero clock skew, pinned algorithm); **Authorization** (a table: operation category ->
  required role, naming the three Agent-restricted actions and pointing at `SecurityRegressionTests.cs`); **Input Validation** (the
  `Result`/`ProblemDetails` 400/404/409 shape, the decimal converter, the free-text guard, each pointing at its test); **Output Encoding**
  (Angular interpolation only, no `innerHTML`/`bypassSecurityTrust*` anywhere, pointing at `security-no-unsafe-html.spec.ts`); **Secrets**
  (connection string and signing key via user-secrets/environment only, never committed, per Constitution III); **Known Gaps** — the four
  items research.md's "Known gaps" section names (HTTPS/HSTS, payment idempotency, invoice history redaction, the stale-role window), each
  with its one-line reason, plus the `TRUNCATE TABLE` trigger-bypass note (now true of `Payments` as well as `InvoiceHistoryEntries`)
- [X] T027 [US4] Add a one-line pointer to `SECURITY.md` in `README.md` (near the top, alongside the other top-level pointers) and confirm
  every item from the developer's own deferred-items memory (specs 004, 007, 008, 009, 010) has a matching line in `SECURITY.md` — closed
  or open, none silently absent (depends on T026). **Found three items missing from this spec's own scope while doing exactly this cross-check**
  (see research R11 and spec.md's Assumptions): closed a `Products.UnitPrice` database check-constraint upper bound (migration
  `AddProductUnitPriceUpperBound`, a new `EfProductStoreCreateTests.cs` theory case); documented the `StaffOnly` read policy as a
  deliberate no-op (the fallback policy and the closed Admin/Agent role set already make it inert); added a sixth Known Gap for
  `Development`'s exception page still echoing headers on a genuinely unexpected exception (a real diagnostics-vs-security trade-off,
  not decided here). Also added an Attribution note (prospective-only via `InvoiceHistoryEntries` since spec 010, per the 010 close note)

**Checkpoint**: the whole backlog this spec exists to close is now visible in one document, and nothing from it was dropped.

---

## Phase 8: Polish and Cross-Cutting Concerns

- [X] T028 [P] Update `README.md`: the `AddPaymentsImmutability` migration step, that login is now rate-limited (what a staff member sees
  if they mistype a password too many times in a row), that every response carries `X-Frame-Options` and API responses `Cache-Control:
  no-store`, that a request body has a size limit, that free text refuses bidi/control characters (with a short example of what that
  looks like to a person typing normally: nothing changes for them), and the `SECURITY.md` pointer (T027). Also documented
  `AddProductUnitPriceUpperBound` (T027's own extra scope) in the same migration-list paragraph
- [X] T029 [P] Update `CLAUDE.md` (touch only what 012 adds): the Project paragraph (spec 012 — the last spec — added the consolidated
  security regression suite, the deadlock-to-409 fix, the decimal converter, anti-framing/no-store headers, a body size limit, login rate
  limiting, `Payments` immutability, the shared `FreeTextGuard`, and `SECURITY.md`); the Domain/Application/Infrastructure/Api layout lines
  (`FreeTextGuard`, `DecimalJsonConverter`); a "Security hardening (spec 012)" bullet under Run Commands summarizing the same, with
  pointers to `SECURITY.md` for detail rather than repeating it
- [X] T030 Run the whole suite: `dotnet test` for each of the three backend projects in turn, `ng test --watch=false`, `ng build`; all
  pass; search the files changed by this spec for `Password`, `Secret`, `SigningKey`, `Server=` and confirm no hit in a committed file
  (an intentional `Server=` may appear only inside `LocalDbFixture.cs`'s existing, already-reviewed connection-string builder); confirm no
  existing response field, screen or test's outcome changed anywhere outside what this spec documents. Confirmed: 843/575/294 backend
  tests pass, 814/814 (48 files) Angular tests pass, `ng build` clean; every `Password`/`Secret`/`SigningKey`/`Server=` hit is a
  pre-existing, already-documented test-only placeholder or dev-seed value (none from this spec's own changes, no committed secret)
- [ ] T031 [P] Walk through `specs/012-security/quickstart.md` sections 3 and 4 against the automated tests and report which rows are
  covered by a test and which are manual only (the literal `curl`/`sqlcmd` sessions, and the deliberately-flaky-by-nature deadlock timing);
  add any missing automated test for a row that can be automated
- [ ] T032 Run the full quickstart validation (`specs/012-security/quickstart.md`) on the developer's machine, including every manual
  check (the request's own example: no token, `POST /api/invoices`, 401; each header; the rate limiter; the body-size limit; the
  `Payments` trigger via `sqlcmd`; reading `SECURITY.md` end to end); the developer starts the servers and confirms it is running
  (Constitution II)
- [ ] T033 Run `api-reviewer`, `angular-reviewer`, **`security-reviewer`** and `spec-auditor`; fix or explicitly record every finding in
  `specs/012-security/research.md` (a "Review disposition" section like every prior spec's). Give `api-reviewer`: that every new 400 stays
  on the `Result`/`ProblemDetails` path and only 413/429 are the documented exception; that the deadlock retry never masks a genuine
  concurrency conflict as something else; that the decimal converter reads the raw token (never `GetDecimal()` first) and applies to every
  `decimal`-bound field, not just some; that the rate limiter and the header middleware apply to exactly the routes documented, no more and
  no less; that the `Payments` trigger's `Down` order matches 010's; and tests that claim more than they prove. Give `security-reviewer`
  (the whole point of this spec): every item in `SECURITY.md`, checked against the actual code, not just the document's own words; whether
  the consolidated `SecurityRegressionTests.cs` really covers every write route (cross-check the route count against `EndpointDataSource`
  directly, not just the routes the test author remembered); the free-text guard's exact character ranges against a recognised reference
  (Unicode bidi control characters); the rate limiter's partition key (client-spoofable headers must never be used instead of the real
  connection address); the known-gaps list for anything this session should have closed but didn't; and the deferred-items memory file
  cross-checked line by line against `SECURITY.md`. Give `angular-reviewer`: that `security-no-unsafe-html.spec.ts` genuinely fails when a
  forbidden binding is (temporarily, for the check) introduced — prove the mutation, don't just trust the assertion; that no existing
  screen's behaviour changed. Give `spec-auditor` the whole spec (US1.1-US1.5, US2.1-US2.4, US3.1-US3.2, US4.1-US4.2, US5.1, edge cases,
  FR-001..FR-016, SC-001..SC-006) with the browser/manual-only rows marked Unknown until T032
- [ ] T034 Re-run `dotnet test` for each backend project, `ng test --watch=false` and `ng build` after any review fixes; all pass
- [ ] T035 Mark spec 012 done only after T032 and T033 are complete, and update `specs/012-security/spec.md` status to Done. This is the
  last spec in the roadmap (001-012): once it is verified, the whole project as specified is complete — say so plainly, and do not treat
  any further request as a new numbered spec without the developer's own new `/speckit-specify`

---

## Dependencies and Execution Order

- **Setup** (T001-T002) first. T001 blocks all code.
- **Foundational**: none — proceed straight to the stories once T002 is green.
- **US1** (T003-T004): needs Setup only.
- **US2** (T005-T008): T005 and T006 (tests, different files) in parallel; T007 (needs T006); T008 last. Needs Setup only; independent of
  US1.
- **US3** (T009-T014): T009, T010, T011 (tests, different files) in parallel; T012 (needs T009), T013 (needs T010, T012); T014 last.
  Needs Setup only; independent of US1 and US2.
- **US5** (T015-T025): T015-T019 (tests, different files) in parallel; T020 (needs T015), T021 (needs T016), T022 (needs T017), T023
  (needs T018), T024 (needs T019); T025 last. Needs Setup only; independent of US1, US2 and US3.
- **US4** (T026-T027): needs US1, US2, US3 and US5 all complete (it documents their outcome), despite being nominally P2 while US5 is P3 —
  the one deliberate priority-order exception in this spec, explained above.
- **Polish** (T028-T035): T028, T029 and T031 in parallel; T030 before T032; T032 and T033 are the done-ness gate (T032 needs the servers
  started by the developer); T034 after fixes; T035 last, and closes the whole 001-012 roadmap.

### Parallel example (US1, US2, US3 and US5 together)

```text
T003 (route/role tests)  ->  T004
T005 (error-shape tests)          T006 (deadlock test)  ->  T007  ->  T008
T009 (guard tests)  T010 (validator tests)  T011 (frontend scan test)  ->  T012  ->  T013  ->  T014
T015 (decimal)  T016 (headers)  T017 (body size)  T018 (rate limit)  T019 (Payments trigger)
   ->  T020        ->  T021        ->  T022           ->  T023          ->  T024                ->  T025
                                                                                                        \
                                                                                    T026 (SECURITY.md)  ->  T027
```

## Implementation Strategy

- **MVP**: Setup plus User Story 1 and User Story 2 (T001-T008): the two P1 stories prove the system's foundation — auth on every write,
  and a predictable shape for every expected problem, including the one genuine gap (the deadlock) closed.
- **Incremental delivery**: add User Story 3 (T009-T014, output safety), then User Story 5 (T015-T025, the closed backlog items), then
  User Story 4 (T026-T027, `SECURITY.md`, once there is something true to write about); then Polish (T028-T035), ending with the
  `security-reviewer`-led pass and the developer's own quickstart run before this — the last spec — is marked Done.
