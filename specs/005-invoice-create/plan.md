# Implementation Plan: Create Draft Invoices

**Branch**: `005-invoice-create` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-invoice-create/spec.md`

## Summary

Add the invoice. An `Invoices` table and an `InvoiceLines` table (migration `AddInvoices`) plus a SQL sequence that hands out
the number (`INV-000123` is formatted from an integer, never stored as text); an `InvoiceService` in Application that
validates the whole request, checks the customer and every new line's product are active, copies each new line's name, SKU,
unit, unit price and VAT percent **once** into the line, and returns `Result`s; an EF store; a `/api/invoices` group for any
signed-in user (create, list, get, and a `PUT` that saves the whole Draft: header plus the full list of lines, so a refused
save changes nothing); and Angular list, details and one add/edit form with customer and product pickers built on the existing
list APIs. No totals, VAT amounts or line amounts anywhere: this spec persists exactly the three numbers per line
(quantity, unit price, VAT percent) that the later `invoice-money` totals spec needs. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new packages. EF Core 8 sequences and `DateOnly` are built in; Angular Material autocomplete,
table, paginator, datepicker-free plain date input are already available (a native date input avoids a new date adapter
dependency)

**Storage**: SQL Server LocalDB; new sequence `InvoiceNumbers` and tables `Invoices`, `InvoiceLines` via migration
`AddInvoices`

**Testing**: xUnit Application unit tests (fake stores), API tests (`WebApplicationFactory`; in-memory invoice store beside the
existing in-memory customer and product stores), the existing `BillingCrm.Infrastructure.Tests` project against a throwaway
LocalDB database (including a **parallel** number test); Vitest for Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: first page of the invoice list over 5,000 invoices in under 1 s (SC-006); create a one-line Draft in under
2 minutes by hand (SC-001)

**Constraints**: numbers unique under simultaneous creation, guaranteed by the database (sequence + unique index); snapshot
columns never rewritten; a refused save changes nothing (single transaction); no exception for expected failures; every
endpoint declares access; exact decimals, no rounding of what the person typed; Arabic and English text; no delete; no totals

**Scale/Scope**: about 5,000 invoices, at most 100 lines each; 4 API operations; 3 new screens plus a shell link

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design — still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Invoices are one of the three permitted domains. No issuing, payments, PDF/email, shop, cart, tickets or WhatsApp (FR-021). |
| II. Sequential Spec Delivery | Pass | 004 was verified running by the developer on 2026-09-24 and closed. 005 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | Validation, inactive or missing customer/product and not found are `Result` failures mapped to `ProblemDetails`; no unique-index violation can come from user input (the number is drawn by the database), so no `Try*` exception translation is needed. |
| IV. Financial Data Integrity | Pass (one reading to confirm) | Unit price `decimal(18,2)` SAR and VAT `decimal(5,2)` are copied exactly from the product; quantity is `decimal(9,2)` (a quantity, not money: same reading as VAT percent in 004, please confirm); nothing multiplies or rounds anything here; no `float`/`double`; no secrets. |
| V. Deliberate API/UI Design | Pass | The items below are to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations` and `invoice-money` (only to keep
money out of this spec's code). Before the spec counts as done run `api-reviewer`, `angular-reviewer` and `spec-auditor`
(`security-reviewer` is not required: no auth code changes and no role split).

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (resource group open to any signed-in user, `201 + Location`, `Result` to `ProblemDetails`,
`PagedResult<T>`, URL-driven list state, one add/edit form, `problem-details.ts`, text-held amounts, `decimal?` JSON with
`Strict` numbers, no delete) need no re-explanation. New in this spec:

1. **Aggregate saved as a whole** — `PUT /api/invoices/{id}` sends the full desired Draft; lines are existing (`id`, quantity
   only), new (`productId`), or omitted (removed); one atomic save; no per-line endpoints and no `DELETE` (R1).
2. **Invoice numbers from a SQL sequence** — `InvoiceNumbers`, an `int` column with a unique index, `INV-` + six digits formatted
   in one Domain function; gaps allowed, duplicates impossible (R2).
3. **Line snapshot columns plus a product reference** — name, SKU, unit, unit price, VAT percent copied once; no live join (R3).
4. **Quantity as `decimal(9,2)`, dates as `date`, "today" at a fixed UTC+03:00** (R4, R5).
5. **Every refusal is a field error with an array path** (`lines[0].quantity`), including unknown/inactive customer or product;
   ids arrive as text and are parsed by the validator (R6).
6. **Edit rules for an existing Draft** — same customer always allowed, a different one must be active; existing lines change
   only quantity; new lines need an active product; 1 to 100 lines; unchanged save writes nothing (R7).
7. **Angular** — one form with a `FormArray` of lines; two picker components (customer, product) on a Material autocomplete
   using the **existing** list APIs; a new pure helper `core/money/decimal-text.ts` for quantity text (products are not
   refactored to use it now); `applyProblemToForm` learns bracket paths (R12).
8. **Defaults in the spec's Assumptions** — Admin and Agent both allowed, at least one line, limits (100 lines, 999,999.99,
   1,000 characters, six digits), same product allowed twice, last save wins, no seed data.

