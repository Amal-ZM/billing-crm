# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

BillingCRM is an internal staff CRM covering exactly three domains: customers, products,
invoices. It is a greenfield project driven by Spec Kit (`.specify/`). Spec 001 (foundation) added
the empty runnable skeleton: API, Angular shell, LocalDB wiring, `/health`. Spec 002 (staff auth)
added JWT sign-in, the Admin/Agent roles and the `StaffUsers` table. Spec 003 (customers) added the
`Customers` table and the customer screens and API. Spec 004 (products) added the `Products` table, the
price list screens and API (Admin writes, Agent reads) and is the first place money is stored. Spec 005
(invoice create) added the `Invoices` and `InvoiceLines` tables, the invoice-number sequence, and the Draft
invoice screens and API (any signed-in user); it stores lines but calculates **no totals**. Spec 006 (invoice totals) added
the stored invoice `Discount` (`AddInvoiceDiscount`) and the calculated amounts: line net, discount share, line VAT and the
invoice totals, from the pure Domain calculator `InvoiceMoney`. Spec 007 (invoice status) added the stored statuses Draft, Sent,
Paid and Cancelled (`AddInvoiceStatuses`), Send and Admin-only Cancel, the read-only lock, the frozen amounts and a row version;
Overdue is derived. Spec 008 (payments) added the `Payments` table (`AddPayments`), Record payment on a Sent invoice, and the payment
that settles an invoice making it Paid. Spec 009 (invoice print) added the printable invoice page (front end only: no API, table or
migration). Spec 010 (invoice history) added the permanent, append-only `InvoiceHistoryEntries` table (`AddInvoiceHistory`, with a
database trigger that refuses any change to it), the `ICurrentUser` port that reads the signed-in user from the token, and one
history entry per successful create/edit/send/payment/cancel, written in the same save as the change it describes. Spec 011
(dashboard) added `GET /api/invoices/summary` (five live figures on Home) and three optional filter values on `GET /api/invoices`
(`status`, `overdue`, `paidMonth`) so each dashboard card links to its own matching, already-filtered invoice list; no new table,
column or migration. Spec 012 (security), **the final spec in the 001-012 roadmap**, closed the accumulated hardening backlog
deferred across specs 002/004/007/008/009/010: a consolidated auth/authorization regression suite, mapping a genuine SQL Server
deadlock to the same 409 a lost race already gets, a `DecimalJsonConverter` (refuses exponent notation and 29+ significant digits
on every money/quantity field), anti-framing and `no-store` response headers, a request body size limit, login rate limiting, the
shared `FreeTextGuard` (refuses bidi/control characters in every free-text field), database-level immutability for `Payments`
(`AddPaymentsImmutability`) matching spec 010's history trigger, a `Products.UnitPrice` database check-constraint upper bound
(`AddProductUnitPriceUpperBound`), and `SECURITY.md` at the repository root. See
`.specify/memory/constitution.md` for the full, authoritative rules; this file summarizes what's
needed day-to-day.

**Explicitly out of scope** — do not build these even if a request implies them: shop, cart,
payment gateway, support tickets, WhatsApp integration.

## Stack

- Backend: .NET 8 Minimal API
- Frontend: Angular, standalone components, Angular Material
- Database: SQL Server LocalDB
- Auth: JWT bearer tokens; roles are exactly `Admin` and `Agent` (no others without a
  constitution amendment)
- Money: always `decimal(18,2)`, currency SAR. Never `float`/`double` for money at any layer.

## Hard Rules (from the constitution)

1. **One spec at a time.** Specs are numbered 001–012 and done strictly in order. Do not start
   the next spec until the current one has been verified running on the developer's machine.
2. **Expected business errors are not exceptions.** Model them as a `Result`-style return and
   surface them to clients as `ProblemDetails` (validation failure, not-found, conflict, auth
   denial, etc.). Exceptions are reserved for genuinely unexpected failures.
3. **No secrets in git.** Connection strings, API keys, JWT signing keys — none of it committed,
   including in sample/appsettings files. Supply via local config outside source control.
4. **Explain before generating a new kind of file.** A new Minimal API endpoint pattern, a new
   Angular architectural pattern, or a new shared abstraction must be explained and confirmed
   with the developer before being generated. Reusing an already-established pattern does not
   need re-explanation.
