# Research: Invoice Totals

**Feature**: 006-invoice-totals | Decisions taken while planning. No `NEEDS CLARIFICATION` remains: the two open questions were
answered on 2026-09-24 (discount is a fixed SAR amount; it is shared over the lines in proportion to their net).

## R1. One pure calculator in the Domain

- **Decision**: A static class `InvoiceMoney` in `BillingCrm.Domain` with `Calculate(IReadOnlyList<AmountLine> lines, decimal discount)`
  returning an `InvoiceAmounts` record. `AmountLine(decimal Quantity, decimal UnitPrice, decimal VatPercent)` is the input;
  `LineAmounts(decimal Net, decimal DiscountShare, decimal Vat)` is one result per input line, in the same order;
  `InvoiceAmounts` holds the line list, `TotalNet`, `Discount`, `NetAfterDiscount`, `TotalVat` and `GrandTotal`. It also holds the
  constant `MaxAmount = 9_999_999_999_999.99m`. The class has no state and no dependencies.
- **Rationale**: the rules must exist in exactly one place. The aggregate (`Invoice.Amounts`), the service's "would this edit be
  allowed?" check and every test call the same function, so what is checked before saving is what is shown after. Because it
  takes plain numbers, not an `Invoice`, the service can calculate for a *proposed* set of lines before anything is changed (R6).
  The header comment records the `invoice-money` rule ("header discount on net, then VAT on the discounted net") as that skill
  asks.
- **Alternatives**: methods on `Invoice` only (cannot be called for a proposed edit without mutating the aggregate); a service in
  Application (Domain would then depend outwards, and the aggregate could not expose its own amounts); computing in SQL or in
  Angular (rejected: two places for money rules, and the screens must not calculate).
- This is a new shared abstraction, so it is listed for the developer's confirmation (Principle V, task T001).

## R2. Calculated on read, not stored

- **Decision**: Only the discount is stored. Line net, discount share, line VAT and every total are calculated from the saved lines
  each time an invoice is read or saved, and returned in the response.
- **Rationale**: (1) spec FR-008 and SC-006: invoices from 005 must show correct amounts with no manual step, so there is nothing
  to backfill; (2) the amounts can never disagree with the lines, whatever order things are saved in; (3) the inputs are already
  frozen per line by 005's snapshot rule, so calculating later gives the same answer as calculating at save time (spec story 3);
  (4) no new columns to keep in step, and nothing extra to migrate or check.
- **Alternatives**: store the totals on the invoice and lines (a backfill migration for 005 invoices, and every code path that
  changes a line must remember to update them: the classic source of wrong totals).
- **Known consequence**: when a later spec *issues* an invoice, it must freeze the amounts (store them) at that moment so that
  a future change to a rule cannot alter an issued invoice. That belongs to that spec (recorded in the plan's known gaps).

## R3. Rounding

- **Decision**: `decimal.Round(x, 2, MidpointRounding.AwayFromZero)`. Line net = `Round(Quantity * UnitPrice, 2)`. Line VAT =
  `Round(discountedLineNet * VatPercent / 100, 2)`. Rounded once per line for each; totals are plain sums of already-rounded
  numbers; nothing is rounded again.
- **Rationale**: the `invoice-money` skill writes `Round(net * vatPercent/100, 2)` and the spec asks for 2 decimals. Away from zero
  is the usual commercial rule and is the one people expect by hand (0.025 to 0.03). C#'s default (`ToEven`) would give 0.02 and
  surprise staff. What is printed on a line is exactly what is summed, so the invoice always adds up on paper.
- **Worked check**: 1.25 x 10.55 = 13.1875, net 13.19; VAT 15% of 13.19 = 1.9785, VAT 1.98; grand 15.17.
- **Alternatives**: bankers' rounding (surprising); rounding only the total VAT (a line's VAT would not add up); keeping four
  decimals on the net (violates "amounts are `decimal(18,2)`").

## R4. Sharing the discount over the lines

- **Decision**: Work in whole halalas (hundredths of a riyal) with `BigInteger`. With `D` the discount, `N` the total net and
  `n_i` line net (all in halalas): `share_i = floor(D * n_i / N)` and `remainder_i = (D * n_i) mod N`. The halalas left over
  (`D - sum(share_i)`, always fewer than the number of lines) go one each to the lines with the largest `remainder_i`; ties go to the
  earlier line. If `N` is 0 the discount must be 0 (it may not exceed the net) and every share is 0. Line VAT is then worked
  out on `n_i - share_i` (R3).
