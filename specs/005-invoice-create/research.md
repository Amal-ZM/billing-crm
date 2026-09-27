# Research: Create Draft Invoices

**Feature**: 005-invoice-create | **Date**: 2026-09-24

No `NEEDS CLARIFICATION` markers remain in the spec. The items below record the decisions the plan depends on.
Items marked **Confirm** are new patterns or judgement calls that need the developer's answer before code is
generated (Constitution V) and are repeated in plan.md. Everything not marked **Confirm** reuses a pattern already
approved and verified in specs 003 and 004 (`Result` to `ProblemDetails`, `PagedResult<T>`, URL-driven list state,
one add/edit form, `problem-details.ts`, text-held amounts, `decimal?` in JSON, `JsonNumberHandling.Strict`).

## R1. An invoice is one aggregate saved as a whole (new pattern) — Confirm

- **Decision**: `Invoice` owns its `InvoiceLine`s. The API works on the whole invoice: `POST /api/invoices` creates the
  header and all lines in one request, and `PUT /api/invoices/{id}` sends the **whole desired Draft**: header fields plus
  the full list of lines. Each line in the list is either an **existing line** (`id` given, only `quantity` may change,
  its saved copy is kept) or a **new line** (`productId` given, copied from the product now). Existing lines that are
  missing from the list are removed. The whole request is validated first and saved in one transaction, so a refused
  save leaves the invoice exactly as it was (spec FR-012). There are no per-line endpoints and no `DELETE`.
- **Rationale**: The form has one Save button and the spec demands "refused save changes nothing" and "at least one
  line" across several edits at once; that is only simple and atomic when the server receives the final state. It also
  keeps the verbs at GET/POST/PUT, so CORS and the "no delete anywhere" rule stay untouched, and removing a *line* is
  not deleting an *invoice*.
- **Alternatives considered**: per-line `POST/PUT/DELETE /lines` (rejected: needs `DELETE`, is not atomic across a
  multi-edit and makes "last line" and "101 lines" rules race-prone); a JSON patch document (rejected: heavier than
  the screen needs).
- **Consequence**: two people saving the same Draft at once: the last full save wins, which the spec accepts.

## R2. Invoice numbers come from a SQL Server sequence (new pattern) — Confirm

- **Decision**: A database sequence `InvoiceNumbers` (start 1, step 1, maximum 999,999, no cycling) hands out the
  number when the invoice row is inserted. `Invoices.Number` is an `int` with a unique index, filled by
  `NEXT VALUE FOR`, and the text `INV-000123` is produced from it in one Domain function (`InvoiceNumber.Format`)
  and never stored. A save that fails after the number was drawn leaves a gap; a duplicate cannot happen.
- **Rationale**: FR-006 and SC-004: unique under simultaneous creation, gaps allowed. A sequence is atomic without
  locks or retries, unlike `MAX(Number)+1`, and the integration test can prove it with many parallel inserts against
  real SQL Server. Validation runs before the insert, so refused saves do not use a number at all.
- **Alternatives considered**: `MAX+1` with a unique index and retry (rejected: needs retry logic and still races);
  a counter row updated in a serializable transaction (rejected: serialises every creation); storing the formatted
  text (rejected: two copies of one value; sorting and later searching by number are easier on an integer).
- **After 999,999**: the sequence refuses to go higher, which surfaces as an unexpected failure. The spec puts that out
  of scope; a later migration can raise the maximum.

## R3. Line snapshots are columns, not a live join (sql-migrations skill)

- **Decision**: `InvoiceLines` has its own `ProductName`, `ProductSku`, `Unit`, `UnitPrice decimal(18,2)` and
  `VatPercent decimal(5,2)` filled when the line is created, plus `ProductId` (foreign key to `Products`, restrict) for
  reference only. Nothing joins to the live product to show a line. Editing a quantity never touches these columns.
