# Implementation Plan: Payments

**Branch**: `008-payments` | **Date**: 2026-09-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-payments/spec.md`

## Summary

Let staff record money received against a Sent invoice. A new `Payments` table (one row per payment: date, amount, method, note, the
time it was recorded) belongs to the invoice and is read and written through the `Invoice` aggregate, which gains `Payments`,
`Paid`, `Remaining`, `RecordPayment` and `AcceptsPayment`. **Remaining is never stored**: it is the invoice's own frozen grand total
(007) minus the sum of its payments, worked out by a two-line addition to `InvoiceMoney` so the money rules stay in one place. **Paid is
the one stored consequence**: the payment that brings the remaining to exactly 0.00 moves the invoice from Sent to Paid in the same
save as the payment row, so both happen or neither. The transition table gains the fourth legal change, **Sent to Paid**, which only a
payment can cause (it is never offered to a person). One new route, `POST /api/invoices/{id}/payments` (any signed-in user), answers
`201` with the updated invoice; an invoice that is Draft, Cancelled or Paid is a `409` naming the status; field problems (date, amount,
method, note, overpayment) are `400` field errors reported together; **cancelling an invoice that has payments is refused** (`409`),
which is the one change to a 007 rule. Concurrency reuses 007's `RowVersion`: adding a payment writes the invoice row too, so two
payments at once, or a payment racing a cancel, end with exactly one winner. Angular shows the paid and remaining figures and the list of
payments on the details screen and adds one form screen, `/invoices/:id/payments/new`, built like the other forms. Decisions are in
[research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new packages

**Storage**: SQL Server LocalDB; migration `AddPayments`: one new table `Payments` (amount `decimal(18,2)`, date as `date`, method as
text, note `nvarchar(1000)`, UTC time) with check constraints and an index on the invoice. `Invoices` is unchanged: its existing
checks already allow a Paid invoice (sent, not cancelled, frozen amounts present)

**Testing**: xUnit Application tests (the transition table, the aggregate's `RecordPayment` and balance arithmetic, service refusals
with fakes), API tests (`WebApplicationFactory`, in-memory stores; 401, 404, 409, 400, 201, the pinned route list), Infrastructure
tests on a throwaway LocalDB database (migration, constraints, round trip, concurrent payment/payment and payment/cancel), Vitest for
Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: none new. An invoice is loaded with its lines and its payments (two child collections, loaded as split queries so
they do not multiply); the list still reads one page of rows

**Constraints**: paid and remaining exact to the halala and calculated only on the server; a refused request changes nothing; no
exception for an expected failure; the invoice's status is judged before the payment's contents; `decimal(18,2)` SAR only, no
`float`/`double`; the API never rounds a payment (a third decimal is refused); payments are never edited or deleted; no secrets

**Scale/Scope**: 1 new route; 1 migration (1 new table); 2 new Domain types (`Payment`, `PaymentMethod`) and 5 new aggregate members;
1 new tiny calculator method; 1 new form screen, the details screen changes; no new package

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design, still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Invoices are a permitted domain. A payment here is a **record** staff make after receiving money; nothing charges a card or moves money, so it is not a payment gateway (spec FR-016, Assumptions). No refunds, receipts, PDF/email, reports, shop, cart, tickets or WhatsApp. |
| II. Sequential Spec Delivery | Pass | 007 was verified running by the developer on 2026-09-26 and closed. 008 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | Not found, wrong status, has-payments and "changed by someone else" are `Result` not-found/conflict mapped to `ProblemDetails` (404/409); date, amount (including overpayment), method and note problems are 400 field errors. Nothing throws for these. Only a defect in the aggregate (a payment reaching an invoice that is not Sent, or one larger than the remaining) throws, and the service refuses first. |
| IV. Financial Data Integrity | Pass | `Payments.Amount` is `decimal(18,2)` SAR with `> 0` and a ceiling as check constraints; paid and remaining are exact decimal arithmetic, never `float`/`double`, never rounded, never calculated in Angular; the grand total used is the frozen one. No secrets. |
| V. Deliberate API/UI Design | Pass | The new shapes below are to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `minimal-api-conventions` (one new route), `angular-ui-conventions` (one form screen, states, hide what cannot
be done), `sql-migrations` (one new migration, never edit an applied one) and `invoice-money` (paid and remaining come from
`InvoiceMoney`). Before the spec counts as done run `api-reviewer`, `angular-reviewer`, `spec-auditor` and **`security-reviewer`**:
this spec records money and changes an Admin-only rule's reach.

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (the `/api/invoices` group open to any signed-in user, `Result` to `ProblemDetails`,
`Error.Conflict` to 409, the status judged before the body (007), field errors with camelCase keys reported together (005),
`TimeProvider` and the fixed UTC+03:00 "today", `InvoiceMoney`, `RowVersion` and the store's "touch the invoice row" rule (007),
decimals bound as `decimal?` and refused past two places, the derived flags the client trusts (`isEditable`), the form-screen pattern
with `applyProblemToForm` and the locked-page and 409-reload behaviour (005 to 007)) need no re-explanation. New in this spec:

1. **Payments belong to the invoice aggregate** — a `Payments` child table read and written through `Invoice` (like lines), never a
   separate service or store; `Invoice.RecordPayment` is the only way in (R1).
2. **A payment is a sub-resource route that answers with its parent** — `POST /api/invoices/{id}/payments`, body
   `{date, amount, method, note}`, `201 Created`, `Location: /api/invoices/{id}`, body = the whole updated invoice (so the screen has
   the new status, paid and remaining without a second call). There is no `GET`, `PUT` or `DELETE` on payments (R2).
3. **Sent to Paid becomes the fourth legal change, but a person never requests it** — it is in `InvoiceStatusRules`, but
   `allowedTransitions` never lists Paid and there is no "mark as paid" route: only a payment that settles the invoice causes it (R3).
4. **Remaining is calculated, Paid is stored** — `InvoiceMoney` gains one small method for the balance; the status change happens in the
   same save as the payment (R4).
5. **Cancel refuses an invoice with payments** — the one change to 007: `409 invoice_has_payments`; `allowedTransitions` drops
   Cancelled once a payment exists (R5).
6. **Concurrency through the invoice row** — a new payment forces the invoice row to be written, so the existing `RowVersion` gives one
   winner for payment/payment and payment/cancel (R6).
7. **Response additions** — `paid`, `remaining`, `payments[]` and `acceptsPayment` (the flag the client trusts, like `isEditable`) on
   the invoice; the list is unchanged (R7).
8. **Error contract** — `409 invoice_payment_not_allowed` naming the status, `409 invoice_has_payments`, `400` field errors on `date`,
   `amount`, `method`, `note` (R8).
9. **Angular** — a new lazy form screen `/invoices/:id/payments/new` built like the other forms, a payments section on the details
   screen, and a Paid badge; the browser only prefills today's date, the server judges it (R9).

## Project Structure

### Documentation (this feature)

```text
specs/008-payments/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── payments-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 007 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── PaymentMethod.cs                   # NEW: Cash, Bank, Other
│   ├── Payment.cs                         # NEW: entity (invoice id, date, amount, method, note, recorded time)
│   ├── InvoiceStatusRules.cs              # + Sent to Paid (the fourth legal change)
│   ├── InvoiceMoney.cs                    # + Balance(grandTotal, payments) -> paid, remaining
│   └── Invoice.cs                         # + Payments, Paid, Remaining, AcceptsPayment, AllowedTransitions,
│                                          #   RecordPayment (settles to Paid), Cancel refuses when payments exist
├── BillingCrm.Application/
│   └── Invoices/
│       ├── PaymentValidator.cs            # NEW: shape rules for the body (date, amount, method, note)
│       ├── InvoiceService.cs              # + RecordPaymentAsync; CancelAsync refuses when payments exist
│       └── InvoiceModels.cs               # + PaymentInput, PaymentDto; InvoiceDto: paid, remaining, payments, acceptsPayment,
│                                          #   allowedTransitions from the invoice
├── BillingCrm.Infrastructure/
│   ├── Invoices/EfInvoiceStore.cs         # load payments (split query); an invoice whose payments are written is written too
│   └── Persistence/
│       ├── BillingDbContext.cs            # + DbSet<Payment>
│       ├── Configurations/PaymentConfiguration.cs   # NEW: table, checks, index, no cascade
│       ├── Configurations/InvoiceConfiguration.cs   # + Payments navigation
│       └── Migrations/                    # AddPayments
└── BillingCrm.Api/
    └── Endpoints/InvoiceEndpoints.cs      # + POST {id}/payments