- **Rationale**: the spec says the shares add up exactly to the discount and the result is the same every time. Integer division and
  remainders are exact and cannot lose or invent a halala; `BigInteger` avoids any overflow (`D * n_i` can reach about 1e30
  halalas squared at the ceiling, more than a `long` holds). A fixed tie rule makes the same invoice always give the same
  amounts. Proportional sharing is what makes "VAT on the discounted net" correct for lines with different VAT percentages.
- **Vectors** (used as tests): one line, net 200.00, discount 20.00 gives share 20.00, VAT 27.00, grand 207.00. Lines net 100.00 (15%)
  and 50.00 (0%), discount 30.00 give shares 20.00 and 10.00, VAT 12.00 and 0.00, grand 132.00. Three lines net 10.00 each,
  discount 0.01: shares 0.01, 0.00, 0.00 (all remainders equal, first line wins). Discount equal to the net: all zero.
- **Alternatives**: taking the discount off the grand total only (over-charges VAT; the developer rejected it); allocating with
  decimal division (repeating decimals make the sum drift by a halala); giving all rounding leftovers to the last line (biased).

## R5. Discount input

- **Decision**: `InvoiceInput` gets a last parameter `decimal? Discount = null`. The validator treats `null` as "no discount" (0.00).
  A value below 0 or with more than two decimal places is refused (never rounded), like a product price in 004; a value above the
  ceiling is refused. The same body shape is used for add and edit; because `PUT` describes the **whole desired Draft**, leaving
  the discount out clears a saved one. A JSON string or other wrong type gives the generic 400 (numbers are bound with `Strict`,
  as for quantities).
- **Rationale**: consistent with 005's whole-Draft rule and with how money is already accepted (004). A default of `null` keeps
  every existing call site and test compiling.
- **Alternatives**: a separate `PUT /discount` endpoint (a new endpoint pattern for no gain); omitted means "keep" (breaks the
  whole-Draft rule and makes clearing impossible without a second convention).

## R6. Checks that need the lines: prospective calculation

