# Security

BillingCRM is an internal staff tool (customers, products, invoices) run on a developer's own machine or a
company network — not exposed to the public internet today. This is a short, factual checklist of what is
enforced, where, and what is proven by an automated test; not a full threat model. Written by spec
`012-security`, which closed the accumulated hardening backlog from specs 002–010 in one place; see
`.specify/memory/constitution.md` for the project's governing rules and `specs/012-security/` for the full
spec, plan and research behind every item below.

## Authentication & Sessions

- **Sign-in**: `POST /api/auth/login` (anonymous) issues a JWT bearer token; every other endpoint requires one
  (`AuthorizationPolicyBuilder().RequireAuthenticatedUser()` is the *fallback* policy in
  `backend/src/BillingCrm.Api/Program.cs`, so an endpoint nobody annotated still needs a signed-in user — an
  endpoint must opt out with `AllowAnonymous`, never opt in to being protected).
- **Token lifetime**: `Jwt:ExpiryMinutes` (default 60), zero clock skew (`ClockSkew = TimeSpan.Zero` — "expired"
  means expired, not "expired 5 minutes ago"), a pinned signing algorithm (`ValidAlgorithms = [HmacSha256]`, so a
  token signed any other way is refused outright), and required issuer/audience validation.
- **Roles**: exactly `Admin` and `Agent` (the constitution pins this; a third role needs a constitution
  amendment). A demoted or deactivated account keeps its old role until its token's own expiry — see Known Gaps.
- **Login rate limiting**: at most 10 `POST /api/auth/login` attempts per minute per source address (an
  in-memory fixed window, `backend/src/BillingCrm.Api/Program.cs`); the 11th is `429 Too Many Requests` with a
  `Retry-After` header. Proven by `backend/tests/BillingCrm.Api.Tests/LoginRateLimitTests.cs`.
- **Response headers**: every response carries `X-Frame-Options: DENY` (nothing on this site can ever be framed
  by another origin — the real target is an invoice's Send/Cancel actions); every `/api` response also carries
  `Cache-Control: no-store` (an authenticated screen must never be restored from a shared machine's
  back/forward cache). Proven by `backend/tests/BillingCrm.Api.Tests/SecurityHeadersTests.cs`.
- **Request body size**: capped at 1 MiB (Kestrel's own limit, plus an explicit `Content-Length` check earlier
  in the pipeline that also works under a test host); a larger body is refused with `413` before any endpoint or
  validator runs. Proven by `backend/tests/BillingCrm.Api.Tests/RequestBodySizeTests.cs`.

## Authorization

Every write endpoint has an explicit authorization requirement (a repo-wide test enforces this, not just a
convention). The table below is what each operation category actually needs; nothing here is re-derived by the
Angular app, which only hides a button the current role cannot call — the API is the real enforcement point.

| Operation category | Who |
|---|---|
| Any read (customers, products, invoices, history, dashboard) | Any signed-in user (Admin or Agent) |
| Create/edit a customer, add a payment, create/edit/send a Draft invoice | Any signed-in user |
| Create, edit or activate/deactivate a **product** | **Admin only** (`AuthorizationPolicies.AdminOnly`) |
| **Cancel** an invoice (any status) | **Admin only** |
| Delete anything (a customer, a product, an invoice of any status, a payment, a history entry) | **Nobody** — no such route exists anywhere in the API |

Proven together, route by route, by `backend/tests/BillingCrm.Api.Tests/SecurityRegressionTests.cs`: every
documented write route refuses a missing, expired or tampered token with `401` (and changes nothing); an Agent
is refused with `403` on the three restricted actions above *before the request body is even read* (a malformed
body does not change the answer); an Admin succeeds on all four; the route list itself is asserted to contain no
`DELETE` anywhere.

**Data integrity below the role check**: once a `Payment` is recorded it can never be changed or deleted by
anyone, any role, any tool — a database trigger (`TR_Payments_Immutable`, the same shape as
`TR_InvoiceHistoryEntries_Immutable` below) refuses any `UPDATE` or `DELETE` on the `Payments` table outright,
whatever wrote the statement (the application, a raw query, a database client). Proven by
`backend/tests/BillingCrm.Infrastructure.Tests/EfPaymentImmutabilityTests.cs`. Invoice history
(`InvoiceHistoryEntries`) has carried the identical guarantee since spec 010.

**Attribution**: who made a change is recorded in `InvoiceHistoryEntries` (actor email and role, as they were at
that moment) for every create, edit, send, payment and cancel from spec 010 onward — resolved *prospectively
only*. Neither `Invoices` nor `Payments` itself carries a `RecordedBy` column, so this cannot be backfilled for
anything recorded before spec 010 shipped; attribution for an older row does not exist anywhere.

## Input Validation

- **Expected problems always look the same**: a validation failure is `400` naming the field (or
  `lines[i].field` for an invoice line), an unknown id is `404`, a duplicate unique value or an illegal status
  change is `409` — always through `Result`/`ProblemDetails`, never an unhandled exception, and the body never
  carries more than `title`/`detail`/`errors` (no internal error code, no stack trace). This now also covers a
  genuine database-level conflict: a SQL Server deadlock on an invoice save is caught and answered with the same
  `409 invoice_changed` any other lost race already gets, never an unhandled `500`. Proven by
  `backend/tests/BillingCrm.Api.Tests/ErrorShapeRegressionTests.cs` and
  `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreDeadlockTests.cs`.
- **Decimal precision**: every money/quantity field (product price and VAT, invoice line quantity, invoice
  discount, payment amount) refuses exponent notation (`1e5`) and more than 28 significant digits at the JSON
  layer, before a `decimal` is ever constructed from the token (`DecimalJsonConverter`,
  `backend/src/BillingCrm.Infrastructure/Serialization/`) — closing a gap where such a value used to be silently
  rounded or shortened rather than refused. Proven by
  `backend/tests/BillingCrm.Api.Tests/DecimalPrecisionTests.cs`.
- **Free text can't carry a hidden character**: every free-text field (customer name/address, product name,
  invoice notes, payment note) refuses a Unicode bidirectional-override character or another control character
  outright (`FreeTextGuard`, `backend/src/BillingCrm.Application/Common/`), on top of its existing length limit.
  Proven by `backend/tests/BillingCrm.Application.Tests/FreeTextGuardTests.cs` and one added case each in
  `CustomerValidatorTests`, `ProductValidatorTests`, `InvoiceValidatorTests`, `PaymentValidatorTests`.