5. **Use project skills for API/UI edits, and run `api-reviewer` after each spec** (before that
   spec is considered done, per rule 1). Skill/hook invocation for this project is already wired
   to run automatically — don't skip it and don't ask whether to run it, just proceed.

## Spec Kit Workflow

This repo uses Spec Kit; skills live in `.claude/skills/speckit-*` and are invoked as slash
commands. Typical order for a new spec:

1. `/speckit-specify` — write/update the feature spec from a natural-language description
2. `/speckit-clarify` (optional) — resolve ambiguous areas before planning
3. `/speckit-plan` — produce the implementation plan
4. `/speckit-tasks` — generate the dependency-ordered task list
5. `/speckit-checklist` (optional) — quality checklist for the spec
6. `/speckit-analyze` (optional) — cross-check spec/plan/tasks consistency before implementing
7. `/speckit-implement` — execute the tasks
8. `/speckit-converge` — after implementing, catch any remaining work the spec/tasks missed

`/speckit-constitution` updates `.specify/memory/constitution.md` itself — use it only for
governance changes, never for feature work.

## Current Repository Layout

```
.claude/skills/               Spec Kit skills (speckit-specify, -plan, -tasks, -implement, etc.)
.specify/memory/              constitution.md — the authoritative project rules
.specify/templates/           Spec Kit templates (spec, plan, tasks, constitution)
.specify/scripts/             PowerShell helper scripts used by the skills
.config/dotnet-tools.json     Pins dotnet-ef 8.x (use `dotnet ef`, not the global tool)
specs/                        One folder per spec (spec, plan, tasks, research, contracts, ...)
backend/BillingCrm.sln        .NET 8 solution
backend/src/BillingCrm.Domain          No references; StaffUser, StaffRole (Admin, Agent), Customer,
                                       CustomerType (Company, Individual), EmailNormalizer, Product,
                                       ProductUnit (Hour, Item, Service), Invoice (aggregate root),
                                       InvoiceLine, InvoiceStatus (Draft, Sent, Paid, Cancelled), InvoiceStatusRules
                                       (the legal status changes), InvoiceNumber, InvoiceMoney
                                       (the money calculator: AmountLine, LineAmounts, InvoiceAmounts,
                                       InvoiceBalance via Balance), Payment, PaymentMethod (Cash, Bank, Other),
                                       HistoryKind (Created, DraftEdited, Sent, Payment, Cancelled), HistoryChange,
                                       InvoiceHistoryEntry (permanent, no setters, outside the Invoice aggregate)
backend/src/BillingCrm.Application     -> Domain; Result/Error, PagedResult<T> and FreeTextGuard (Common/, spec 012), AuthService +
                                       ICurrentUser/Actor (Auth/), CustomerService/Validator + ICustomerStore (Customers/),
                                       ProductService/Validator + IProductStore (Products/),
                                       InvoiceService/Validator + PaymentValidator + IInvoiceStore + InvoiceHistoryBuilder +
                                       InvoiceHistorySnapshot + InvoiceSearchFilter + DashboardSummaryDto (Invoices/)
backend/src/BillingCrm.Infrastructure  -> Application; BillingDbContext, Migrations, JWT token
                                       service, password hasher, EF user store, EfCustomerStore
                                       (Customers/), EfProductStore (Products/), EfInvoiceStore (Invoices/),
                                       DecimalJsonConverter (Serialization/, spec 012), AddInfrastructure()
backend/src/BillingCrm.Api             -> Application, Infrastructure; Program.cs (auth pipeline; spec 012 added the
                                       anti-framing/no-store header middleware, the request-body-size check and the
                                       login rate limiter), Auth/HttpContextCurrentUser (ICurrentUser from the token's claims),
                                       Endpoints/ (health, /api/auth, /api/admin, /api/customers,
                                       /api/products, /api/invoices, Result -> ProblemDetails), Development/ (dev-only seeder)
backend/tests/BillingCrm.Application.Tests     xUnit unit tests for AuthService/CustomerService/ProductService/InvoiceService (fakes),
                                               FreeTextGuardTests (spec 012)
backend/tests/BillingCrm.Api.Tests             xUnit + WebApplicationFactory; in-memory stores, fake
                                               signing key (no LocalDB needed); SecurityRegressionTests,
                                               ErrorShapeRegressionTests, DecimalPrecisionTests, SecurityHeadersTests,
                                               RequestBodySizeTests, LoginRateLimitTests (spec 012)
backend/tests/BillingCrm.Infrastructure.Tests  xUnit against a throwaway LocalDB database per test class
                                               (BillingCrm_Tests_<guid>, real migrations; NEEDS LocalDB);
                                               EfInvoiceStoreDeadlockTests, EfPaymentImmutabilityTests (spec 012)
frontend/                     Angular 21 workspace; src/app/core (shell, HealthService, dashboard/ (DashboardService,
                              spec 011), auth/: AuthService, interceptor, guards, admin/, customers/, products/, invoices/,
                              money/decimal-text, http/problem-details), src/app/features (home, login, admin,
                              access-denied, customers/, products/, invoices/: list + detail + one add/edit form
                              each, plus payment-form for Record payment, invoice-print for the printable page and
                              invoice-history for the History screen; invoices/pickers: customer and product
                              pickers), src/environments (apiBaseUrl), src/app/security-no-unsafe-html.spec.ts (spec 012, a
                              repo-wide check that no source file uses innerHTML/outerHTML/bypassSecurityTrust*)
README.md                     Developer setup (secrets, database, run, tests)
```