- **Decision**: The validator checks the discount's shape. The **service**, once the customer and products are resolved and there
  are no other errors, builds the list of amount lines the save *would* produce (an existing line: its saved price and VAT with the
  requested quantity; a new line: the product's current price and VAT; on create, all lines new), calls `InvoiceMoney.Calculate`,
  and refuses when: a line net is above `MaxAmount` (`lines[i].quantity`), the total net or grand total is above `MaxAmount`
  (`lines`), or the discount is above the total net (`discount`). Only after these pass does it change the aggregate. If other
  errors already exist, these three checks are skipped and reported after those are fixed (they need valid lines to mean anything).
- **Rationale**: the same "check everything first, change nothing on refusal" rule as 005 (FR-012), without half-applying an edit to
  a tracked EF entity and then having to undo it.
- **Alternatives**: apply then check (leaves a changed tracked entity behind); a database check constraint (cannot see other
  rows).

## R7. The ceiling on amounts

- **Decision**: `MaxAmount = 9,999,999,999,999.99` SAR (about ten trillion). Any line net, total net or grand total above it is
  refused; a discount above it is refused by shape (it cannot be more than the net anyway).
- **Rationale**: amounts travel as JSON numbers (as all money in this API does), and a browser number is exact only up to 2^53
  (about 9.0e15) in whole halalas, i.e. about 9.0e13 riyals. Refusing anything above about 1e13 keeps every amount exact on every
  screen (FR-010) and still far above any real invoice. Without a ceiling, 004's largest price (999,999,999.99) times the largest
  quantity (999,999.99) over 100 lines reaches about 1e17 riyals: above `decimal(18,2)`, above exact browser numbers, and
  meaningless. The spec's first draft used the `decimal(18,2)` limit (about 1e16); planning found that too high, so the spec
  text was narrowed (FR-011, the edge case).
- **Alternatives**: send amounts as JSON strings (a new convention that every client would have to parse); leave the ceiling at
  `decimal(18,2)` (the browser could then show a wrong halala on absurd invoices).

## R8. API shape

- **Decision**: No new route. The add, edit and get responses (the same `InvoiceDto`) gain `discount` and `totals`
  (`totalNet`, `discount`, `netAfterDiscount`, `totalVat`, `grandTotal`), and each line gains `net`, `discountShare`, `vat`. Amounts
  are JSON numbers written with two decimals (`decimal` keeps its scale when serialised). The list summary is unchanged.
  Field names, errors and examples are in [contracts/invoice-totals-api.md](contracts/invoice-totals-api.md).
- **Rationale**: everything the screen needs comes back in the one response it already asks for; the client does no arithmetic
  (`angular-ui-conventions`: no business totals in the template if the API returns them).
- **Alternatives**: a separate `GET /api/invoices/{id}/totals` (a second round trip, and two places that must agree).

## R9. Angular

- **Decision**: `Invoice`/`InvoiceLine` models gain the new fields (`InvoiceInput.discount: number | null`). The details page shows a
  totals block (total net, discount when not zero, net after discount, total VAT, grand total, all `SAR` with the decimal pipe
  in `1.2-2`) and per line net, discount share (only when a discount exists) and VAT. The form gets one optional **Discount**
  field held as text and checked with the existing `decimalTextProblem` (empty allowed; zero allowed; not negative; at most two
  decimals; up to the ceiling), sent as `Number(text)` or `null`. Server messages for `discount`, `lines` and `lines[i].quantity`
  reuse `applyProblemToForm`; the form already shows a `lines` message above the lines. There is **no live preview**: the
  form cannot show a total without calculating one.
- **Rationale**: no new component or pattern, so nothing new to confirm on the UI side beyond the field.
- **Alternatives**: a live preview by duplicating the rules in TypeScript (two copies of money rules, and floating-point
  temptation); an "amount" component (nothing to share yet: the details screen is the only place amounts are shown).

## R10. Test approach

- **Calculator (Application.Tests, exact decimals)**: the vectors in the spec and R4; the rounding cases (0.025, 13.1875, 1.9785, 0.0045);
  zero price; the ceiling boundary; and invariants over many generated invoices (a fixed-seed generator): shares sum to the
  discount; total net equals the sum of line nets; total VAT equals the sum of line VATs; grand equals net after discount plus VAT;
  no negative share; the same input twice gives the same output; every amount has at most two decimals.
- **Service/validator (Application.Tests)**: discount shape rules; discount above the net on create and on edit; an edit that
  reduces lines below the saved discount; a too-large line and total; nothing saved on refusal; an edit keeps its saved price when
  the product changed; a new line uses the current price; an omitted discount clears a saved one.
- **API (Api.Tests)**: the acceptance body (200.00 / 30.00 / 230.00) on create, get and put; a discount body; the error keys;
  wrong-typed discount; signed-out access.
- **Real SQL (Infrastructure.Tests)**: the migration smoke test (column type `decimal(18,2)`, default 0, check constraint); an
  invoice inserted the 005 way (no discount column in the insert) reads back with discount 0 and correct amounts; a discount
  round-trips exactly.
- **Browser**: the developer's quickstart run (T060-style).

## R11. Known gaps

- Freezing amounts at issue time, payments, balances and remaining amounts, a percentage discount, per-line discounts, a live
  preview, a list column for the grand total: later specs.
- Amount output is a JSON number like all money here; strings would be needed only if the ceiling were ever raised.
- A `rowversion` on `Invoices` (found by the API review): without one, two people saving different rows of the same Draft can leave
  a saved discount above the net. Reading is safe (`Invoice.Amounts` shows the whole net) and the next save re-checks it; a row
  version would make every concurrent edit a 409 and needs its own decision.

## Confirmations (task T001, Constitution V) — 2026-09-24

The developer confirmed every new pattern in plan.md before any code was generated (all four questions answered "Confirm"):

1. **Calculator and storage (R1, R2)**: the pure Domain calculator `InvoiceMoney` is the one place the money rules live; amounts are
   calculated on read and only `Discount` is stored. A later "issue invoice" spec must freeze the amounts when it issues.
2. **Rounding, sharing, ceiling (R3, R4, R7)**: half away from zero once per line; the discount shared in whole halalas by largest
   remainder (ties to the earlier line); ceiling 9,999,999,999,999.99.
3. **API shape (R5, R6, R8)**: optional `decimal?` `discount` in the whole-Draft body (omitted clears it); the prospective check before
   any change; the response carries `discount`, `totals` and per-line `net`, `discountShare`, `vat` as JSON numbers with two decimals.
4. **Angular (R9)**: totals block on the details page, one optional Discount field on the form, no live preview, no arithmetic in components.

## Verification log

### T029: user story 3 (amounts stay true when products change), 2026-09-24

No production code was needed: 005's snapshot rule and the calculator already guarantee it. The proof runs at three layers:

| Layer | What was checked | Result |
|-------|------------------|--------|
| Application (fakes) | Product repriced, renamed and deactivated after saving: `GetAsync` amounts identical; the same with a saved discount (207.00); a quantity 2 to 3 edit after a repricing (150.00 / 5%) gives 300.00 / 45.00 / 345.00 from the saved 100.00 / 15%; a new line for the repriced product gets 150.00 / 5% while the kept line keeps 200.00 / 30.00 (total 387.50) | Pass |
| API (in-memory stores, real HTTP) | An Admin reprices (150 / 5%) and deactivates the product through `PUT /api/products/{id}` and `/active`; `GET /api/invoices/{id}` still returns 200.00 / 30.00 / 230.00; a `PUT` of quantity 3 returns 300.00 / 45.00 / 345.00 | Pass |
| Real SQL Server | Through the real `ProductService` (update to 999.99 / 0%, then inactive) and the real `InvoiceService`: amounts read back in a new context are identical; an edit to quantity 3 with the same 20.00 discount gives 300.00 / 280.00 / 42.00 / 322.00 | Pass |

Also proven on real SQL (T011, T019): an invoice inserted the 005 way (no `Discount` in the insert) reads back with discount 0 and
230.00 (SC-006); a discount of 20.10 round-trips exactly; `CK_Invoices_Discount` rejects a negative value (547).

Test status at this point: Application 504, API 396, Infrastructure 160, Angular 488, all passing; `ng build` clean.

### Adjustment made while implementing

- A single line above the ceiling also pushes the total above it. Only the line is reported (`lines[i].quantity`); the `lines`
  message is added only when no single line is too large, because fixing the line fixes the total (contract updated).
- Two tests written in 005 asserted "no totals anywhere" (a reflection test on the DTOs and an API body test). They were replaced
  by tests that the amounts exist and that no balance, payment or paid member exists: 005's FR-013 is superseded by this spec.

## Review disposition (2026-09-24)

Three read-only reviews ran after implementation (`api-reviewer`, `angular-reviewer`, `spec-auditor`). One real defect was found and
fixed; every other finding is fixed or explicitly recorded here.

### api-reviewer

| Finding | Disposition |
|---------|-------------|
| **B1 (blocker)**: two people saving different rows of the same Draft at once (one the discount, one a smaller quantity) can leave a saved discount above the net, and `Invoice.Amounts` then threw, so `GET` was a 500 | **Fixed**: `Invoice.Amounts` never throws; a stored discount above the net is shown as the whole net (nothing left to pay) and the next save checks it again. Tests: aggregate, and real SQL (a row with discount 50.00 over a net of 30.00 reads back). Not done: a `rowversion` column, which would turn every concurrent edit into a 409; recorded as a known gap (R11) because it changes 005's "later save wins" rule and was not confirmed at T001 |
| Minor 1: the prospective check calculated in request order, the saved invoice in position order (tie-breaks could differ by a halala) | **Fixed**: the check orders kept lines by saved position, then new lines in request order; a too-large line is still named by its own request index. Test added |
| Minor 2: `Invoice.Lines` order not total (positions are not unique) | **Fixed**: `ThenBy(Id)` |
| Minor 3: the aggregate does not validate the discount | **Accepted**: the service is the gate (as for quantities); B1's fix makes reading safe whatever is stored |
| Minor 4: invariant tests would pass a wrong sharing algorithm | **Fixed**: each generated share must be within one halala of its exact proportional part; every fifth generated line is free (zero-net lines with a discount); a 100-line case near the ceiling with a 5 trillion discount |
| Minor 5-7: ceiling boundary, `lines` key on edit and over HTTP, real-SQL ceiling, multi-line shares over JSON, raw JSON text for 20.10 | **Added**: exact ceiling accepted and one halala over refused; total too large on edit and over HTTP; a real-SQL ceiling refusal; shares and the two-decimal wire format for 20.10 |
| Minor 8: seeded `unitPrice` in-memory serialises as `100`, not `100.00` | **Accepted**: real SQL always gives scale 2, and every calculated amount goes through `Money()`; the contract promises two decimals for amounts |

### angular-reviewer

| Finding | Disposition |
|---------|-------------|
| 1 (medium): line-level fixtures in the detail spec were arithmetically true, so a component that calculated would pass | **Fixed**: fixtures where net, VAT and discount share differ from what the raw fields give, asserted per column (`td[6]`, `td[7]`, `td[8]`) |
| 2 (medium): FR-007 says the discount rows always show; the screen hides them when there is no discount | **Fixed in the spec**: FR-007 now says the API always returns the discount amounts and the screen shows the discount rows and column only when there is a discount. The gate now reads the discount that is shown (`totals.discount`) so the two can never disagree; test added |
| 3: the server `lines` message stayed until the next save | **Fixed**: it clears as soon as a line is edited. A server `discount` message stays until that field is edited or the next save (the server has the last word). Tests added |
| 4: no focus or `aria-invalid` test for the Discount field | **Added**: client refusal and server refusal both move focus to the field, which is `aria-invalid` |
| 5: thin boundary tests | **Added**: `9999999999999.99` and `20.100`, `0.00` accepted; `9999999999999.991`, `10000000000000.00`, `.5`, `5.`, Arabic-Indic digits refused; a saved 20.10 loads as `20.1` and is sent back as the same number; edit-mode refusals clear on the next save |
| 6: stale comments in `decimal-text.ts` | **Fixed** |
| 7: totals `dl` label, physical margin, border gap, cramped table, amount alignment | **Fixed**: a visible "Totals" heading names the section, `margin-inline-start`, no border gap, the lines table scrolls sideways in a narrow card, amount columns are right-aligned |
| 8: thin "shows no amounts" test | **Fixed**: also no `SAR` text and no table while loading, not found and error |

### spec-auditor

| Finding | Disposition |
|---------|-------------|
| F1: spec not closed | **Open by design**: T034 (the developer's browser run) and T037 |
| F2: FR-007 versus the hidden discount rows | **Fixed in the spec** (see angular 2) |
| F3: a wrong-typed discount is a 400 without a field name (FR-012) | **Fixed in the spec**: FR-012 now says so, as for a quantity in 005 (the screen never sends one). The API test also asserts the problem shape and that no `discount` error key is present |
| F4: missing tests | **Added**: exact ceiling, `lines` on edit and over HTTP, combined `discount` + `lines` errors, checks skipped while another error exists and reported once it is fixed, discount equal to the net on edit, over HTTP and on real SQL, 20.10 over a 20.00 net. **Not added**: running the migration over a populated pre-006 database (the raw-SQL 005-style insert plus a NOT NULL DEFAULT 0 column is the same proof) and a 006-specific two-people test beyond B1 and 005's 409 tests |
| F5: tests that claimed more than they proved | **Fixed**: the two-decimals test now includes a four-decimal net, an unstorable three-decimal price case was replaced, the validator's ceiling and decimal cases are separated with their own messages, two tests were renamed to what they prove, and one test was replaced by a real combined-errors case |
| F6: documentation drift | **Fixed**: the `invoice-money` skill now states the rounded net, the discount sharing and the calculator (updated at the developer's "yes"), quickstart rewritten as separate invoices with verified numbers (adds `ODD-001`), `InvoiceLine.cs` comment, `CLAUDE.md` helper line. **Not changed here**: the project skill `.claude/skills/invoice-money/SKILL.md` still says line net = qty x unitPrice with no rounding; this spec rounds the net to two decimals (half away from zero) before VAT. The skill is project governance, so this is left to the developer to decide |

Test status after the fixes: Application 515, API 400, Infrastructure 162, Angular 500, all passing.

## Developer verification (T034)

2026-09-24: the developer ran `quickstart.md` on their machine (API and Angular running against LocalDB) and confirmed "Yes, 006 verified". Spec 006 is Done.