backend/tests/
├── BillingCrm.Application.Tests/          # PaymentTests (aggregate), PaymentValidatorTests, InvoiceServicePaymentTests, rules updated
├── BillingCrm.Api.Tests/                  # InvoicePaymentEndpointTests (+ access test updated)
└── BillingCrm.Infrastructure.Tests/       # migration/constraints, round trip, concurrency (payment/payment, payment/cancel)

frontend/src/app/
├── core/invoices/{invoice.models.ts, invoice.service.ts}   # Payment types, recordPayment(), new fields
├── features/invoices/
│   ├── payment-form.component.ts          # NEW: the Record payment form (route /invoices/:id/payments/new)
│   ├── invoice-detail.component.ts        # paid/remaining, payments list, Record payment link, Paid badge
│   └── invoice-list.component.ts          # Paid badge style
├── app.routes.ts                          # + invoices/:id/payments/new (lazy)
└── (spec files beside them)

README.md, CLAUDE.md                       # the migration step, the payment rules, the route, the layout (CLAUDE.md with the developer's OK)
```

**Structure Decision**: Extend the 007 layout in place. Two small Domain files for the new entity and enum; one new validator beside the
invoice validator; one new form component beside the other forms. No new projects, packages, services, stores or shared abstractions.

## Known gaps carried forward

- No refunds, credit notes or voiding (payments are permanent): a mistaken entry is handled outside the system; a later spec adds them,
  and would then also relax "an invoice with payments cannot be cancelled".
- No record of who recorded a payment (as for send and cancel).
- The one-payment-at-a-time race protection is the application plus `RowVersion`; the database itself cannot check that the payments
  add up to no more than the total (a check constraint cannot see other rows).
- Deferred to spec 012 (recorded): a deadlock victim on save is a 500, and a token's role is not re-checked during its lifetime.
