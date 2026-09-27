# Implementation Plan: Invoice Totals

**Branch**: `006-invoice-totals` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-invoice-totals/spec.md`

## Summary

Make a Draft invoice readable as a bill. One new stored value, the header **discount** (`decimal(18,2)`, default 0; migration
`AddInvoiceDiscount`); everything else is **calculated from the saved lines every time it is needed**, never stored: a small pure
Domain calculator (`InvoiceMoney`) turns each line's quantity, saved unit price and saved VAT percent, plus the discount, into
line net, the line's share of the discount, line VAT, and the invoice's total net, net after discount, total VAT and grand total,
all exact `decimal`, two decimals, SAR (rules of the `invoice-money` skill). Calculating on read means invoices made in 005 show
correct amounts with no backfill and the amounts can never disagree with the lines. The service uses the same calculator
**before** it changes anything, so a discount larger than the net or an absurdly large invoice is refused and nothing is saved.
The API gains an optional `discount` in the add and edit body and returns `discount`, `totals` and per-line `net`,
`discountShare`, `vat`. Angular shows the amounts on the details page and adds one optional Discount field to the existing form;
the screens never calculate. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new packages. `System.Numerics.BigInteger` (part of .NET) is used for the exact discount sharing

**Storage**: SQL Server LocalDB; one new column `Invoices.Discount` (`decimal(18,2) NOT NULL DEFAULT 0`) and a check
constraint, via migration `AddInvoiceDiscount`. No line columns, no stored totals

**Testing**: xUnit Application tests (calculator vectors and invariants, service refusals with fakes), API tests
(`WebApplicationFactory`, in-memory stores), Infrastructure tests on a throwaway LocalDB database (migration, default 0 for
existing rows, discount round trip, a 005-style invoice read with amounts), Vitest for Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: none new; the calculation is over at most 100 lines in memory. The list is untouched, so SC-006 of 005
still holds

**Constraints**: `decimal` everywhere, never `float`/`double`; rounding half away from zero once per line for net and once per
line for VAT; discount shares add up exactly to the discount; amounts never above 9,999,999,999,999.99 (so a JSON number is exact
in the browser); a refused save changes nothing; no exception for expected failures; the screens do no arithmetic

**Scale/Scope**: at most 100 lines per invoice; 0 new endpoints; 1 new column; 1 new Domain calculator; 1 new form field and
one totals block on the details screen

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design, still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Invoices are a permitted domain. No issuing, payments, balances, PDF/email, per-line discounts, shop, cart, tickets or WhatsApp (FR-015). |
| II. Sequential Spec Delivery | Pass | 005 was verified running by the developer on 2026-09-24 and closed (status Done). 006 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | A bad discount, a discount above the net and a too-large amount are `Result` validation failures mapped to `ProblemDetails` with field keys; nothing throws for them. |
| IV. Financial Data Integrity | Pass | The heart of this spec. Every amount is `decimal(18,2)` SAR (one stored: `Discount`), computed with `decimal` only, rounded half away from zero, refused rather than rounded when a person types a third decimal, never `float`/`double` in the database, Domain, API or client. The ceiling keeps every amount exact as a JSON number. No secrets. |
| V. Deliberate API/UI Design | Pass | The new shapes are listed below to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `minimal-api-conventions` (no new endpoint, but the request and response shapes change),
`angular-ui-conventions` (no business totals calculated in the template: the API returns them), `sql-migrations` (one new
migration, never edit an applied one) and **`invoice-money`** (the rules this spec implements; its "document it in code comments"
rule is followed in the calculator). Before the spec counts as done run `api-reviewer`, `angular-reviewer` and `spec-auditor`
(`security-reviewer` is not required: no auth change, no role split).

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (the `/api/invoices` group open to any signed-in user, whole-Draft `PUT`, `Result` to
`ProblemDetails`, field errors with array paths, `decimal?` JSON bound with `Strict` numbers, text-held amounts and
`decimal-text.ts`, `applyProblemToForm`, one add/edit form, no delete) need no re-explanation. New in this spec:

1. **A pure calculator in the Domain** — `InvoiceMoney.Calculate(lines, discount)` (a static class with small records
   `AmountLine`, `LineAmounts`, `InvoiceAmounts`). It is the only place these rules exist; the aggregate, the service and the
   tests all call it. It is a new shared abstraction (R1).
2. **Amounts are calculated on read, not stored** — only `Discount` is a new column. 005 invoices show amounts with no backfill and
   nothing can drift. A later spec that *issues* an invoice will have to freeze its amounts then (R2).
3. **Rounding and sharing rules** — net and VAT rounded half away from zero once per line; the discount is shared over the lines
   in proportion to their net using exact whole-halala arithmetic (largest remainder, ties to the earlier line) so the shares
   add up to the discount exactly (R3, R4).
4. **Discount is an optional `decimal?` in the whole-Draft body** — omitted or `null` means none and clears a saved discount
   (`PUT` describes the whole desired Draft) (R5).
5. **Prospective check before any change** — the service calculates the amounts the edit *would* produce, and refuses a discount
   above the net or a too-large amount before touching the invoice (R6).
6. **Amount ceiling 9,999,999,999,999.99** (about ten trillion) so a JSON number is exact in the browser; this narrows the spec's
   first draft (R7).
7. **Response shape** — `discount`, `totals { totalNet, discount, netAfterDiscount, totalVat, grandTotal }` on the invoice and
   `net`, `discountShare`, `vat` on each line, as JSON numbers with two decimals (R8).
8. **Angular** — no new architectural pattern: the details screen shows what it is given, the form gets one optional
   text-held Discount field, and server messages reuse `applyProblemToForm`. No live preview (R9).

## Project Structure

### Documentation (this feature)

```text
specs/006-invoice-totals/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── invoice-totals-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 005 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── InvoiceMoney.cs                    # NEW: pure calculator + AmountLine / LineAmounts / InvoiceAmounts, MaxAmount
│   └── Invoice.cs                         # + Discount, UpdateDetails(..., discount, ...), Amounts (calls InvoiceMoney)
├── BillingCrm.Application/
│   └── Invoices/
│       ├── InvoiceModels.cs               # InvoiceInput + Discount; InvoiceLineDto + Net/DiscountShare/Vat; InvoiceDto + Discount/Totals
│       ├── InvoiceValidator.cs            # + discount shape rules (>= 0, <= 2 decimals, <= MaxAmount); Inspection carries Discount
│       └── InvoiceService.cs              # prospective amounts before create/update: discount > net, too-large refusals
├── BillingCrm.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/InvoiceConfiguration.cs   # + Discount column, default 0, CK_Invoices_Discount
│   │   └── Migrations/                    # AddInvoiceDiscount
│   └── (EfInvoiceStore unchanged)
└── BillingCrm.Api/
    └── (InvoiceEndpoints unchanged: same routes, richer bodies)