- **Rationale**: FR-005/SC-003 and the `sql-migrations` skill ("invoice line snapshots are columns, not live product
  joins for price"). Products are never deleted, so the reference stays valid.

## R4. Quantity and money types

- **Decision**: `Quantity decimal(9,2)` with check constraints `> 0` and `<= 999999.99` (a quantity, not money, so not
  `decimal(18,2)`, same reasoning as VAT percent in 004). Unit price and VAT keep the product's exact types. JSON
  numbers bind to `decimal?`; more than two decimals is refused, never rounded (`value != decimal.Round(value, 2)`);
  the response scale is normalised to two decimals as in 004. No totals or line amounts exist in this spec (FR-013):
  nothing multiplies quantity by price here.
- **Rationale**: Constitution IV and `invoice-money`; the later totals spec needs exactly these three numbers per line.

## R5. Dates: `date` column and Saudi "today"

- **Decision**: `DueDate` is a `date` (EF Core 8 `DateOnly`). "Today" for the past-due-date rule is the calendar date at
  a fixed UTC+03:00 offset taken from `TimeProvider` (Saudi Arabia has no daylight saving), so no time-zone database is
  needed. The JSON form is `yyyy-MM-dd`, read as **text** and parsed by the validator so a bad date is a named `dueDate`
  error rather than a generic 400. An unchanged due date on a Draft is always accepted.
- **Alternatives considered**: `TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh")` (rejected: identifier differs between
  Windows and Linux); server local date (rejected: depends on the machine's clock zone).

## R6. Ids arrive as text; every refusal is a field error

- **Decision**: `customerId`, `productId` and line `id` are strings in the request and are parsed by the validator, so a
  malformed id is `customerId` / `lines[2].productId` "is not valid" (400 with the field named) instead of a generic
  400. A missing, unknown or inactive customer or product is also a 400 field error, never a 404 or 409: "choose an active
  customer" is something the person fixes on the form, and it does not reveal which ids exist. Error keys use the array
  path: `lines[0].quantity`, `lines[1].productId`, `lines[0].id`. The service collects **all** problems (shape, customer,
  products) and reports them together.
- **Rationale**: FR-003/FR-004/FR-012 ("name the field or line at fault"); matches `ProblemDetails` field errors from 003/004.

## R7. Rules for an existing Draft (spec FR-011)

- **Decision**:
  - Keeping the same customer is always allowed, even if it became inactive; a **different** customer must be active.
  - An existing line (identified by `id`) can only change `quantity`; its product cannot be swapped (remove it and add a
    new line instead). An `id` that is not a line of this invoice, or used twice, is a `lines[i].id` error.
  - A new line's product must be active **now**; an existing line whose product is inactive is kept as is.
  - A line list with 0 lines, or more than 100, is a `lines` error (so removing the last line is refused).
  - If nothing actually changed, nothing is written and `UpdatedAtUtc` does not move.
- **Race**: a product deactivated between the check and the save is accepted (a window of milliseconds); no database
  constraint can express "product must be active".

## R8. Persistence shape and EF specifics

- **Decision**: `Invoice` and `InvoiceLine` are two tables. The line key is generated by the application
  (`Guid.NewGuid()`) and configured `ValueGeneratedNever()`, so a new line added to a tracked invoice's collection is
  inserted (not mistaken for an existing row); lines removed from the collection are deleted (orphan delete, cascade
  relationship). `Position` (order added, never renumbered) orders the lines; its index is **not** unique, because delete
  and insert in one save could otherwise collide. Foreign keys to `Customers` and `Products` are `Restrict`; the line's
  key to its invoice cascades (no invoice delete exists). The integration tests prove insert, replace, orphan delete and
  the sequence against real SQL Server.

## R9. Port shape: no `Try*`

- **Decision**: `IInvoiceStore` has `FindByIdAsync(id)` (invoice with lines, tracked), `AddAsync(invoice)` (fills the
  number), `SaveChangesAsync()` (for an edited tracked invoice; returns `false` when another edit got there first, see R16) and `SearchAsync(page, pageSize)` returning
  `PagedResult<InvoiceListRow>` (customer name joined in the query; the service maps rows to `InvoiceSummary`). There is no `Try*`: no user input can violate a
  unique index here (the number is not chosen by the user). Customer and product lookups reuse the existing
  `ICustomerStore.FindByIdAsync` and `IProductStore.FindByIdAsync`; those closed ports are not changed.

## R10. List and details

- **Decision**: List ordered `CreatedAtUtc` descending then `Number` descending, with a supporting index; page defaults 20,
  maximum 100, long-arithmetic offset, a huge page number gives an empty page with the real total (same as 003/004); no
  search or filter. Details load the invoice with its lines and the customer by id through the existing customer port
  (current name and active flag), lines ordered by `Position`.

## R11. Who may do what

- **Decision**: One group `/api/invoices` with `RequireAuthorization()`; Admin and Agent have the same access, as customers.
  No new policy. (The role split for invoices, if wanted later, is a spec change.)

## R12. Angular structure — Confirm

- **Decision**: Lazy `invoices` routes under the signed-in shell: `/invoices`, `/invoices/new`, `/invoices/:id`,
  `/invoices/:id/edit`. One form component for add and edit with a `FormArray` of lines. Two small **picker components**
  (customer, product) built on a Material autocomplete that call the **existing** list APIs (`GET /api/customers?active=true&q=`
  and `GET /api/products?q=`, which already return active products only for both roles), debounced, showing name and SKU.
  Quantity is held as text and checked as text through a new small pure helper `core/money/decimal-text.ts`; the product
  form is **not** refactored to use it in this spec. `applyProblemToForm` gains bracket-path support so `lines[0].quantity`
  reaches the matching control (customer/product behaviour unchanged, proven by their existing specs).
- **Rationale**: Principle V and the UI rules (a shared component and a new form structure are new architectural shapes).
  No new endpoints are needed for the pickers.
- **Alternatives considered**: `<select>` with every customer/product loaded (rejected: unbounded at 5,000 products); a new
  "lookup" endpoint family (rejected: the list APIs already do it).

## R13. Test approach (reuses 003/004)

- Application unit tests with fake stores (validator: quantity/date/notes/lines/ids; service: create, update rules,
  snapshots, inactive/missing customer and product, unchanged edits, all errors reported together).
- API tests with `WebApplicationFactory` and an `InMemoryInvoiceStore` in `ApiFactory` next to the existing in-memory
  customer and product stores (contract, numbers, snapshots surviving a product change through the real service,
  refusals, list paging, 401).
- Infrastructure tests on the throwaway-LocalDB fixture: the sequence under **parallel** inserts, unique number index,
  snapshot columns and exact decimals round-trip, orphan delete and new-line insert on a tracked invoice, foreign keys,
  check constraints, list order and paging, and a 5,000-invoice timing under 1 second.
- Angular Vitest: service, list, detail, form (lines, pickers, refusals keep values, bracket-path errors), routes.

## R14. Migration

- **Decision**: One migration `AddInvoices`: sequence `InvoiceNumbers`, tables `Invoices` and `InvoiceLines` with the
  constraints in data-model.md. Applied with `dotnet ef database update`; never edited afterwards. No seed data.

## R15. Known gaps carried forward

- Totals, issuing, other statuses, cancel/delete, payments, PDF/email, list search and filters (later specs).
- The Draft-only status check constraint is widened by the migration of the spec that adds a status.
- A row-version column for optimistic concurrency (a clash on the same rows is a 409, see R16; other edits are last-save-wins), numbers above 999,999, per-invoice currency, line discounts.
- Items still open from spec 004 and deferred to 012-security (decimal JSON converter for extreme literals, a `StaffOnly`
  policy, Development-only header leak) apply to this API as well.

## Confirmations (task T001, Constitution V) — 2026-09-24

The developer confirmed all four groups exactly as recommended, before any code was generated:

- R1 the whole-Draft `PUT /api/invoices/{id}` (kept lines by `id` with quantity only, new lines by `productId`, omitted lines
  removed, one atomic save, no per-line endpoints, no `DELETE`) and R6 every refusal as a field error with array paths.
- R2 invoice numbers from the SQL sequence `InvoiceNumbers` (`int` column, unique index, `INV-` + six digits, gaps allowed);
  R3 snapshot columns plus a product reference; R4 quantity as `decimal(9,2)`; R5 `date` column and "today" at a fixed UTC+03:00.
- R12 the Angular pieces: customer and product pickers on the existing list APIs, `core/money/decimal-text.ts`, bracket paths in
  `applyProblemToForm` (products and customers not refactored or changed).
- The spec defaults (Admin and Agent both allowed, at least one line and at most 100, quantity 0.01-999,999.99, notes up to 1,000
  characters, the same product twice allowed, no past due date on creation, last save wins, no seed data, no totals anywhere).

## Verification log

### T033: user story 1 against real SQL Server (2026-09-24)

The system stopped the API and `ng serve` earlier because memory was critically low and asked that they not be restarted
unprompted, so the create path was verified **without a running API**: `InvoiceServiceRealDatabaseTests` wires the same
`InvoiceService` the API uses to the real EF stores on a throwaway LocalDB database, with a fresh context per call like one
request each. A live curl run against the API is left for the final browser verification (T060).

| Check (real SQL Server) | Result |
|-------------------------|--------|
| Customer ABC + a product, quantity 2 | `Draft`, number `INV-` + six digits, one line of quantity 2 with the product's price 100.00 and VAT 15.00; read back from a **new** context with the same values, notes and SKU |
| Product renamed, repriced and deactivated through the real `ProductService` | The saved line still shows the old name, SKU, unit, price and VAT; a new invoice for that product is now refused on `lines[0].productId` |
| Inactive customer + inactive product + quantity 0 + past due date in one request | `Validation` failure naming `customerId`, `dueDate`, `lines[0].productId`, `lines[0].quantity`; the `Invoices` row count did not change |
| Consecutive creates around a refused one | Numbers strictly increase; the refused save did not disturb the sequence |
| Unknown id | `NotFound` |
| Fifty invoices added in parallel from separate contexts (`EfInvoiceStoreCreateTests`) | Fifty different numbers, none refused |
| Constraints written directly in SQL | Quantity 0 / negative / 1,000,000, negative price, VAT 101, unit `Box`, status `Paid`: all rejected (547); unknown customer or product rejected; a used customer or product cannot be deleted; duplicate number rejected (2601, `IX_Invoices_Number`) |
| Product row changed directly in SQL after the invoice was saved | The line's saved columns are unchanged |

### T044: user story 2 (editing a Draft) against real SQL Server (2026-09-24)

Verified the same way as T033 (no running API; the servers stay stopped): `InvoiceServiceRealDatabaseTests` drives the same
`InvoiceService.UpdateAsync` the API's `PUT` calls, over the real EF stores, a fresh context per call, and
`EfInvoiceStoreUpdateTests` proves the persistence of an edited aggregate on real SQL Server.

| Check (real SQL Server) | Result |
|-------------------------|--------|
| One save: change a quantity, add a line, remove a line, change due date, notes and customer | All persisted; number and created time unchanged, changed time moved; the kept line keeps its saved price |
| Product repriced through the real `ProductService`, then a save that keeps the old line and adds the same product again | Kept line still 100.00 / 15%; the new line 150.00 / 0% |
| Refused edit (inactive customer + past date + inactive product + quantity 0) | Four named errors together (`customerId`, `dueDate`, `lines[1].productId`, `lines[1].quantity`); header, lines and the changed time in the database are exactly as before |
| Remove one of two lines; then try to remove the last | First saved; the last is refused on `lines` and the line is still there |
| Product and customer deactivated after the Draft was made | Existing line kept and changed; a new line for the inactive product refused; a new line for an active product saved; customer shown inactive |
| Save with nothing changed; unknown invoice | Changed time untouched; `NotFound` |
| Store: new line added to a loaded invoice, removed line, kept line's quantity, header change, all in one `SaveChanges` | Inserted, deleted, updated, saved together; removing the last-position line and adding a new one in the same save does not collide (the position index is not unique) |
| Store: a save that breaks a constraint (header changed + a line with quantity 0) | `DbUpdateException`; nothing at all was saved (notes, changed time and line count as before) |

### T054: user story 3 (the invoice list) against real SQL Server (2026-09-24)

Verified without a running API, as for T033 and T044. `EfInvoiceStoreSearchTests` and `EfInvoiceStorePerformanceTests` run the real
store on a throwaway LocalDB database; the API's list behaviour (order, paging, bad values, both roles, no token) is covered by
`InvoiceListEndpointTests` through the real service. A live curl run moves to the final browser verification (T060).

| Check | Result |
|-------|--------|
| Order | Newest first by created time, then by number descending; four invoices (two at the same moment) came back in exactly that order |
| Row content | Number, status, customer id and the customer's **current** name (Arabic intact, and a rename via SQL shows up), due date, created time; no lines, no totals |
| Paging | 7 invoices at one moment, pages of 3: no duplicates, none skipped, totals add up |
| Page beyond the end (`int.MaxValue`, 1,000,000) | Empty page with the real total, no error |
| Bad `page` / `pageSize` (0, non-numbers, > int, 101) | `400` naming the field |

The 5,000-invoice timing printed by `EfInvoiceStorePerformanceTests` (300 customers, one line each, real SQL Server, after one
warm-up): first page of 20 **3 ms**, first page of 100 **5 ms**, second page **6 ms**, a middle page **8 ms**, page 50 of 100
**14 ms**. The limit is 1,000 ms (SC-006).

## R16. Two people editing the same Draft (added by the review, 2026-09-24)

- **Decision**: `IInvoiceStore.SaveChangesAsync` returns `bool`. The EF store catches `DbUpdateConcurrencyException` (EF reports it when
  an UPDATE or DELETE touches a row that another save already deleted) and returns `false`; the service answers
  `Error.Conflict("invoice_changed", ...)`, which is a `409` `ProblemDetails`. Everything else still throws.
- **Why**: a lost race between two staff members is an expected business outcome (Constitution: expected errors are not exceptions),
  and before this it would have been a `500`.
- **Not done**: a `rowversion` column. A clash on different rows is still last-save-wins; the spec never asked for more, and a
  version column would be a change of the data model for a rare case. Recorded in R15.

## Review disposition (2026-09-24)

Three read-only reviews ran after the implementation (`api-reviewer`, `angular-reviewer`, `spec-auditor`). No blockers. Every finding
is fixed or explicitly recorded here.

### api-reviewer

| Finding | Disposition |
|---------|-------------|
| S1: a concurrent edit throws `DbUpdateConcurrencyException`, a `500` | **Fixed** (R16). Tests: service (`A_save_that_lost_a_race...`), API (`An_edit_that_lost_a_race...`, 409, title and detail), real SQL with two contexts (`A_save_that_lost_a_race_with_another_edit_reports_false_and_writes_nothing`) |
| S2: the number sequence is cached, so numbers can jump after a LocalDB restart | **Fixed**: migration `NoCacheInvoiceNumbers` (`ALTER SEQUENCE ... NO CACHE`), applied to the developer database. Smoke test checks `is_cached = 0`. A rolled-back save can still leave a gap; accepted and written in data-model.md |
| `Invoice.AddLine` comment claimed positions never repeat | **Fixed**: the comment now says the new line goes after the highest position that remains |
| `InvoiceLine.CreateFrom` / `SetQuantity` public although only the aggregate may call them | **Fixed**: both `internal` |

### angular-reviewer

| Finding | Disposition |
|---------|-------------|
| A refusal's messages stay on the controls, keep the form invalid and block a retry (`lines[i].id` errors are sticky) | **Fixed**: `clearServerErrors` (core/http/problem-details) runs at the start of every save and refreshes the parents' status. Specs: helper (keeps a real validation error), form (a second save goes through) |
| Picker inputs never got `aria-invalid`, so focus after a refusal skipped them | **Fixed**: both pickers take `invalid` and `errorId`; the error text has that id and `tabindex="-1"`. `focusFirstInvalid` falls back to the first message (`.field-error`, top `form-error`), which is what a 409 uses. Spec: a 409 moves focus to the message |
| Lines had no accessible name | **Fixed**: each line is `role="group"` `aria-label="Line N"`; Remove is `aria-label="Remove line N"` |
| Focus was lost after Remove | **Fixed**: focus goes to Add line |
| Cancel while saving hides the outcome | **Fixed differently**: the save request is no longer cancelled when the page is left (cancelling on the client does not stop the server), and its answer is ignored. Spec covers it |
| `lines[-1]` style keys | **Fixed**: only non-negative whole-number brackets resolve; anything else goes to the top message. Spec added |
| A refusal on `lines[i].productId` of a saved line was invisible | **Fixed**: `lineIdError` also shows the product message |
| Product picker relied on the API's default status and carried dead `displayName`/`externalId` code | **Fixed**: it asks for `status: 'active'` explicitly and the dead code is removed (spec expectation updated) |

### spec-auditor

| Finding | Disposition |
|---------|-------------|
| data-model.md drift (method names, `IX_InvoiceLines_ProductId`, the decimals check, `SearchAsync` row type) | **Fixed** in data-model.md and R9 |
| Contract product message wording | **Fixed** ("This product is inactive or does not exist. Choose an active product.") |
| `PUT` with an unreadable body is a `400` before the `404` | **Recorded** in the contract: only a body that cannot be read at all; a readable body for an unknown id is `404` first |
| "An invoice needs at least one line." vs the spec's "a Draft needs at least one line" | **Accepted**: same rule, and every invoice is a Draft in this spec. The message will be revisited when a second status exists |
| Missing tests: first number `INV-000001` on a fresh sequence; 100 lines and a 1,000-character note on real SQL; `PUT` with 101 lines | **Added**: `EfInvoiceStoreFirstNumberTests` (own database, one test), `An_invoice_at_the_limits_...`, `An_edit_with_more_than_100_lines_is_refused_and_exactly_100_is_accepted` |
| Live-API run | **Open, by design**: T060, done by the developer |
| Extreme JSON decimal literals | **Deferred to spec 012** (already listed in R15) |

Test status after the fixes: Application 416, API 375, Infrastructure 149, Angular 466, all passing; `ng build` clean.

## Developer verification (T060)

2026-09-24: the developer ran `quickstart.md` on their machine (API and Angular running against LocalDB) and confirmed "Yes, 005 verified". Spec 005 is Done.
