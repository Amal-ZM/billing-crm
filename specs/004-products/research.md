# Research: Products

**Feature**: 004-products | **Date**: 2026-09-24

No `NEEDS CLARIFICATION` markers remain in the spec. The items below record the decisions the plan
depends on. Items marked **Confirm** are new patterns or judgement calls that need the developer's
answer before code is generated (Constitution V) and are repeated in plan.md. Everything not marked
**Confirm** reuses a pattern already approved and verified in spec 003.

## R1. What "CRUD" and "hide" mean (spec FR-010, FR-012) — Confirm

- **Decision**: Create, read (list + single), update, and change the active state. No delete
  operation. "Deactivate to hide" means an inactive product is left out of the default list and is
  invisible to Agents; an Admin can reveal inactive products with a status filter and reactivate them.
- **Rationale**: Invoices (specs 005+) will reference products, so a product must never disappear
  from the database. The request says "deactivate to hide", which is a stronger word than customers'
  "stays listed".
- **Alternatives considered**: keep inactive products listed with a marker for everyone, as customers
  do (rejected: contradicts "hide"); hard delete (rejected: orphans invoice lines later).

## R2. Mixed-role resource endpoints (new endpoint pattern) — Confirm

- **Decision**: One `/api/products` group. The group requires a signed-in user (`RequireAuthorization()`,
  so the "every endpoint declares access" test keeps passing). The three write endpoints (`POST`,
  `PUT /{id}`, `PUT /{id}/active`) add `.RequireAuthorization(AuthorizationPolicies.AdminOnly)`; both
  requirements apply, so an Agent gets **403** and a signed-out caller gets **401**. `GET` list and
  `GET /{id}` are open to Admin and Agent.
- **Rationale**: Spec 002 has whole groups that are Admin-only (`/api/admin`) and spec 003 has a group
  open to everyone; products is the first group mixing both per operation. Authorization runs before
  body binding, so an Agent is refused with 403 even when the body is malformed and nothing is
  validated or created (FR-002).
- **Alternatives considered**: two groups with the same prefix (rejected: split registration, harder
  to see the whole resource); checking the role inside the service and returning `Forbidden`
  (rejected: duplicates the policy and can be forgotten; kept only as a fallback, see R3).

## R3. Role-dependent visibility of inactive products — Confirm

- **Decision**: The endpoint decides who may see inactive products, `user.IsInRole("Admin")` (the token's
  `role` claim is already the role claim type), and passes a plain `bool includeInactive` to
  `ProductService.SearchAsync` and `GetAsync`. Application never sees claims. For a caller who may not
  see inactive products: the list is always active-only whatever `status` they send (no error, no
  hint that inactive products exist), and `GET /{id}` of an inactive product is **404** with the same
  body as an unknown id.
- **Rationale**: FR-011 and SC-004. A 403 or a distinct message would confirm that a hidden SKU
  exists. A boolean parameter keeps the Application layer free of ASP.NET types and is trivially unit
  testable.
- **Alternatives considered**: a `ClaimsPrincipal` or role enum in the service signature (rejected:
  couples Application to auth types for one boolean); separate "admin" endpoints for inactive products
  (rejected: duplicates the list).

## R4. Status filter instead of the customers' `active` flag

- **Decision**: `status` query value `active` (default), `inactive` or `all`, parsed as text so a bad
  value is a named `status` error (400) for Admins. The parameter is named `status` on purpose, not
  `active`: customers use `active=true|false` with "omitted = both", products default to active only,
  and the same name with a different default would be a trap.
- **Rationale**: FR-011. Three states are needed and a default that hides inactive products.

## R5. Duplicate SKU must survive a race (reuses 003 R2)

- **Decision**: Same two layers as customers. `ProductService` pre-checks `SkuExistsAsync(sku, excludeId)`
  for a clean message; a **unique index** `IX_Products_Sku` is the real guarantee. `TryAddAsync` and
  `TryUpdateAsync` catch SQL errors 2601/2627 **only when the message names `IX_Products_Sku`** and return
  `false`; the service turns `false` into the same conflict `Result`. No exception crosses the layer
  boundary.
- **Rationale**: Constitution III, spec edge case "two Admins save the same SKU at once".

## R6. SKU is stored already-normalized (differs from customers) — Confirm

- **Decision**: The validator trims and upper-cases the SKU (invariant), checks the character rule, and the
  stored `Sku` column **is** the normalized value; the unique index is directly on `Sku`. There is no
  separate "as entered" column.
