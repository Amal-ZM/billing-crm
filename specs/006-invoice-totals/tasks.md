---

description: "Task list for 006-invoice-totals"
---

# Tasks: Invoice Totals

**Input**: Design documents from `/specs/006-invoice-totals/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/invoice-totals-api.md, quickstart.md

**Tests**: Included. FR-014 requires automated tests, and the acceptance checks in contracts/invoice-totals-api.md map to test
tasks below. Write each phase's tests first and confirm they fail (a test that does not compile yet counts as failing). The
calculator is proven with **exact decimals**, never by comparing approximate numbers.

**Organization**: Grouped by user story (US1 see the amounts on a Draft, US2 give a discount on the whole invoice, US3 amounts
stay true when products change). The calculator, the stored `Discount` and the client models are Foundational because all three
stories need them. **US1 ends at the amounts on the details page**; the Discount field and its rules arrive with US2 (Phase 4).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run from the repository root unless
a task says otherwise; run frontend commands from PowerShell (`npx --yes npm@11 exec -- ng test --watch=false`). Stop the API
and `ng serve` before `dotnet build`, `dotnet test` or `dotnet ef`; **do not start them again unless the developer asks**.

**Rules that apply to every task**: use the `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations` and
**`invoice-money`** skills for matching edits; expected failures (bad discount, discount above the net, too-large amount) are
`Result` -> `ProblemDetails`, never exceptions; **every amount is `decimal`, never `float`/`double`, at every layer**; a value
typed with more than two decimals is refused, never rounded; net and VAT are rounded half away from zero **once per line** and
nothing is rounded twice; **the Angular code shows the amounts the API returns and never calculates one** (no multiplication,
sums, percentages or rounding of money in a component or template); amounts are calculated from the saved lines and never
stored except the discount; a line's saved name, SKU, unit, unit price and VAT percent are never rewritten; no connection
string, key or credential in any committed file; every endpoint keeps its explicit access rule; do not change customer, product
or invoice-list behaviour (003, 004 and the list of 005 are closed); Arabic and English text must round-trip unchanged.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the new items in plan.md "Patterns and decisions to confirm": (1) the pure Domain calculator `InvoiceMoney` as the one place the rules live, (2) amounts calculated on read with only `Discount` stored (and the note that a later "issue invoice" spec must freeze them), (3) rounding half away from zero once per line and the discount shared in whole halalas by largest remainder (ties to the earlier line), (4) `discount` as an optional `decimal?` in the whole-Draft body where leaving it out clears it, (5) the prospective check before any change, (6) the amount ceiling 9,999,999,999,999.99, (7) the response shape (`discount`, `totals`, per-line `net`, `discountShare`, `vat` as JSON numbers with two decimals), (8) the Angular pieces (details totals block, one optional Discount field, no live preview). If (1) or (2) is rejected, stop and re-plan before any code; if (6) changes, adjust T004, T023, T024 and the contract. Record the answers in `specs/006-invoice-totals/research.md` (a "Confirmations" section like 005's)
- [X] T002 Baseline check before any change: stop the API and `ng serve` if running, then `dotnet test backend/BillingCrm.sln` and (from PowerShell, in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` pass, `sqllocaldb i` lists `MSSQLLocalDB`, and `dotnet ef migrations list --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api` shows `NoCacheInvoiceNumbers` as the last applied migration

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The calculator, the stored discount, the migration and the client models.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 [P] Write `backend/tests/BillingCrm.Application.Tests/InvoiceMoneyTests.cs` first (exact `decimal` asserts, no tolerance): every worked example A to I in data-model.md (A 2 x 100.00 @ 15% gives net 200.00, VAT 30.00, grand 230.00; B mixed VAT 280.00; C 1.25 x 10.55 gives net 13.19, VAT 1.98, grand 15.17; D 0.5 x 0.05 gives net 0.03 and VAT 0.00; E discount 20.00 gives VAT 27.00 and grand 207.00; F shares 20.00 and 10.00 with VAT 12.00 and grand 132.00; G three lines of 10.00 and discount 0.01 give shares 0.01, 0.00, 0.00 and grand 34.49; H discount equal to the net gives zero after discount; I a zero-price line); the rounding pins (0.025 to 0.03, 13.1875 to 13.19, 1.9785 to 1.98, 0.0045 to 0.00, 1.4985 to 1.50); no lines gives all zeros; a discount above the total net or a negative discount throws `ArgumentOutOfRangeException` (it is a programming error: the service refuses those first); the ceiling constant equals `9_999_999_999_999.99m`; and invariants over a few thousand invoices from a fixed-seed generator (`new Random(20260924)`, 1 to 100 lines, quantities and prices with two decimals, VAT 0 to 100 with two decimals, a discount from 0 to the total net): shares sum to the discount exactly, no share is negative or above its line net, total net = sum of line nets, total VAT = sum of line VATs, grand = net after discount + total VAT, net after discount = total net - discount, every amount has at most two decimals, the same input twice gives the same output. They fail until T004
- [X] T004 Add `backend/src/BillingCrm.Domain/InvoiceMoney.cs` per research R1, R3, R4 and data-model.md: `public static class InvoiceMoney` with `MaxAmount = 9_999_999_999_999.99m`, records `AmountLine(decimal Quantity, decimal UnitPrice, decimal VatPercent)`, `LineAmounts(decimal Net, decimal DiscountShare, decimal Vat)` and `InvoiceAmounts(IReadOnlyList<LineAmounts> Lines, decimal TotalNet, decimal Discount, decimal NetAfterDiscount, decimal TotalVat, decimal GrandTotal)`, and `Calculate(IReadOnlyList<AmountLine> lines, decimal discount)`: line net `Round(q * p, 2, MidpointRounding.AwayFromZero)`; discount shared in whole halalas with `System.Numerics.BigInteger` (floor of `D * n_i / N`, leftover halalas one each to the largest remainders, ties to the earlier line, all zero when `N` is 0); line VAT `Round((net - share) * vat / 100, 2, MidpointRounding.AwayFromZero)`; totals are sums; every returned amount normalised to two decimals (`+ 0.00m` after rounding so `200` is `200.00`). Guard with `ArgumentOutOfRangeException` for a negative discount or one above the total net. Document the `invoice-money` rule ("header discount on net, then VAT on the discounted net") and the rounding choice in the class comment. No `float`/`double` anywhere (depends on T003)
- [X] T005 Add the stored discount to the aggregate in `backend/src/BillingCrm.Domain/Invoice.cs`, tests first in `backend/tests/BillingCrm.Application.Tests/InvoiceAmountsTests.cs` (new): `Discount` (`decimal`, private setter, `0` by default); `Create(customerId, dueDate, notes, nowUtc, discount = 0m)`; `UpdateDetails(customerId, dueDate, notes, discount, nowUtc)` (returns true and moves `UpdatedAtUtc` only when something changed, the discount included); and `Amounts`, which returns `InvoiceMoney.Calculate` over `Lines` (position order) using each line's **saved** quantity, unit price and VAT percent and `Discount`. Update every existing caller of `UpdateDetails` (search `UpdateDetails(` in `backend/src` and `backend/tests`, including `InvoiceService.cs`, `EfInvoiceStoreUpdateTests.cs` and the fakes) so the solution builds. Tests: a Draft of 2 x 100.00 @ 15% reports 200.00 / 30.00 / 230.00; after the source product object is changed the amounts are the same; `UpdateDetails` with the same values including the discount returns false and leaves `UpdatedAtUtc`; a changed discount returns true (depends on T004)
- [X] T006 Add the column and migration: in `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs` map `Discount` as `decimal(18,2)`, required, default `0`, with check constraint `CK_Invoices_Discount` (`[Discount] >= 0`); then `dotnet ef migrations add AddInvoiceDiscount --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api --output-dir Persistence/Migrations`. Read the generated file and confirm it adds the column `NOT NULL` with `defaultValue: 0m` (so existing invoices get 0) and the check constraint and touches nothing else (no line columns); never edit it after it is applied. Extend `backend/tests/BillingCrm.Infrastructure.Tests/MigrationSmokeTests.cs`: `Invoices.Discount` is `decimal` precision 18 scale 2 not null with default 0, `CK_Invoices_Discount` exists, and `InvoiceLines` has no column named like `Net`, `Vat`, `Total` or `Amount`. Apply it to the developer's database with `dotnet ef database update` (API stopped) (depends on T005)
- [X] T007 [P] Extend `frontend/src/app/core/invoices/invoice.models.ts` per the contract: `InvoiceLine` gains `net`, `discountShare`, `vat` (numbers); a new `InvoiceTotals` (`totalNet`, `discount`, `netAfterDiscount`, `totalVat`, `grandTotal`); `Invoice` gains `discount` and `totals`; `InvoiceInput` gains `discount: number | null`. Update every spec fixture that builds an `Invoice`, `InvoiceLine` or `InvoiceInput` so `ng test` still compiles (`invoice.service.spec.ts`, `invoice.service.update.spec.ts`, `invoice-form.component.spec.ts`, `invoice-form-edit.component.spec.ts`, `invoice-detail.component.spec.ts`, the picker specs if they build invoices), with the amounts of example A
- [X] T008 Run `dotnet build backend/BillingCrm.sln`, `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; everything compiles and passes, including the new calculator tests (depends on T003-T007)

**Checkpoint**: the calculator is proven, `Invoice.Amounts` works, the column exists on the developer's database, and no existing behaviour changed.

---

## Phase 3: User Story 1 - See the amounts on a Draft invoice (Priority: P1)

**Goal**: Every invoice response carries line net and VAT and the invoice totals, and the details page shows them, for new invoices and for invoices made in 005.

**Independent Test**: Create a Draft for ABC with `CNS-001` (100.00, 15%) quantity 2: the line shows net 200.00 and VAT 30.00 and the invoice shows total net 200.00, total VAT 30.00, grand total 230.00 SAR; reopen it and see the same.

### Tests for User Story 1 (write first; they fail until the implementation)

- [X] T009 [P] [US1] Add to `backend/tests/BillingCrm.Application.Tests/InvoiceServiceCreateTests.cs` and `InvoiceServiceUpdateTests.cs`: create for the acceptance case returns totals 200.00 / 0.00 / 200.00 / 30.00 / 230.00 and the line's net 200.00, discount share 0.00, VAT 30.00; two lines at 15% and 0% (280.00); the rounding case C and the zero-price case; `GetAsync` returns the same amounts; an edit that changes a quantity returns recalculated amounts from the saved price; the DTO invariants (sum of line nets = total net, sum of VATs = total VAT, grand = net after discount + VAT)
- [X] T010 [P] [US1] Add `backend/tests/BillingCrm.Api.Tests/InvoiceTotalsEndpointTests.cs`: `POST`, `GET` and `PUT` bodies contain `discount`, `totals` (five fields) and per-line `net`, `discountShare`, `vat`; the acceptance case gives exactly 200.00, 30.00 and 230.00, and the raw JSON text shows two decimals (`GetRawText()` contains `"grandTotal":230.00`); the list body is unchanged (no amounts); either role; no token is 401 on `GET /api/invoices/{id}`
- [X] T011 [P] [US1] Add `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceAmountsTests.cs` (own `LocalDbFixture`): an invoice row and its lines inserted with raw SQL **the way 005 did** (no `Discount` in the insert) reads back through `EfInvoiceStore.FindByIdAsync` with `Discount` 0 and `Amounts` correct (a 005 Draft shows amounts with no backfill: SC-006); `Discount` defaults to 0 for a normally added invoice
- [X] T012 [P] [US1] Add to `frontend/src/app/features/invoices/invoice-detail.component.spec.ts`: the totals block shows total net, total VAT and grand total with two decimals and `SAR`, taken straight from the response (the fixture uses numbers that differ from any client calculation, for example a grand total that is not net + VAT, to prove the screen does not calculate); each line shows its net and VAT; the discount rows are absent when the discount is 0 and present when it is not; no total appears while loading or on error

### Implementation for User Story 1

- [X] T013 [US1] In `backend/src/BillingCrm.Application/Invoices/InvoiceModels.cs` add `InvoiceTotalsDto(decimal TotalNet, decimal Discount, decimal NetAfterDiscount, decimal TotalVat, decimal GrandTotal)`; add `Net`, `DiscountShare`, `Vat` to `InvoiceLineDto` and `Discount` and `Totals` to `InvoiceDto`; `InvoiceDto.From(invoice, customer)` calls `invoice.Amounts` **once** and pairs each `LineAmounts` with its line by position order. No route, service or endpoint changes are needed: create, edit and get already return `InvoiceDto` (depends on T005)
- [X] T014 [US1] Update `frontend/src/app/features/invoices/invoice-detail.component.ts`: a totals block (a `dl` with total net, discount and net after discount only when the discount is not 0, total VAT, grand total, each `number:'1.2-2'` with `SAR`, `dir="ltr"`) and per-line net and VAT (and discount share only when the invoice has a discount) in the existing lines table. Display only: no arithmetic on any amount, no `reduce`, no `toFixed`. Keep the loading, error and not-found states and the Edit and Back links unchanged
- [X] T015 [US1] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; T009-T012 pass (depends on T013, T014)

**Checkpoint**: US1 acceptance scenarios 1-8 pass via tests; the browser check is in the quickstart.

---

## Phase 4: User Story 2 - Give a discount on the whole invoice (Priority: P2)

**Goal**: A Draft can carry an optional discount in SAR that is shared over its lines before VAT; bad or impossible discounts and too-large amounts are refused with nothing changed.

**Independent Test**: On the 230.00 Draft enter a discount of 20.00 and save: 200.00 / 20.00 / 180.00 / 27.00 / 207.00; clear it and save: back to 230.00.

### Tests for User Story 2 (write first)

- [X] T016 [P] [US2] Extend `backend/tests/BillingCrm.Application.Tests/InvoiceValidatorTests.cs`: `discount` null, 0, 20.00, 20.1 and 20.100 are accepted (and normalised to two decimals); -0.01, 20.001 and a value above `9_999_999_999_999.99m` are refused on key `discount` with the contract's messages; `Inspection` and `ValidInvoice` carry the discount; a bad discount is reported together with other errors
- [X] T017 [P] [US2] Extend `InvoiceServiceCreateTests.cs` and `InvoiceServiceUpdateTests.cs`: create with discount 20.00 gives 200.00 / 20.00 / 180.00 / 27.00 / 207.00; a discount equal to the total net is accepted (all zero after); a discount above the total net on create and on edit is refused on `discount` with nothing saved (`SaveChangesCalls` 0 and the aggregate snapshot unchanged); an edit that removes lines so the saved discount now exceeds the net is refused on `discount`; leaving the discount out on edit clears a saved one; the same lines and the same discount save nothing (`UpdateDetails` returns false); a line net above the ceiling (quantity 999,999.99 of a product priced 999,999,999.99) is refused on `lines[0].quantity`; enough large lines to pass the ceiling in total are refused on `lines`; when another error is already present (for example an inactive product) the discount-above-net, line-size and total-size checks are skipped and only the other errors are reported; the discount stays as saved when only quantities change
- [X] T018 [P] [US2] Extend `backend/tests/BillingCrm.Api.Tests/InvoiceTotalsEndpointTests.cs`: `POST` with `"discount":20.00` gives the E amounts and `discountShare` 20.00; `PUT` without `discount` after one was saved gives `discount` 0.00; `discount` -1, 20.001 and 500 (above the net) are `400` on `errors.discount` and change nothing (a `GET` afterwards is identical); `"discount":"20"` (a string) is the generic `400`; a huge quantity is `400` on `lines[0].quantity`; the refusals are `ProblemDetails`, never `500`
- [X] T019 [P] [US2] Extend `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceAmountsTests.cs` (or `EfInvoiceStoreUpdateTests.cs`): a discount of 20.10 saved through `EfInvoiceStore` reads back as exactly 20.10 with `Amounts` consistent; a direct SQL update to a negative discount is rejected by `CK_Invoices_Discount` (error 547); `InvoiceServiceRealDatabaseTests.cs` gains one create-with-discount and one edit-removing-lines-below-the-discount refusal on real SQL
- [X] T020 [P] [US2] Extend `frontend/src/app/features/invoices/invoice-form.component.spec.ts` and `invoice-form-edit.component.spec.ts`: the Discount field is optional (empty sends `discount: null`); a typed `20.00` is sent as the number 20; `-1`, `20.001` and `abc` show a message on the field and send nothing; a server `400` on `discount` is shown on the Discount field with every other value kept; a server `400` on `lines` shows above the lines; the edit form shows a saved discount in the field (and an empty field when the saved discount is 0) and sends it back unchanged; clearing it sends `null`; the field is `dir="ltr"` with `inputmode="decimal"`; the form performs no calculation (no total appears on it)

### Implementation for User Story 2

- [X] T021 [US2] In `backend/src/BillingCrm.Application/Invoices/InvoiceModels.cs` add the last parameter `decimal? Discount = null` to `InvoiceInput` (existing callers keep compiling). Document that `PUT` describes the whole Draft, so an omitted discount clears a saved one
- [X] T022 [US2] In `backend/src/BillingCrm.Application/Invoices/InvoiceValidator.cs` add `MaxDiscount = InvoiceMoney.MaxAmount` handling: `ParseDiscount` (null is `0.00m`; below zero or a value that differs from `decimal.Round(value, 2)` is refused on `discount` with "Discount must be a number of 0 or more with at most two decimal places."; above the ceiling with "Discount cannot be more than 9,999,999,999,999.99."; the accepted value normalised with `decimal.Round(value, 2) + 0.00m` like quantity); add `Discount` to `Inspection` and `ValidInvoice`; keep every message as fixed text (no culture formatting) (depends on T021)
- [X] T023 [US2] In `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs` add the **prospective check** of research R6 for create and update, after the customer and products are resolved and only when `errors` is empty: build the `AmountLine` list the save would produce (existing line: its saved unit price and VAT with the requested quantity; new line: the product's current price and VAT; all new on create), call `InvoiceMoney.Calculate` with the discount clamped for the check (compare the discount with the total net first so `Calculate` never throws), and refuse with `Error.Validation`: a line net above `MaxAmount` on `lines[i].quantity` ("The amount of this line is too large. Reduce the quantity."), total net or grand total above `MaxAmount` on `lines` ("The invoice total is too large. Reduce the quantities or remove lines."), the discount above the total net on `discount` ("Discount cannot be more than the total net of the lines."). Only then change the aggregate: `Invoice.Create(..., discount)` and `UpdateDetails(..., discount, now)` so `changed` includes the discount. Expose the three messages as public constants next to the existing ones (depends on T021, T022)
- [X] T024 [US2] Update `frontend/src/app/features/invoices/invoice-form.component.ts`: an optional **Discount (SAR)** `mat-form-field` between the notes and the lines, held as text (`discount: new FormControl('', { nonNullable: true, validators: [discountText] })` where `discountText` is empty-or-`decimalTextProblem(text, { max: 9999999999999.99, allowZero: true })`), `discountError()` with the messages of the contract (server message first via `serverMessage`), `dir="ltr"`, `inputmode="decimal"`, `autocomplete="off"`; on load put `String(invoice.discount)` in the field only when it is not 0; `submit()` sends `discount: text.trim() === '' ? null : decimalTextToNumber(text)`; `applyProblemToForm` already maps `discount` and `lines`; `clearServerErrors` and `focusFirstInvalid` cover the new control. No live total and no calculation anywhere in the form (depends on T007)
- [X] T025 [US2] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; T016-T020 pass (depends on T021-T024)

**Checkpoint**: US2 acceptance scenarios 1-7 pass via tests; the browser check is in the quickstart.

---

## Phase 5: User Story 3 - Amounts stay true when products change (Priority: P3)

**Goal**: Prove, at every layer, that amounts depend only on what was saved with each line and the discount, never on the product's current values. No new production code is expected: 005's snapshot rule and the calculator already guarantee it; a failure here is a bug to fix.

**Independent Test**: Save the 230.00 Draft; change `CNS-001` to 150.00 / 5% or deactivate it; every amount is unchanged; quantity 2 to 3 gives 300.00 / 45.00 / 345.00; a new line for the repriced product uses 150.00 and 5%.

- [X] T026 [P] [US3] Add to `backend/tests/BillingCrm.Application.Tests/InvoiceServiceUpdateTests.cs`: after the product's price, VAT percent, name and active state are changed in the fake store, `GetAsync` returns exactly the same amounts; editing the existing line's quantity from 2 to 3 gives net 300.00, VAT 45.00, grand 345.00 (the saved 100.00 and 15%); adding a new line for the repriced product gives that line the current price and VAT while the first line keeps its amounts; the same with a discount saved (shares recalculated from saved values only)
- [X] T027 [P] [US3] Add to `backend/tests/BillingCrm.Api.Tests/InvoiceTotalsEndpointTests.cs`: create the acceptance invoice, change the product through `PUT /api/products/{id}` as Admin (price 150.00, VAT 5, then inactive), `GET` the invoice and get identical amounts; a `PUT` that keeps the line and changes its quantity to 3 gives 300.00 / 45.00 / 345.00
- [X] T028 [P] [US3] Add to `backend/tests/BillingCrm.Infrastructure.Tests/InvoiceServiceRealDatabaseTests.cs`: through the real `ProductService` and `InvoiceService` on real SQL, reprice and deactivate the product after saving the invoice and read the invoice's amounts back unchanged; then raise the quantity and see the amounts move using the saved price
- [X] T029 [US3] Run all three suites; if any test in T026-T028 fails, fix the cause (the amounts must never read the product) and re-run; record the outcome in `specs/006-invoice-totals/research.md` (a "Verification log" section)

**Checkpoint**: US3 acceptance scenarios 1-3 pass at the service, HTTP and database layers.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T030 [P] Update `README.md`: the `AddInvoiceDiscount` migration step, what the amounts are and how they are calculated (line net, VAT on the discounted net, rounding half away from zero, the discount shared in proportion), the Discount field, the amount ceiling, and what to check by hand (the ABC / `CNS-001` quantity 2 invoice: 200.00 / 30.00 / 230.00, then a 20.00 discount: 207.00)
- [X] T031 [P] Update `CLAUDE.md`: the project line (spec 006 added the stored discount and calculated amounts), the layout (`InvoiceMoney` in Domain), the Invoices bullet (optional `discount`; `totals` and per-line `net`, `discountShare`, `vat` in every invoice response; new 400 keys; ceiling), the Money bullet (amounts are calculated, never stored; rounding rule) and the "No line amount ... exists anywhere in 005" sentence now points to 006 for the amounts
- [X] T032 [P] Secret and hygiene scan of everything committable: no connection-string literal or credential in the new files; no `float`/`double` on any amount, discount, price, VAT or quantity member (search `backend/src` and `frontend/src`); **no arithmetic on money in `frontend/src/app`** (search the invoice features for `reduce(`, `toFixed`, `Math.round`, `* ` and `+ ` applied to amounts; formatting with the decimal pipe is fine); no `Net`, `Vat` or `Total` column in any migration; every `InvoiceEndpoints.cs` route still declares its access
- [X] T033 Re-run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; all pass (SC-008)
- [X] T034 Run the full quickstart validation (`specs/006-invoice-totals/quickstart.md`) on the developer's machine, including every browser check (an old 005 Draft showing amounts; ABC / `CNS-001` quantity 2 gives 200.00 / 30.00 / 230.00; mixed VAT; the 13.19 / 1.98 rounding line; discount 20.00 gives 207.00; the bad discounts; removing lines below the discount; clearing the discount; product repriced or deactivated with every amount unchanged; signed-out redirect); the developer starts the servers and confirms it is running (Constitution II)
- [X] T035 Run `api-reviewer`, `angular-reviewer` and `spec-auditor`; fix or explicitly record every finding in `specs/006-invoice-totals/research.md` (a "Review disposition" section like 005's). Give `api-reviewer` these focus areas: exactness of the rounding and of the `BigInteger` discount sharing, the ceiling and the JSON scale of amounts, that the prospective check changes nothing on refusal, that invoices made in 005 read correctly, and that no amount is ever read from the live product. Give `angular-reviewer`: no arithmetic on money anywhere in the components and templates, the Discount field's error and focus handling, and the details layout
- [X] T036 Re-run `dotnet test backend/BillingCrm.sln`, `ng test --watch=false` and `ng build` after any review fixes; all pass
- [X] T037 Mark spec 006 done only after T034 and T035 are complete, and update `specs/006-invoice-totals/spec.md` status; do not start spec 007 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates all code generation; T002 is independent.
- **Foundational**: T003 first (tests), T004 needs T003, T005 needs T004, T006 needs T005, T007 is independent of the back end (parallel with T003-T006), T008 needs T003-T007.
- **US1**: after Foundational. Tests T009-T012 in parallel; the back-end change T013 needs T005; the details page T014 needs T007; T015 needs T013 and T014.
- **US2**: after US1 (T013 and the details page already show the discount rows). Tests T016-T020 in parallel; then T021, T022, T023 in order (all in the Application project); T024 (form) needs T007 and can run beside the back-end tasks; T025 needs all of them.
- **US3**: after US2 (tests exercise a saved discount); T026-T028 in parallel; T029 needs them. No production code is expected.
- **Polish**: after all stories. T034 and T035 are the done-ness gate.

### User Story Dependencies

- **US1 (P1)**: needs Foundational only.
- **US2 (P2)**: builds on US1's DTO and details page and on the stored `Discount`.
- **US3 (P3)**: verifies US1 and US2 together; no new production code.

### Within Each Story

Tests first and failing -> implementation -> run tests. Back end: validator -> service -> (endpoint unchanged). Front end: models -> details -> form.

### Parallel Opportunities

- Foundational: T007 with T003-T006.
- US1: tests `T009-T012` together; `T013` and `T014` together.
- US2: tests `T016-T020` together; `T024` alongside `T021-T023`.
- US3: `T026-T028` together.
- Polish: T030-T032.

---

## Parallel Example: User Story 2

```text
# Tests, all at once:
T016 validator   T017 service create/edit   T018 API   T019 real SQL   T020 form specs

# Then two tracks:
Back end:   T021 -> T022 -> T023
Front end:  T024
Then:       T025
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001-T002) and Foundational (T003-T008).
2. US1 (T009-T015).
3. Stop and validate: create a Draft for ABC with `CNS-001` quantity 2 (by API and at `/invoices/new`) and see 200.00, 30.00 and 230.00 on the details page; open an invoice made in 005 and see its amounts. This is the request's stated acceptance scenario.

### Incremental Delivery

1. Add US2 (T016-T025): the discount, its refusals and the form field.
2. Add US3 (T026-T029): the "amounts never follow the product" proof at every layer.
3. Polish, three reviewers, developer verification (T030-T037).

---

## Notes

- T001 is a hard gate; do not generate code before the developer answers.
- Freezing amounts when an invoice is issued, payments and balances, other statuses, a percentage discount, per-line discounts, a live
  preview and a list column for the grand total belong to later specs.
- The three items deferred to 012-security in spec 004 (decimal JSON converter, `StaffOnly` policy, Development header leak) apply to
  this API too and stay deferred.
- Commit only when the developer asks; there are no commits yet.
