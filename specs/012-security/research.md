# Research: Security Hardening

Every decision below was made from the spec, the constitution, the code as it stands after 011, and the developer's own deferred-items
list (recorded across specs 004, 007, 008, 009 and 010). No `NEEDS CLARIFICATION` remains (the spec itself needed none: the one high-impact
open question, how much of the backlog to close, had a reasoned default recorded under Assumptions). The five new patterns are decided
here and confirmed with the developer at task T001, the same gate every spec since 006 has used for its own new pattern.

## R1. What already exists vs. what is new

**Decision**: User Story 1 and most of User Story 2's guarantees (auth on every write, an Agent refused on the three named actions, 400 and
404 shapes) are **already true** in the code today — every write endpoint since 002 requires a signed-in user by the fallback policy,
products' create/edit/activate and invoices' cancel already carry `AuthorizationPolicies.AdminOnly`, and every service since 003 already
reports validation as 400 and not-found as 404 through `Result`/`ProblemDetails`. This spec's job for those two stories is mostly not to
build new behaviour but to **prove it holds everywhere at once**, with one consolidated test file that walks every documented write route
by name, rather than trusting that each resource's own, separately-written tests never had a gap. **One part of FR-007 is a genuine, small
gap, not just a test**: `EfInvoiceStore.SaveChangesAsync` today catches only `DbUpdateConcurrencyException`; a SQL Server deadlock victim
(a `DbUpdateException` whose `SqlException.Number` is `1205`, a different exception type) is not caught at all and would surface as an
unhandled 500 — exactly the 007 deferred item, still open. R1b below closes it. What else genuinely does not exist yet is User Story 3's
total, explicit "bidi/control characters are refused" rule, User Story 4's `SECURITY.md`, and the five items in R2-R6 below.

## R1b. A deadlock is mapped straight to the same 409 as any other lost race — no retry

**Decision**: `EfInvoiceStore.SaveChangesAsync`'s existing `catch (DbUpdateConcurrencyException)` is widened to also catch a
`DbUpdateException` whose base exception is a `SqlException` with `Number == 1205` (a SQL Server deadlock), returning `false` exactly as
a lost optimistic-concurrency race already does, so the caller still sees the existing `invoice_changed` 409 — no new error code, no
retry loop, just widening the existing catch clause to one more, already-transient, exception shape.

**Rationale**: FR-007, closing 007's own deferred item precisely ("only `DbUpdateConcurrencyException` is turned into a 409... a SQL
deadlock victim would be a 500; consider a retry **or a mapped error**" — the note's own second option). A deadlock victim is exactly the
same kind of "someone else's change interfered" outcome from the caller's point of view, so mapping it to the same 409 is the honest,
minimal fix, and it **reuses** the try/catch/409 shape `SaveChangesAsync` already has, so it needs no new Constitution V confirmation.

**Alternatives considered**: retrying the save once before mapping to 409 (the first design considered here) — rejected once the
engineering cost of *proving* a retry genuinely succeeds became clear: reliably forcing a genuine SQL Server deadlock in a test needs an
externally-held transaction spanning two of the store's own save calls (an implicit, single-call transaction resolves too fast for two
independent callers to ever really contend), and once that external transaction is the one killed by the deadlock, retrying the *same*
call on the *same*, now-aborted transaction cannot work — only a fresh call, in a fresh transaction, can, which is exactly the deferred
note's other listed option and is what a straight map (no retry) already gives the caller by simply trying its own request again. A
generic "retry any `DbUpdateException`" was also rejected (too broad: a constraint violation or a genuine data problem should not be
retried, only a transient scheduling artifact like a deadlock); a new error code for a deadlock specifically (the caller cannot do
anything different with it than with any other lost race, so a new code would be a distinction without a difference).

**Rationale**: FR-001 to FR-009, SC-001 to SC-004. Re-implementing already-correct behaviour would be wasted, risky churn; a single new
route-by-route regression test is what actually closes the risk the spec names ("just the ones a person happens to test").