backend/tests/
├── BillingCrm.Application.Tests/          # InvoiceMoneyTests (vectors + invariants), validator + service discount/limit tests
├── BillingCrm.Api.Tests/                  # totals in create/get/put responses, discount refusals, access
└── BillingCrm.Infrastructure.Tests/       # migration smoke (column/default/check), discount round trip, 005-style read

frontend/src/app/
├── core/invoices/invoice.models.ts        # + discount, totals, per-line amounts; InvoiceInput.discount
├── features/invoices/
│   ├── invoice-detail.component.ts        # totals block + per-line net / discount share / VAT (display only)
│   └── invoice-form.component.ts          # + optional Discount field, error mapping for discount / lines
└── (spec files beside them)

README.md, CLAUDE.md                       # the migration step, the totals rules, the discount field, the layout
```

**Structure Decision**: Extend the 005 layout in place. One new Domain file for the calculator; everything else is a change to
an existing file. No new projects, packages, endpoints or Angular components.

## Known gaps carried forward

- Freezing amounts when an invoice is issued, payments and balances, other statuses, per-line discounts, a discount as a
  percentage, a live amount preview in the form, a grand-total column in the list, PDF/email.
- Optimistic concurrency beyond 005's 409 on the same rows (a `rowversion`; see research R11).
- The decimal-literal converter, `StaffOnly` policy and Development header leak deferred to 012-security still apply.

## Complexity Tracking

No constitution violations to justify. The one new abstraction (the Domain calculator) is the minimum needed so the rules exist in
exactly one place and can be proven with exact vectors; it is listed for confirmation above.