- **Character rule**: `A-Z`, `0-9`, `-`, `_`, 1 to 50 characters, checked on the ASCII range only
  (`char.IsLetter` would admit Arabic and other scripts, which the spec's assumption now excludes).
  Because the set is ASCII, upper-casing has no culture surprises.
- **Rationale**: FR-004 says the SKU is *saved* in upper case, so nothing "as entered" needs to survive
  (customers keep the email's casing, products do not). One column, one index, and uniqueness does not
  depend on the database collation.
- **Alternatives considered**: `Sku` + `NormalizedSku` as in customers (rejected: two columns holding the
  same value); relying on SQL Server's case-insensitive collation (rejected: implicit, breaks if the
  collation differs).

## R7. Money and VAT on the wire and in the database (first money field) — Confirm

- **Decision**:
  - `UnitPrice`: C# `decimal`, column `decimal(18,2)`, SAR. Check constraint `>= 0`.
  - `VatPercent`: C# `decimal`, column **`decimal(5,2)`**, check constraint between 0 and 100. It is a
    percentage, not money, so `decimal(18,2)` would only widen it; `decimal(5,2)` holds 0.00 to 100.00
    exactly. (This is the one reading of the "money is `decimal(18,2)`" rule to confirm.)
  - JSON: both are numbers bound to `decimal?` (System.Text.Json reads the text exactly, so `100.10`
    never passes through a `double`). The API returns them with two decimals in the number text where
    the serializer preserves scale (`100.00`); the UI formats for display and never re-rounds what it
    sends.
  - **Refuse, do not round**: the validator rejects a value whose scale exceeds two decimals
    (`value != decimal.Round(value, 2)`), so `10.005` is a `unitPrice` field error, while `100`, `100.5`,
    `100.50` and `100.500` are accepted. Range: price 0 to 999,999,999.99; VAT 0 to 100.
  - Missing `unitPrice` is a field error; missing `vatPercent` is 15 on **add** and a field error on
    **edit** (a PUT is a full replacement, and silently resetting VAT to 15 on edit would change a
    price list without anyone asking).
  - A JSON value that is not a number at all (`"abc"`) cannot bind to `decimal?`; it produces the same
    clean generic 400 as any malformed body (the Angular form cannot send it). Every other invalid
    value is a named field error.
- **Rationale**: Constitution IV and FR-007; this is the pattern invoices will copy, so getting it
  right and tested once here is cheaper than fixing it there.
- **Alternatives considered**: accept prices as strings (rejected: unusual for an API and pushes parsing
  into every client); round silently (rejected: FR-007 forbids changing a value unseen); one shared
  `decimal(18,2)` for VAT (rejected: works, but misstates what the column means).

## R8. Validation lives in Application and returns field errors (reuses 003 R4)

- **Rules (from spec FR-006)**: name required, ≤ 200; SKU per R6; unit required and exactly `Hour`,
  `Item` or `Service` (any letter case accepted, numbers such as `1` refused, as customers do for type);
  unit price per R7; VAT per R7. Error keys are camelCase: `name`, `sku`, `unit`, `unitPrice`,
  `vatPercent`. Every offending field is reported together.

## R9. Search and paging (reuses 003 R5/R6)

- **Decision**: `q` (trimmed, ≤ 100 characters, blank = none) is a contains-match with `EF.Functions.Like`
  on `Name` **or** `Sku`, with `%`, `_`, `[` and `\` escaped so they match themselves. `unit` filters
  exactly. `page` / `pageSize` as in customers (default 20, max 100, long arithmetic so a huge `page`
  gives an empty page, never an overflow). Sorted by `Name`, then `Id` for stable pages. Letter case is ignored by
  SQL Server's default collation, and the integration tests prove it (see R10).
- **Rationale**: FR-013 and SC-006 (5,000 products, first page under 1 s). Indexes: `IX_Products_Name`
  for ordering; `IX_Products_Sku` (unique) serves exact SKU lookups.

## R10. Test approach (reuses 003)

- **Decision**: Three layers, as in 003.
  - Application unit tests with a fake `IProductStore` (validation, defaults, conflict mapping,
    `includeInactive`, idempotent active, paging arguments).
  - API tests with `WebApplicationFactory` and an `InMemoryProductStore` registered in `ApiFactory`
    (status codes, `Location`, Agent 403 on all three writes including with a malformed body, 401 with no
    token, Agent's inactive-hiding, contract JSON shape, every-endpoint-declares-access test still
    green).
  - Infrastructure tests on the existing throwaway-LocalDB fixture (real migration; unique SKU index
    rejects a bypassed pre-check and reports as `false`; check constraints reject a negative price, a VAT
    of 101 and an unknown unit; `decimal(18,2)` round-trips `100.10` and `999999999.99` exactly; search
    case/wildcards/Arabic name; 5,000-product first-page timing under 1 s).
  - Angular Vitest: service, list (role-based controls, URL state), form (defaults, refusals keep values,
    409 on the SKU field), route guards on `/products/new` and `/products/:id/edit`.
- **Rationale**: FR-019. The DB-level guarantees can only be proven against SQL Server.

## R11. Angular: role-aware screens, no new shared abstraction — Confirm

- **Decision**: Same shapes as customers: lazy-loaded `products` routes under the signed-in shell; list state
  in the URL (`q`, `unit`, `status`, `page`, `pageSize`); one form component for add and edit; a
  `ProductService` owning all HTTP; `applyProblemToForm` from `core/http/problem-details.ts` for 400/409.
  New in this spec: (1) `/products/new` and `/products/:id/edit` carry the existing `roleGuard('Admin')`,
  so an Agent lands on the existing access-denied page (US1.5); (2) the list and detail hide Add, Edit and
  Deactivate/Activate and the status filter unless `AuthService.role()` is `Admin`; the API still
  enforces; (3) prices show as SAR with two decimals via Angular's decimal formatting (display only);
  (4) a unit filter and a unit label.
  The customer list is **copied and adapted**, not generalized into a shared base list component. Two
  concrete lists are not yet enough to justify an abstraction, and a shared base component would itself be
  a new architectural pattern; revisit when invoices adds a third.
- **Rationale**: Principle V and the UI rules ("before introducing a shared/base component, explain it").
- **Alternatives considered**: generic list/form base components now (rejected as above).

## R12. Migration

- **Decision**: New EF migration `AddProducts` creating `Products`, with the constraints above. Applied with
  `dotnet ef database update`. Never edited after it has been applied. No seed data (the developer adds
  products through the UI; the tests create their own).
- **Rationale**: `sql-migrations` skill; no sample products in git keeps the "no fabricated data" line
  clean and avoids collisions with real SKUs.

## Confirmations (task T001, Constitution V) — 2026-09-24

The developer confirmed all six items marked **Confirm** exactly as recommended, before any code was generated:

- R2 mixed-role `/api/products` group (reads any signed-in user, writes `AdminOnly`, 403 before the body is read).
- R3 role-dependent visibility as an `includeInactive` boolean (Agent never sees inactive; 404 by id).
- R1/R4 "deactivate to hide" with `status=active|inactive|all` for Admins (customers' "stay listed" rule not used).
- R7 money pattern, including `VatPercent` as `decimal(5,2)`; R6 SKU stored normalized, A-Z only, unique index on `Sku`.
- R11 Angular role-aware screens, customer list copied and adapted (no shared base component), optional
  `conflictField` on the shared problem-details helper.
- The spec defaults (no delete, name ≤ 200, SKU ≤ 50, price 0 allowed and max 999,999,999.99, last save wins, no seeded
  products).

## R13. Known gaps carried forward

- Invoices must offer only active products and must snapshot price and VAT into the invoice line
  (spec 005/006); this spec creates no invoices and enforces nothing there.
- Optimistic concurrency (last save wins), permanent delete, categories, price history, import/export, and a
  user-selectable sort.

## Verification log

### T030: user story 1 on the real API and database (2026-09-24)

Real `Development` API on http://localhost:5080 against the developer's LocalDB database `BillingCrm` (migration
`AddProducts` applied), signed in with the two dev accounts.

| Request | Result |
|---------|--------|
| Admin `POST` `CNS-001`, price 100, VAT 15 | `201`, `Location: /api/products/{id}`, `isActive: true` |
| Admin `GET /api/products`, `GET /{id}` | `200`; the list shows it with `unitPrice` `100.00` and `vatPercent` `15.00` |
| Admin `POST` the same SKU; `cns-001`; ` CNS-001 ` | `409` "SKU already in use" / "Another product already uses this SKU." each time |
| Admin `POST` price `10.005` | `400`, `errors.unitPrice`: "Unit price can have at most two decimal places." (not rounded, not saved) |
| Admin `POST` empty name, SKU `AB CD`, unit `Box`, price -1, VAT 101 | `400` naming `name`, `sku`, `unit`, `unitPrice`, `vatPercent` |
| Admin `POST` malformed JSON | `400` problem details, no stack trace or headers |
| Admin `POST` without VAT; with VAT `0` | saved as 15; saved as 0 |
| Admin `POST` Arabic name; price `999999999.99`; price `100.10` | stored and read back exactly (`ساعة استشارة`, `999999999.99`, `100.10`) |
| Agent `POST` (valid body, and malformed body) | `403` `Forbidden` both times; no row created |
| Agent `GET` list and `GET /{id}` | `200` |
| No token, `POST` and `GET` | `401` |
| `GET` unknown id and non-GUID id | `404` "Product not found" |
| `DELETE /api/products/{id}` | `405` (no delete exists) |

Observations: the `201` body prints `unitPrice` as `100` (the in-memory value) while later reads print `100.00`
(the stored `decimal(18,2)`); the values are equal and the UI formats prices for display, so nothing depends on the
number of zeros in the JSON. The 5 test rows created were deleted afterwards (`Products` = 0).

### T040: user story 2 on the real API and database (2026-09-24)

Same setup. Six products were created through the API (three units, an Arabic name, a `%` name, an underscore SKU);
one was made inactive directly in SQL (there is no deactivate operation until user story 3). All were deleted afterwards.

| Request | Result |
|---------|--------|
| Admin default list | 5 active products, the inactive one absent |
| Admin `status=inactive` / `status=all` / `status=nonsense` | 1 (the inactive one) / 6 / `400` |
| Agent default, `status=all`, `status=inactive` | always the same 5 active products; no error, no hint that an inactive one exists |
| Inactive product by id: Agent / Admin | `404` / `200` |
| `q=consult`, `q=CNS`, `q=cns-0` | `CNS-001` each time (name and SKU, either letter case) |
| `q=sup_`, `q=_`, `q=%25` | only `SUP_1`, only `SUP_1`, only `100% Cotton` (wildcards match literally) |
| `q=` Arabic fragment (percent-encoded UTF-8) | the Arabic-named product |
| `unit=Hour`, `unit=service`, `unit=Hour&q=cns`, `unit=Service&q=cns`, `unit=Box` | 2, 1, 1, 0 (empty, `200`), `400` |
| `pageSize=2` pages 1, 2, 3 | 2, 2, 1 items of 5, stable order (digits, then Latin, then Arabic, by name) |
| `page=2147483647`; `page=99999999999`; `pageSize=101`; `q` of 101 characters | `200` empty with total 5; `400`; `400`; `400` |

The 5,000-product timing printed by `EfProductStorePerformanceTests` (real SQL Server, after one warm-up):
name fragment 18 ms, SKU fragment 15 ms, Arabic fragment 16 ms, no text 8 ms, unit only 17 ms, status all 3 ms,
inactive only 13 ms, text + unit + status 16 ms. The limit is 1,000 ms (SC-006).

### T051: user story 3 on the real API and database (2026-09-24)

Same setup; two products created through the API (`CNS-001`, `WEB-9`), all deleted afterwards (`Products` = 0).

| Request | Result |
|---------|--------|
| Admin `PUT` name, unit, price `750.50`, VAT `0` | `200`, values saved, VAT `0` kept (not the default) |
| Admin `PUT` with its own SKU in another case and with spaces | `200`, stored as `CNS-001` |
| Admin `PUT` taking `WEB-9` / `web-9` | `409` "SKU already in use"; a `GET` afterwards shows the product unchanged |
| Admin `PUT` without `vatPercent`; with price `10.005` | `400` naming `vatPercent`; `400` naming `unitPrice` |
| Admin `PUT` unknown id; non-GUID id | `404` "Product not found" both |
| Admin `PUT /active` false | `200`; gone from the Admin default list, present with `status=inactive` and `status=all`; **gone from the Agent's list even with `status=all`**; Agent `GET` by id `404`, Admin `200` |
| Admin `POST` a new product with the deactivated SKU | `409` (the SKU stays reserved) |
| Admin `PUT /active` false again; `PUT` edit while inactive; `PUT /active` true | `200` each; product back in the Agent's list |
| Admin `PUT /active` with `{}`; unknown id | `400` naming `isActive`; `404` |
| **Agent `PUT` edit (valid body), edit (malformed body), `PUT /active`, edit of an unknown id** | **`403` `Forbidden` every time** (so nothing about existence leaks) |
| No token, edit and `/active` | `401` |
| Admin `DELETE` | `405` |
| Database after the Agent attempts | `CNS-001` still as the Admin last saved it (Consulting day, 750.50, VAT 0) |

## Review disposition (T057, 2026-09-24)

`api-reviewer`, `angular-reviewer`, `security-reviewer` and `spec-auditor` were run after the last story. None found a
blocker and the spec-auditor found nothing "Not met" (it marks SC-001/SC-008 as waiting for the developer's browser
run). Every finding, and what was done:

**Fixed**

| Finding | Reviewer | Fix |
|---------|----------|-----|
| Numeric strings (`"unitPrice":"100"`, `"vatPercent":"15"`) were accepted although the contract calls a wrong type a 400 (confirmed on the real API: `201`) | security S1 | `Program.cs`: `ConfigureHttpJsonOptions` with `JsonNumberHandling.Strict`; API tests for both strings |
| Accepted amounts echoed the scale the client sent (`100`, `100.500`) | api nit 2 | Validator returns two-decimal values (`decimal.Round(x, 2) + 0.00m`), no digit can be lost because the checks ran first; unit test |
| Search text over 100 characters (typed, pasted or in a hand-edited address) made the list an error page | angular 1 | List drops over-long `q` from the address and the box has `maxlength="100"`; two specs |
| Detail page missing `dir="ltr"` on the SKU, price and VAT | angular 2 | Added; spec |
| VAT is required on edit but nothing said so | angular 3 | `[required]` on the VAT input when editing (Material then sets `aria-required` and the marker); spec |

**Decisions needed from the developer (not changed, because they add a new pattern or touch closed specs)**

1. *Extreme decimal literals* (security S1, second half; confirmed on the real API): `1e-30` is stored as `0.00` and
   `100.000000000000000000000000000001` as `100.00`, because `System.Text.Json` collapses them **before** the
   validator sees them. The UI cannot send such values and only an Admin can post them, but FR-007 says a value is
   never changed unseen. A fix needs a small custom `decimal` JSON converter (a new shared abstraction, so Principle V
   applies) that refuses exponent notation and more than 28 significant digits.
2. *`StaffOnly` read policy* (security S3): reads use "any signed-in user"; a validly signed token with an unknown or
   missing role could read active products (never inactive). Reachable only with a token from our own key. A
   `RequireRole("Admin","Agent")` policy would fix it for products; customers have the same shape, so it fits
   spec 012-security better than a one-off here.

**Accepted or deferred (recorded, no change)**

- Database upper bound on `UnitPrice` (api nit 1): the app caps at 999,999,999.99; the check constraint only says
  `>= 0`. Adding the bound needs a new migration; deferred as defence in depth.
- Development-only leak of request headers on an unexpected exception (security S2): inherited from 002/003, the
  Development exception page. Left for spec 012-security.
- The client converts amounts with `Number(text)` and prefills with `toFixed(2)` (security N1, auditor FR-007 note):
  the text is validated first and every value with at most two decimals up to 999,999,999.99 round-trips exactly, so
  nothing is ever rounded; this is recorded as an accepted exception to "no floating point at any layer".
- Names accept control and zero-width characters (api nit 4, security N2), same as customers; SKU is ASCII-strict.
- Search case-insensitivity depends on the database collation (api nit 3); the integration tests fail loudly if it is
  ever case-sensitive.
- Concurrent edits of different fields can merge (api nit 5); accepted with "last save wins" (R13).
- Request-body size limit and rate limiting (security N3), stale roles inside a token's lifetime (N6), dev seed
  passwords (N7), `AllowedHosts` (N8): inherited, outside 004.
- Angular nits (live regions inserted filled, "No products match" while loading, saving not announced, formatter
  indentation of the form, price column alignment): identical to customers; left for a shared clean-up.
- Spec-auditor notes recorded in the spec's Assumptions: page numbers beyond a 32-bit whole number are a `400`
  (`page=2147483647` is an empty page), letter case is accepted for `unit`, and activating/deactivating moves the
  "last changed" time (the product's data is unchanged).

### Developer decisions on the open review items (2026-09-24)

- **Extreme decimal literals** (custom `decimal` JSON converter): deferred to spec 012-security by the developer. Known gap:
  a JSON price such as `1e-30` or one with more than 28 significant digits is changed by the JSON parser before the
  validator runs. The UI cannot send it and only an Admin can post it.
- **`StaffOnly` read policy**: deferred to spec 012-security by the developer, to be added once for customers and products
  together.

### Developer verification (T056, 2026-09-24)

The developer ran the quickstart checks in the browser, as Admin and as Agent (add `CNS-001` at 100 / VAT 15 and refresh,
duplicate SKU refusal with values kept, validation messages, search and filters, edit, deactivate/hide, Status filter,
activate, and the Agent's read-only view with the add and edit pages refused), and confirmed spec 004 is verified running
on their machine. Any products they created while checking are theirs and were left in their `BillingCrm` database.