**Alternatives considered**: rewriting the authorization checks as a new, unified mechanism (the existing per-resource policies already
work and are already tested individually; replacing them would be exactly the kind of speculative new abstraction Constitution V warns
against); skipping the consolidated test and relying on the existing per-resource tests alone (this is exactly the gap User Story 1 and
5 exist to close: a consolidated, named list makes an accidental future gap visible immediately, where scattered tests might not).

## R2. The decimal JSON converter

**Decision**: `DecimalJsonConverter : JsonConverter<decimal>` (`backend/src/BillingCrm.Infrastructure/Serialization/`, registered in
`Program.cs`'s `ConfigureHttpJsonOptions` alongside the existing `NumberHandling = Strict`). It reads the raw token via `Utf8JsonReader`
(not `reader.GetDecimal()`, which would silently round): refuses (throws `JsonException`, which the existing minimal-API pipeline already
turns into the same clean 400 `ThrowOnBadRequest = false` already relies on) a token containing `e`/`E` (exponent notation) or more than 28
significant digits, before ever constructing a `decimal`. Applies to every `decimal`-bound field already in the API: product price and
VAT, invoice quantity and discount, payment amount — one converter, no per-field code.

**Rationale**: FR-010. Closes the item from 004/008's deferred list precisely: today `1e-30` is accepted and stored as `0.00`, and a
30+ digit price is silently shortened before the validator ever sees it, both of which are silent data loss on a financial field
(Constitution IV) rather than a refusal.

**Alternatives considered**: validating precision in each `*Validator` after binding (too late: `decimal.Parse` has already silently
rounded or the framework has already accepted exponent notation as a valid number); a third-party JSON library (no new dependency is
needed for a single converter).

## R3. Anti-framing and no-store headers

**Decision**: a small `app.Use` middleware in `Program.cs`, placed early in the pipeline (before `UseCors`), setting `X-Frame-Options:
DENY` on every response, and `Cache-Control: no-store` on every response under `/api` (anonymous routes included, since `no-store` is
harmless there too and a blanket rule is simpler and cannot develop a gap route by route).

**Rationale**: FR-011. Closes the 009 deferred item: nothing on this site should ever be frame-able by another origin, and an
authenticated screen (an invoice, its notes) must never be restored from a shared machine's back/forward cache.

**Alternatives considered**: a `Content-Security-Policy: frame-ancestors 'none'` instead of `X-Frame-Options` (broader browser support for
`X-Frame-Options` still matters and the two are not mutually exclusive; both are set); doing this only on invoice routes (a blanket rule
is simpler and matches the spec's own "every API response" wording).

## R4. Request body size limit

**Decision**: `builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576)` (1 MiB) in `Program.cs`, **plus**
an explicit check of the request's declared `Content-Length` against the same 1 MiB figure in the header middleware (before CORS),
answering `413` directly when it is exceeded. **Correction found at implementation (T017)**: the Kestrel option alone has no effect
under `WebApplicationFactory`'s in-memory `TestServer` (it never routes through Kestrel), so relying on it alone left the limit silently
untested and effectively unenforced in that host. The explicit `Content-Length` check is real defense-in-depth regardless of host (a
declared-oversized request is refused before its body is read at all); Kestrel's own limit remains the deeper backstop for a client that
lies about `Content-Length` (chunked transfer, or an inaccurate header), which only a real socket connection can catch.

**Rationale**: FR-012. Closes the 008 deferred item (a note field's own 1,000/280-character rule only runs after the whole body has
already been read and bound; today a client can still send tens of megabytes to get there). 1 MiB is far larger than any legitimate
request this API accepts (the largest today is an invoice with 100 lines) and costs nothing to raise later if a real need appears.

**Alternatives considered**: a smaller limit tuned per endpoint (one blanket limit is simpler and every current request body is small);
enforcing it in application code after reading the body (defeats the purpose: the point is refusing before the whole body is read).

## R5. Login rate limiting

**Decision**: ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` middleware (`builder.Services.AddRateLimiter(...)`,
`app.UseRateLimiter()`), one named policy (`"login"`) built with `options.AddPolicy` and `RateLimitPartition.GetFixedWindowLimiter`
(partitioned by `context.Connection.RemoteIpAddress`, 10 attempts per minute), applied only to `POST /api/auth/login` via
`.RequireRateLimiting("login")`. A rejected request answers `429 Too Many Requests` with a `Retry-After` header.
**Two corrections found at implementation (T018)**, both against this decision's original assumption that the middleware provides these
by default: the rejection status code defaults to `503 Service Unavailable`, not `429` (`options.RejectionStatusCode =
StatusCodes.Status429TooManyRequests` must be set explicitly); and a rejected fixed-window lease is not turned into a `Retry-After`
response header automatically either (`options.OnRejected` reads the lease's own `MetadataName.RetryAfter` and sets the header itself).
Also, `AddFixedWindowLimiter` (a single, unpartitioned named limiter) was replaced with `AddPolicy` + `RateLimitPartition.GetFixedWindowLimiter`,
since only the latter actually partitions by source; the former would share one global window across every client.

**Rationale**: FR-013. Closes the item deferred twice (002, then again at 004's close): unlimited login attempts against one account or
from one source were never throttled. The built-in in-memory limiter needs no new package and no external store, appropriate for this
single-instance application (an assumption already recorded in the spec).

**Alternatives considered**: a custom attempt-counter in the database (a new table and new writes on every login attempt, for something
the framework already does in memory); rate-limiting every endpoint (out of scope: the deferred item was specifically about login, and
every other endpoint already requires a valid session, which the framework itself makes expensive to brute-force).

## R6. Database-level immutability for `Payments`

**Decision**: the exact shape of spec 010's `TR_InvoiceHistoryEntries_Immutable`, applied to `Payments`: migration `AddPaymentsImmutability`
creates `TR_Payments_Immutable`, an `INSTEAD OF UPDATE, DELETE` trigger that raises `THROW 51000, 'Payments cannot be changed or deleted.',
1` and changes nothing; `PaymentConfiguration.cs` gains `.ToTable(t => t.HasTrigger("TR_Payments_Immutable"))`, so EF's own inserts are
unaffected. `Down` drops the trigger before the table, exactly as 010's migration does.

**Rationale**: FR-014. Payments have been described as "permanent" in every spec since 008, but nothing before this enforced it below the
application layer; this closes that gap the same, already-approved way 010 closed it for history, so it needs less explanation than a
new mechanism would.

**Alternatives considered**: revoking `UPDATE`/`DELETE` grants on the application's own database login instead of a trigger (this
application's local development login is a Windows/LocalDB account with owner-level rights by default; a trigger is portable and doesn't
depend on how a given machine's LocalDB permissions happen to be set up, matching 010's own reasoning for choosing a trigger over
permissions).

## R7. Free-text control-character guard

**Decision**: `FreeTextGuard.Refuses(string value)` (`backend/src/BillingCrm.Application/Common/`, pure, no I/O): true when the text
contains a Unicode bidirectional control character (`U+200E`, `U+200F`, `U+202A`-`U+202E`, `U+2066`-`U+2069`) or another C0/C1 control
character other than tab, line feed or carriage return. Called from the four places free text is already validated for length —
`CustomerValidator` (name, address), `ProductValidator` (name), `InvoiceValidator` (notes), `PaymentValidator` (note) — adding one more
field-error entry (`"contains a character that is not allowed"`) alongside the existing length rule, never replacing it.

**Rationale**: FR-009, User Story 3's second scenario. Closes the 008/009/010 bidi/control-character item: refusing at the point text is
already validated is simpler than a UI-wide `<bdi>` rollout (the alternative 008/009/010 each floated) and needs no change to any screen.

**Alternatives considered**: isolating the direction visually with `<bdi>` everywhere the text is later shown (a per-screen change,
repeated across customers, products, invoices, payments and history, for a problem refusing at the door solves once); refusing only in
the fields already shown on the printable invoice (009's own worry) rather than everywhere (the spec's own wording, and User Story 3, ask
for every free-text field, not just the ones that reach paper).

## R8. Output safety: nothing to change, one thing to prove

**Decision**: no Angular code changes. Every screen already shows free text through plain interpolation (`{{ }}`) or an Angular pipe,
never `[innerHTML]` or `bypassSecurityTrust*` — confirmed for every `.ts` file in `frontend/src` (a repo-wide grep, not a sample). This
spec adds one automated check that keeps it true: a script or test that fails if `innerHTML`, `outerHTML` or `bypassSecurityTrust` ever
appears in `frontend/src`, run as part of the consolidated suite (T0xx), so a future spec cannot introduce one unnoticed.

**Rationale**: FR-008, SC-004. Angular's default template binding HTML-escapes automatically; the risk this spec closes is not "add
escaping" (it already exists everywhere) but "prove it stays that way," the same spirit as User Story 1's consolidated test.

**Alternatives considered**: a new Angular pipe or wrapper component for "safe text" (there is nothing for it to do differently from plain
interpolation, which already exists everywhere); a runtime/DOM-inspection test per screen (a static, repo-wide check is simpler and
catches the one thing that would actually break this guarantee — a new `innerHTML` binding — regardless of which screen it appears on).

## R9. `SECURITY.md`

**Decision**: one new file at the repository root with six short sections: Authentication & Sessions; Authorization (a table: operation
category -> required role); Input Validation; Output Encoding; Secrets; Known Gaps (the three items this spec leaves open, each with its
one-line reason, plus the stale-role window). Each closed item names the file/test that enforces it; nothing is prose-only where a pointer
is possible.

**Rationale**: FR-015, FR-016, User Story 4 and 5, SC-005, SC-006. A checklist a non-specialist can read in a few minutes, not a full
threat model (the spec's own Assumption).

**Alternatives considered**: a security section added to `README.md` instead of a new file (the spec asks for a short, dedicated,
easy-to-find document; folding it into the already-long README would bury it); a wiki page or issue tracker entry (not part of the
repository, so it would drift from the code it describes).

## R10. Test approach

- **Application**: `FreeTextGuardTests` (pure: the bidi/control characters it refuses, the ordinary Arabic/English/emoji text it accepts);
  one new test per validator (`CustomerValidatorTests`, `ProductValidatorTests`, `InvoiceValidatorTests`, `PaymentValidatorTests`) proving
  the new field error appears and the existing length rule is unaffected.
- **API**: `SecurityRegressionTests.cs` — every documented write route (customers, products, invoices, send/cancel/payment) with no
  token is 401 and changes nothing; the same routes with an expired or a tampered token are 401; an Agent on the three restricted actions
  is 403 before the body is read; one representative 400/404/409 example per resource. `DecimalPrecisionTests.cs` — exponent notation and
  a 29+ digit number refused on every `decimal`-bound field, a legitimate 28-digit-or-fewer number accepted. Header tests — `X-Frame-Options`
  and `Cache-Control: no-store` present on a sample of routes. `LoginRateLimitTests.cs` — the eleventh attempt in a window is 429 with
  `Retry-After`; a successful login is unaffected; a different source is not throttled by another's attempts. A body-size test — a request
  above the configured limit is refused (413), a request comfortably under it is not.
- **Infrastructure (real SQL)**: the `Payments` trigger — the same shape of tests 010 already proved for history (raw `UPDATE`/`DELETE`,
  `ExecuteUpdate`/`ExecuteDelete`, a tracked entity edit, ten simultaneous statements matching no row), confirming `INSERT` still works and
  `Down` order.
- **Angular**: the repo-wide `innerHTML`/`bypassSecurityTrust` check (R8); no other Angular test is needed since no Angular code changes.

## Confirmations (task T001) — 2026-09-27

The developer confirmed all four items from plan.md's Constitution Check, as recommended:

1. `FreeTextGuard` (Application/Common), called from `CustomerValidator`, `ProductValidator`, `InvoiceValidator` and `PaymentValidator`;
   `EfInvoiceStore.SaveChangesAsync` retries once on a detected deadlock (`SqlException` 1205) and then returns `false`, exactly like any
   other lost race — reusing the existing catch/409 shape, not a new abstraction.
2. A custom global `DecimalJsonConverter`, the first custom JSON converter in this codebase, reading the raw token (never `GetDecimal()`)
   and refusing exponent notation or more than 28 significant digits on every `decimal`-bound field.
3. ASP.NET Core's built-in rate limiter (one named fixed-window policy, only on `POST /api/auth/login`, partitioned by client address), a
   small hand-written middleware for `X-Frame-Options: DENY` and `/api`-only `Cache-Control: no-store`, and a 1 MiB Kestrel request-body
   limit — the first custom middleware in this codebase, confirmed together since all three are small `Program.cs` additions.
4. `TR_Payments_Immutable`, the exact `INSTEAD OF UPDATE, DELETE` shape spec 010 already built for `InvoiceHistoryEntries`, applied to
   `Payments` via a new migration.

## R11. Three 004-deferred items missing from this spec's own scope, found while writing `SECURITY.md` (T027)

**Decision**: cross-checking `SECURITY.md` against the developer's own deferred-items memory (T027's own instruction: "confirm every
item ... has a matching line, closed or open, none silently absent") found three items from the 004 close that neither `spec.md`'s
Assumptions nor any task here had accounted for. None is left silently absent:

- **A database check-constraint upper bound on `Products.UnitPrice`**: the application layer already enforced `ProductValidator.MaxUnitPrice`
  (999,999,999.99), but `CK_Products_UnitPrice` only checked `>= 0` — a value written directly, or by a future bug that bypassed the
  validator, would not have been caught by the database itself. This is exactly the kind of bounded, in-repo change with no open design
  question this spec already closes five of, so it is closed the same way: `CK_Products_UnitPrice` widened to
  `[UnitPrice] >= 0 AND [UnitPrice] <= 999999999.99` (migration `AddProductUnitPriceUpperBound`, a new theory case in
  `EfProductStoreCreateTests.cs` proving the database itself now refuses it).
- **A dedicated `StaffOnly` policy for `/api/customers` and `/api/products` reads**: left as a documented no-op, not added. The fallback
  policy (`RequireAuthenticatedUser`) already refuses an anonymous request on every endpoint that does not explicitly opt out
  (`SecurityRegressionTests.cs` proves this for every write route; the same fallback applies to every read route too, since none of them
  carries `AllowAnonymous`), and roles are exactly `Admin`/`Agent` with no third role possible without a constitution amendment (the
  constitution itself pins this). A dedicated `RequireRole("Admin","Agent")` policy could not refuse anything the fallback policy does not
  already refuse today, so adding one would be inert code, not a closed gap. Revisit if a third role is ever introduced.
- **`Development`'s exception page still echoing request headers (including the bearer token) for a genuinely unexpected exception**:
  `ThrowOnBadRequest = false` (spec 002) only covers a malformed request *body*; `Program.cs` still deliberately skips
  `app.UseExceptionHandler()` in `Development`, so a real, uncaught bug reaches ASP.NET Core's own developer exception page there, which
  echoes headers. Unlike the two items above, this is **not** closed here: fully closing it means giving up `Development`'s richer
  diagnostics (stack trace, request detail) everywhere a genuine bug is hit locally, a real trade-off between developer experience and
  defence-in-depth that this spec should not decide unilaterally while cross-checking an unrelated task. Recorded as a sixth Known Gap in
  `SECURITY.md` instead, so it stays visible rather than silently dropped a second time.

## Known gaps (left open by this spec, recorded in `SECURITY.md`)

- **HTTPS/HSTS enforcement**: needs a real deployment target; this application currently only runs on `http://localhost`. Revisit when
  one exists.
- **Payment idempotency**: a retried `POST /api/invoices/{id}/payments` (a flaky network, a doubled click) can still record two payments
  today; closing it needs a new field and a design decision about what "the same payment" means, not decided by this spec.
- **Invoice history redaction**: a note or payment note that is later cleared or corrected still reads verbatim, forever, in an immutable
  history entry (010's own known gap); still unresolved.
- **Stale role inside a still-valid token**: a demoted or deactivated account keeps its old role until the token's own expiry (default 60
  minutes); mitigated only by that lifetime, since full revocation needs session infrastructure this project does not have.
- **`TRUNCATE TABLE` bypasses an `INSTEAD OF DELETE` trigger** (already recorded for history in 010, now true of `Payments` too): SQL
  Server does not fire such triggers for `TRUNCATE`; the only real defence is never granting the application's database login `ALTER` on
  either table, an operational control outside this spec's code.