## Run Commands

Run from the repository root. Full setup (secrets, database) is in `README.md`.

- Backend: `dotnet build backend/BillingCrm.sln`,
  `dotnet run --project backend/src/BillingCrm.Api` (http://localhost:5080),
  `dotnet test backend/BillingCrm.sln` (single test: add `--filter FullyQualifiedName~<Name>`)
- Frontend (from `frontend/`): `npm install`, `ng serve` (http://localhost:4200),
  `ng test --watch=false`, `ng build`. Use npm 11+ (e.g. `npx npm@11 install <pkg>`) when
  adding or updating packages; npm 9/10 cannot resolve the Angular 21 tree from scratch.
- Database: `dotnet tool restore`, then
  `dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api`.
  The connection string comes from user-secrets (`ConnectionStrings:DefaultConnection`, read only
  in the `Development` environment) or the `ConnectionStrings__DefaultConnection` env var; it is
  never committed. New migrations: add them with the same `--project`/`--startup-project`.
- Signing key: set `Jwt:SigningKey` (>= 32 bytes, random) in user-secrets or `Jwt__SigningKey`
  **before** any `dotnet ef` command or running the API; it is never committed and the API refuses
  to start without it. Token lifetime: `Jwt:ExpiryMinutes` (default 60, zero clock skew).
- Auth: `POST /api/auth/login` (anonymous) returns `{accessToken, expiresAtUtc, email, role}`;
  `GET /api/auth/me` needs a token; `/api/admin/*` is Admin-only (403 for Agent). The fallback
  policy requires a signed-in user, and every endpoint must declare `AllowAnonymous` or
  `RequireAuthorization` (a test enforces this). Expected failures are `Result` -> `ProblemDetails`.
  The two dev accounts (see Seed Users) are created on first `Development` start by
  `Development/DevelopmentSeeder`; the seeder is registered only in Development and refuses any
  database that is not a `(localdb)` instance, so they never exist anywhere else.
- Customers: `/api/customers` (any signed-in Admin or Agent): `POST` (201 + `Location`), `GET`
  (`q`, `type`, `active`, `page`, `pageSize`; returns `PagedResult<T>`), `GET /{id}`, `PUT /{id}`,
  `PUT /{id}/active`. A duplicate email is 409 (normalized email, case/space-insensitive, active and
  inactive customers alike; guaranteed by a unique index, with `ICustomerStore.TryAdd/TryUpdate`
  returning `false` instead of throwing). **There is no delete**: customers are retired by making them
  inactive, because invoices will refer to them. This is the pattern products and invoices follow.
  The Angular app keeps the list's search/filters/page in the URL query string. Routes: `/customers`,
  `/customers/new`, `/customers/:id`, `/customers/:id/edit` (lazy-loaded, under the signed-in shell).
- Products: `/api/products` is a **mixed-role group**: `GET` (list/search with `q` over name or SKU, `unit`,
  `status`, `page`, `pageSize`) and `GET /{id}` are open to any signed-in Admin or Agent; `POST`, `PUT /{id}` and
  `PUT /{id}/active` add `AuthorizationPolicies.AdminOnly`, so an Agent gets 403 before the body is read. A
  duplicate SKU is 409 (stored already upper-case, A-Z 0-9 - _ only, unique index `IX_Products_Sku`, active and
  inactive alike, `IProductStore.TryAdd/TryUpdate` return `false` instead of throwing). **"Deactivate to hide"**: the
  default list is active products only; only an Admin may pass `status=inactive|all` (the endpoint passes the service
  just an `includeInactive` boolean from `IsInRole("Admin")`); an Agent always gets active products, and an inactive
  product by id is the same 404 as an unknown id. **There is no delete** and no stock/inventory field. Angular
  routes: `/products`, `/products/new` and `/products/:id/edit` (both `roleGuard('Admin')`), `/products/:id`.
- Invoice print (spec 009, front end only): `/invoices/:id/print` (`InvoicePrintComponent`) is a **top-level route outside the signed-in
  shell**, behind the same `authGuard`, so the toolbar, menus and Sign out are not in the page and nothing is hidden for print. It is
  reached from the details' **Print** link (every status, any signed-in user), which opens in the **same tab** (the session lives in
  each tab's `sessionStorage`). It makes the one existing `GET /api/invoices/{id}` and shows title BillingCRM, customer name, number,
  dates, notes (when present), lines, totals and status, with the details' own field paths, pipes and `data-testid`s. **It calculates,
  rounds and derives nothing**: `invoice-print-parity.spec.ts` renders the details and the print page from one invoice and compares
  them (a mutation of the page's format fails it). It leaves out payments, paid/remaining, ids, contact details and seller details
  (spec Clarifications). Print rules live in the component's own stylesheet (`ViewEncapsulation.None`, everything under
  `.invoice-print`, so `@page { margin }` exists only while the page is shown): the on-screen bar (`.no-print`, holding the Print button
  and the way back) is hidden, headings repeat, rows and totals stay whole, black on white. The Print button calls `window.print()`
  through the injected `DOCUMENT`; nothing prints by itself. No PDF or file, no history, no backend change.
- Invoice status (spec 007): stored statuses are exactly `Draft`, `Sent`, `Paid`, `Cancelled` (`CK_Invoices_Status`); **Overdue is never
  stored** (`Invoice.IsOverdue(today)` = Sent and due date before today's Saudi date, returned as `isOverdue`). The only legal changes
  are Draft to Sent, Draft to Cancelled, Sent to Cancelled and (spec 008) Sent to Paid, spelled out only in `InvoiceStatusRules` (Paid is
  final; only a settling payment causes Sent to Paid, so it is never offered to a person). `POST /api/invoices/{id}/send` (any signed-in user) and `POST /api/invoices/{id}/cancel` (**Admin only**: the
  `AdminOnly` policy is added on the route, so an Agent gets 403 before the id is looked up) take no body and return the invoice. An
  illegal change is 409 `invoice_status_change_not_allowed` and an edit of a non-Draft invoice is 409 `invoice_not_draft` (judged
  before the body), both naming the current status (Overdue for a late Sent invoice); a Draft with a past due date cannot be sent
  (400 on `dueDate`). A non-Draft invoice is read-only (the aggregate also throws if an edit reaches it: a bug, the service refuses
  first). Every invoice response has `isOverdue`, `sentAtUtc`, `cancelledAtUtc`, `allowedTransitions` (what the stored status permits,
  before role) and `isEditable`; the Angular code uses those flags and never re-derives the rules; Send and Cancel ask "Yes, ..."
  inline first (both are permanent). Leaving Draft **freezes the amounts** into `Invoices.Frozen*` and `InvoiceLines.Frozen*`
  (`CK_Invoices_FrozenAmounts`, `CK_Invoices_StatusTimes` keep them consistent) and a non-Draft invoice reads those, so nothing can move
  them. `Invoices.RowVersion` is an EF concurrency token: two changes to the same invoice at the same instant give one winner and one
  409 `invoice_changed`; this **replaces 005's "last save wins"** for Drafts.
- Payments (spec 008): `POST /api/invoices/{id}/payments` (any signed-in user, in the `/api/invoices` group) records money received against a
  **Sent** invoice (also Overdue): body `{date yyyy-MM-dd, amount, method Cash|Bank|Other, note?}`, answers `201` + `Location` + the whole
  updated invoice. A payment is a record staff make, **not a payment gateway**. It lives in the invoice aggregate (`Payments` table,
  written only through `Invoice.RecordPayment`; no payment service or store). **Paid and remaining are never stored**: `paid` is the sum of
  the payments and `remaining` is the frozen grand total minus it (`InvoiceMoney.Balance`, exact, no rounding); the payment that brings
  remaining to exactly 0.00 sets the status to Paid in the same save. Order of checks: not found (404), status must be Sent (409
  `invoice_payment_not_allowed` naming it, judged **before** the body), then the body with all field errors together (400 on `date`, `amount`,
  `method`, `note`): amount > 0, two decimals (refused, never rounded), not above remaining; date not after today's Saudi date nor before the
  Saudi date the invoice was sent; method spelled exactly Cash/Bank/Other; note <= 1000. Every invoice response has `paid`, `remaining`,
  `payments[]` and `acceptsPayment` (the flag Angular trusts for Record payment). Payments are **permanent** (no edit, void, delete or
  route for one) and an invoice **with payments cannot be cancelled** (409 `invoice_has_payments`, after the illegal-change check, an Agent
  still 403 first); `allowedTransitions` never lists Paid and drops Cancelled once a payment exists. Adding a payment writes the invoice row
  too, so `RowVersion` gives one winner for two payments or a payment racing a cancel (409 `invoice_changed`). The error codes are service
  `Error.Code`s: the HTTP body has only `title`/`detail`/`errors`. Angular: `/invoices/:id/payments/new` (`payment-form`, a form like the
  others, native select for the method, date prefilled with the browser's date, the server judges), a payments section on the details screen.
  After a save whose answer is lost (no reply or a 5xx) the form turns Save off until the person has checked the invoice again, because the
  payment may already exist and cannot be undone; after a 409 or an amount error it fetches the invoice again quietly.
- Invoice amounts (spec 006): **calculated while a Draft, frozen when it leaves Draft (spec 007)** (only `Invoices.Discount` is stored, `decimal(18,2)`, default 0,
  `CK_Invoices_Discount >= 0`). `InvoiceMoney.Calculate(lines, discount)` is the one place the rules live (skill
  `invoice-money`): line net = `Round(quantity x saved unit price, 2)`; the discount comes off the net **before VAT** and is
  shared over the lines in proportion to their net in whole halalas (`BigInteger`, largest remainder, ties to the earlier
  line, shares add up to the discount exactly); line VAT = `Round((net - share) x VAT% / 100, 2)`; grand total = net after
  discount + total VAT. Rounding is half away from zero, once per line. Amounts never exceed `InvoiceMoney.MaxAmount`
  (9,999,999,999,999.99) so a JSON number is exact in a browser. Every invoice response has `discount`, `totals`
  (`totalNet`, `discount`, `netAfterDiscount`, `totalVat`, `grandTotal`) and per line `net`, `discountShare`, `vat`; the list
  has none. The optional `discount` in the body is a `decimal?` number in SAR; a `PUT` without it clears a saved one. The
  service checks the amounts a save **would** produce before changing anything (`InvoiceService.CheckAmounts`): discount above
  the total net is a 400 on `discount`, a too-large line a 400 on `lines[i].quantity`, a too-large total a 400 on `lines`.
  The Angular code shows the returned amounts and **never calculates money** (no live preview).
- Money (first used by products, invoices must follow it): `UnitPrice` is `decimal(18,2)` SAR; `VatPercent` is a
  percentage stored as `decimal(5,2)` (0-100). JSON numbers bind to `decimal?`; a value with more than two decimals
  is **refused, never rounded** (`value != decimal.Round(value, 2)`); VAT defaults to 15 on add only and is required on
  edit. The UI holds amounts as text, checks them as text, sends `Number(text)` and formats for display only.
- Invoices (spec 005): `/api/invoices` is open to any signed-in Admin or Agent: `POST` (201 + `Location`), `GET` (newest
  first, `page`, `pageSize`, returns `PagedResult<T>`), `GET /{id}`, and `PUT /{id}` which saves the **whole Draft**: header
  plus the full list of lines. A line is **existing** (`id`: only its quantity may change, its saved copy is kept), **new**
  (`productId`: copied from the product now) or **left out** (removed). The service checks everything first (shape, then the
  customer, then each new line's product) and reports **all** problems together as 400 field errors with array paths
  (`lines[0].quantity`, `lines[1].productId`); only when there are none is anything changed, so a refused save changes
  nothing. Unknown/inactive customer or product is a 400 field error (never a 404/409); an unknown invoice id is 404; a `PUT` that lost a race with another edit of the same rows is a 409 (`IInvoiceStore.SaveChangesAsync` returns `false`); there
  is no delete, un-send or reopen (status changes are the two action routes of spec 007). Angular routes: `/invoices`, `/invoices/new`, `/invoices/:id`, `/invoices/:id/edit`, `/invoices/:id/payments/new` (008).
- Invoice number: the database sequence `InvoiceNumbers` (1 to 999,999, no cycle) fills the `int` column `Invoices.Number`
  (unique index) when the row is inserted; the text `INV-000123` is made only by `InvoiceNumber.Format` and never stored.
  The sequence is `NO CACHE` (migration `NoCacheInvoiceNumbers`), so a restart cannot skip numbers. Gaps after a rolled-back save are fine, duplicates are impossible, a refused save uses no number. There is no `Try*` on `IInvoiceStore`: nothing a
  person types can violate a unique index.
- Snapshot rule: a line copies the product's name, SKU, unit, unit price and VAT percent **once** (`InvoiceLine.CreateFrom`)
  into its own columns and never rewrites them; `ProductId` is a reference only and nothing joins to the live product to show
  a line. **A Draft stores no line amount, VAT amount or total**: only quantity, unit price and VAT percent (exact:
  `decimal(9,2)`, `decimal(18,2)`, `decimal(5,2)`) plus the invoice discount; spec 006 calculates its amounts from them, so they
  can never follow the live product. When an invoice leaves Draft (spec 007) the amounts are frozen into the `Frozen*` columns.
  "Today" for the due-date rule is the date at a fixed UTC+03:00 (`InvoiceService.Today`). An existing Draft keeps a due date
  that has since passed as long as it is unchanged; the same customer is allowed even if inactive, a different one must be active.
- Front-end helpers added by 005: `core/money/decimal-text.ts` (checks a quantity or the discount as text, never through a float),
  `applyProblemToForm` maps array-path keys such as `lines[0].quantity` to the matching line control, and the customer and
  product pickers (`features/invoices/pickers`) search the existing customer/product list APIs (active only).
- Test data: the API/Application tests never touch a database. The Infrastructure tests need LocalDB
  and delete only the throwaway `BillingCrm_Tests_*` database they create.
- Development quirk: minimal APIs would throw on a malformed body in Development and the developer page
  would echo headers (incl. the token); `Program.cs` sets `ThrowOnBadRequest = false` so Development
  answers with the same clean 400 as Production.
- Health: `GET http://localhost:5080/health` (anonymous). Stopping LocalDB does not make it
  unhealthy (LocalDB auto-starts); use a nonexistent database name to test the 503 path.
- Invoice history (spec 010): a permanent, append-only log of every change to an invoice, in a table
  (`InvoiceHistoryEntries`) **outside** the `Invoice` aggregate (never loaded with it). `InvoiceService`'s create, edit
  (only when it changes something), send, record-payment and cancel each record exactly one entry — via
  `IInvoiceStore.RecordHistory(entry)` — **after every check has passed, immediately before the one save**, so the
  entry and the change it describes are written in the same transaction: a refused request, an unchanged edit or a
  save that loses a race (`SaveChangesAsync` returns `false`) writes none. The five kinds are exactly `Created`,
  `DraftEdited`, `Sent`, `Payment` (a settling payment is one `Payment` entry that also shows `Sent` to `Paid`; there
  is no sixth kind) and `Cancelled`. `InvoiceHistoryBuilder` (pure, invariant culture) turns an event into an entry's
  `Changes` (`HistoryChange(Field, Before, After)`, both nullable text): money and quantities two decimals, dates
  `yyyy-MM-dd`, statuses by their stored name (never `Overdue`); `DraftEdited` diffs two `InvoiceHistorySnapshot`s
  (lines matched by id) and returns `null` when nothing differs. **Who made the change is never a field of a
  request**: the actor is `ICurrentUser.Current` (`Actor(Email, Role)`), read from the signed-in user's token
  (`HttpContextCurrentUser` in the Api layer), and it throws if there is no usable signed-in user — a defect, since
  every invoice endpoint requires sign-in. `GET /api/invoices/{id}/history` (any signed-in Admin or Agent, no other
  operation on history exists) answers `{number, startsWithCreation, entries[{id, kind, occurredAtUtc, actorEmail,
  actorRole, changes[{field, before, after}]}]}`, entries newest first (the `Sequence` identity column);
  `startsWithCreation` is false for an invoice made before this feature (no entries invented for it). The database
  itself enforces permanence: an `INSTEAD OF UPDATE, DELETE` trigger (`TR_InvoiceHistoryEntries_Immutable`, added in
  migration `AddInvoiceHistory`) refuses any change to a row (a database administrator dropping the trigger is a
  known, documented gap). The Angular screen (`/invoices/:id/history`, under the shell, any signed-in user, reached
  from a **History** link on the details before Print) only reads: it has no button, form or control besides the
  back link and Retry, and shows every value exactly as received (no formatting but the time, no recalculation).
- Dashboard (spec 011): `GET /api/invoices/summary` (any signed-in Admin or Agent, in the existing `/api/invoices` group, no query
  string, always `200`) answers `{draftCount, unpaidCount, overdueCount, paidThisMonthCount, unpaidAmount}`, computed fresh from the
  current `Invoices`/`Payments` on every read (`InvoiceService.GetSummaryAsync` -> `IInvoiceStore.GetSummaryAsync`), never cached.
  `unpaidCount` is every Sent invoice (on-time and overdue alike); `overdueCount` is the Sent-and-past-due subset;
  `paidThisMonthCount` counts a Paid invoice by its own **settling payment**'s date (its last-recorded payment — no more can follow
  once it is Paid), never an earlier part payment's; `unpaidAmount` is the SAR sum of every Sent invoice's own `Remaining`. A
  Cancelled invoice is never part of any figure. `GET /api/invoices` gains three optional query values — `status` (`Draft`/`Sent`/
  `Paid`/`Cancelled`, an unrecognized value is 400 naming `status`), `overdue` (`true`; only narrows further with `status=Sent`,
  otherwise silently ignored) and `paidMonth` (`true`; only narrows further with `status=Paid`, otherwise ignored) — carried by
  `InvoiceSearchFilter` through a widened `IInvoiceStore.SearchAsync`; with none of the three, the response is unchanged from before
  this spec. The invoice list screen itself gains **no filter UI**: these exist only so a Home dashboard card's link
  (`core/dashboard/dashboard.service.ts`, five cards added to the existing `HomeComponent`, each an `[routerLink]` to `/invoices`
  with `[queryParams]` from the table in contracts/dashboard-api.md) can reach its own matching, already-filtered list; the invoice
  list reads `status`/`overdue`/`paidMonth` from its own address, exactly as it already reads `page`/`pageSize`. No new table,
  column or migration.
- Security hardening (spec 012, the final spec): see `SECURITY.md` at the repository root for the full picture (what's enforced,
  where, and what's a documented open gap). In brief — every write route requires a real session everywhere at once, proven by one
  consolidated `SecurityRegressionTests.cs` rather than per-resource tests alone; a SQL Server deadlock on an invoice save answers
  the same `409 invoice_changed` any other lost race already does; a `decimal`-bound field (product price/VAT, invoice
  quantity/discount, payment amount) refuses exponent notation or more than 28 significant digits
  (`DecimalJsonConverter`); a free-text field (customer name/address, product name, invoice notes, payment note) refuses a Unicode
  bidi-override or other control character (`FreeTextGuard`); every response carries `X-Frame-Options: DENY` and every `/api`
  response also `Cache-Control: no-store`; a request body over 1 MiB is `413`; `POST /api/auth/login` is rate-limited (10/minute per
  source, `429` with `Retry-After` past that); `Payments` cannot be changed or deleted, the same database-trigger guarantee spec 010
  gave invoice history (`TR_Payments_Immutable`, migration `AddPaymentsImmutability`); `Products.UnitPrice` now also has a database
  check-constraint upper bound (`AddProductUnitPriceUpperBound`). This is the last spec in the 001-012 roadmap: once verified, the
  whole project as specified is complete.

## Seed Users (local LocalDB only)

For local development/testing only — not real credentials, never reuse outside a developer's
own LocalDB instance:

- `admin@crm.local` / `Admin#123` — role `Admin`
- `agent1@crm.local` / `Agent#123` — role `Agent`