## Project Structure

### Documentation (this feature)

```text
specs/005-invoice-create/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── invoices-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 004 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── InvoiceStatus.cs                   # enum Draft
│   ├── InvoiceNumber.cs                   # Format(int) -> INV-000123, TryParse
│   ├── Invoice.cs                         # aggregate root: Create/UpdateDetails/AddLine/RemoveLine/SetQuantity
│   └── InvoiceLine.cs                     # entity: snapshot copied once from a Product; only Quantity changes
├── BillingCrm.Application/
│   └── Invoices/
│       ├── IInvoiceStore.cs               # FindById (with lines), Add, SaveChanges, Search
│       ├── InvoiceService.cs              # Create, Update, Get, Search -> Result
│       ├── InvoiceValidator.cs            # shape rules, date/quantity/notes/lines, all errors together
│       └── InvoiceModels.cs               # InvoiceInput/LineInput, DTOs, InvoiceSummary, InvoiceQuery
├── BillingCrm.Infrastructure/
│   ├── Persistence/
│   │   ├── BillingDbContext.cs            # + DbSet<Invoice>, DbSet<InvoiceLine>, sequence
│   │   ├── Configurations/InvoiceConfiguration.cs
│   │   ├── Configurations/InvoiceLineConfiguration.cs
│   │   └── Migrations/                    # AddInvoices
│   ├── Invoices/EfInvoiceStore.cs         # add (number filled by the database), replace lines, list with customer name
│   └── DependencyInjection (existing)     # register IInvoiceStore
└── BillingCrm.Api/
    ├── Endpoints/InvoiceEndpoints.cs      # /api/invoices group
    └── Program.cs                         # MapInvoiceEndpoints()

backend/tests/
├── BillingCrm.Application.Tests/          # InvoiceService/validator tests, FakeInvoiceStore
├── BillingCrm.Api.Tests/                  # ApiFactory gains InMemoryInvoiceStore; HTTP contract tests
└── BillingCrm.Infrastructure.Tests/       # sequence (parallel), snapshots, replace-lines, constraints, list timing

frontend/src/app/
├── core/
│   ├── invoices/{invoice.service.ts, invoice.models.ts}
│   ├── money/decimal-text.ts              # quantity text checks (pure functions)
│   ├── http/problem-details.ts            # + bracket-path mapping (lines[0].quantity)
│   └── layout/shell.component.ts          # + Invoices link
├── features/invoices/
│   ├── invoice-list.component.ts
│   ├── invoice-detail.component.ts
│   ├── invoice-form.component.ts          # add + edit, FormArray of lines
│   └── pickers/{customer-picker.component.ts, product-picker.component.ts}
└── app.routes.ts                          # /invoices, /invoices/new, /invoices/:id, /invoices/:id/edit

README.md, CLAUDE.md                       # invoices table step, invoices API, layout, the invoice-number rule
```

**Structure Decision**: Extend the 004 layout in place, one folder per concern per layer. No new projects or packages. The only
new shared pieces are the two small pickers and the decimal-text helper, both listed for confirmation above.

## Known gaps carried forward

- Totals and every later invoice concern (issuing, statuses, payments, PDF/email, cancel/delete, list search and filters);
  optimistic concurrency; numbers above 999,999; a per-invoice currency; line discounts.
- The decimal-literal converter, `StaffOnly` policy and Development header leak deferred to 012-security also cover invoices.

## Complexity Tracking

No constitution violations to justify. The new shapes (aggregate `PUT`, number sequence, snapshot columns, pickers, decimal-text
helper) are each the minimum for the spec's rules ("a refused save changes nothing", "unique under simultaneous creation",
"lines keep what they were added with") and are listed for confirmation above.