- **Database check constraints back up the application layer** for every money/quantity field that has one
  (for example `CK_Products_UnitPrice` mirrors `ProductValidator.MaxUnitPrice` exactly), so a value that bypassed
  the validator — a bug, a raw query — is still refused at the database.

## Output Encoding

Every screen shows user-entered text (a customer's name, an invoice's notes, a payment's note, a history entry)
through Angular's own template interpolation (`{{ }}`) or a pipe, which HTML-escapes by construction; nothing in
the Angular app ever uses `innerHTML`, `outerHTML` or bypasses Angular's sanitizer
(`bypassSecurityTrust*`). This was true before spec 012 (confirmed by a repo-wide review); what spec 012 adds is
an automated check that keeps it true going forward instead of relying on a one-off manual review each time:
`frontend/src/app/security-no-unsafe-html.spec.ts` fails the suite the day any of those three ever appears in a
non-test source file.

## Secrets

- The database connection string and the JWT signing key are **never committed**, in any form, including in
  sample/`appsettings` files (Constitution III). They are supplied only through user-secrets (read in the
  `Development` environment only) or environment variables (`ConnectionStrings__DefaultConnection`,
  `Jwt__SigningKey`); the API refuses to start without a signing key of at least 32 bytes. See `README.md`
  ("Configure the database connection", "Configure the signing key") for exact commands.
- The two seeded local-development accounts (`admin@crm.local`, `agent1@crm.local`) exist **only** against a
  LocalDB connection string, created only by `Development/DevelopmentSeeder`, which is registered only in the
  `Development` environment and refuses to run against any database that is not a `(localdb)` instance. They are
  not real credentials and must never be reused anywhere else.
- Development's own convenience (`ThrowOnBadRequest = false`) keeps a malformed request body from ever reaching
  the developer exception page, which would otherwise echo request headers — including the bearer token — into
  the response.

## Known Gaps

Left open on purpose, each because it needs something this spec could not decide or build alone. Closing one is
future work, not an oversight:

- **HTTPS/HSTS enforcement**: this application currently only runs on `http://localhost`; there is no real
  deployment target yet to enforce it against. Revisit when one exists.
- **Payment idempotency**: a retried `POST /api/invoices/{id}/payments` (a flaky network, a doubled click) can
  still record two separate payments today. Closing it needs a new field and a design decision about what "the
  same payment" means — not decided by this spec.
- **Invoice/payment history redaction**: a note or payment note that is later cleared or corrected still reads
  verbatim, forever, inside an immutable `InvoiceHistoryEntries` row (an unresolved question carried since spec
  010).
- **Stale role inside a still-valid token**: a demoted or deactivated account keeps its old role until its
  token's own expiry (default 60 minutes) — mitigated only by that lifetime, since real revocation needs session
  infrastructure this project does not have.
- **`TRUNCATE TABLE` bypasses an `INSTEAD OF DELETE` trigger**: SQL Server does not fire such a trigger for
  `TRUNCATE`, and the statement needs only `ALTER` permission on the table (less than dropping the trigger or
  the table outright) to silently empty it. True of both `InvoiceHistoryEntries` (since spec 010) and, now,
  `Payments`. The only real defence is an operational one — never granting the application's own database login
  `ALTER` on either table — outside what code in this repository can enforce.
- **No dedicated `StaffOnly` read policy**: considered and deliberately not added. `/api/customers` and
  `/api/products` reads rely on the fallback policy (any signed-in user), which already refuses an anonymous
  request; since roles are exactly `Admin`/`Agent` with no third role possible without a constitution amendment,
  a dedicated `RequireRole("Admin","Agent")` policy could not refuse anything the fallback does not already
  refuse today. Revisit if a third role is ever introduced.
- **`Development`'s exception page still echoes request headers (including the bearer token) for a genuinely
  unexpected exception**: `ThrowOnBadRequest = false` closed this for a malformed *request body* (spec 002), but
  a real, uncaught bug still reaches ASP.NET Core's own developer exception page in `Development`
  (`Program.cs` deliberately skips `UseExceptionHandler()` there, to keep the richer local-development
  diagnostics). Closing it fully means giving up that diagnostic detail during local development everywhere, a
  trade-off this spec did not decide unilaterally; revisit as its own, explicit decision.
