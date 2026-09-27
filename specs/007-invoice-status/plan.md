# Implementation Plan: Invoice Status

**Branch**: `007-invoice-status` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/007-invoice-status/spec.md`

## Summary

Give an invoice a life. The stored status widens from `Draft` to `Draft`, `Sent`, `Paid`, `Cancelled` (migration
`AddInvoiceStatuses`), and the rules live in one place, the Domain: a small transition table (`InvoiceStatusRules`) and two
aggregate methods, `Send` and `Cancel`, plus a guard that makes every edit of a non-Draft invoice impossible. **Overdue is never
stored**: it is `Sent` with a due date before today (Saudi date), worked out when an invoice is read and returned as `isOverdue`.
Leaving Draft (sent or cancelled) **freezes the amounts**: the five totals and each line's net, discount share and VAT are written
into their own columns at that moment, and a non-Draft invoice reads those stored numbers, so nothing later (a product change, a
change of the calculation rules) can move an issued invoice. An `InvoiceService` gains `SendAsync` and `CancelAsync`; two new routes,
`POST /api/invoices/{id}/send` (any signed-in user) and `POST /api/invoices/{id}/cancel` (**Admin only**, so an Agent gets 403 before
anything is looked at); illegal changes and edits of a locked invoice are `409`. A `RowVersion` on `Invoices` makes any two actions
that touch the same invoice at the same instant end with exactly one winner. Angular shows the status (including Overdue), offers
Send, Cancel and Edit only when legal and allowed (a two-step inline confirmation, no dialog), and shows a message instead of the
edit form for a locked invoice. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new packages. EF Core 8 concurrency tokens and check constraints are built in

**Storage**: SQL Server LocalDB; migration `AddInvoiceStatuses`: `Invoices.SentAtUtc`, `CancelledAtUtc`, four frozen total columns,
`RowVersion`; `InvoiceLines` three frozen amount columns; the status check widened; two new consistency checks

**Testing**: xUnit Application tests (the whole transition table, Overdue at the Saudi day boundary, freezing, service refusals with
fakes), API tests (`WebApplicationFactory`, in-memory stores; 401, 403-before-404, 404, 409, 400), Infrastructure tests on a
throwaway LocalDB database (migration, check constraints, frozen round trip, concurrent send/send and edit/send), Vitest for Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: none new. The list still reads one page of rows; `isOverdue` is worked out in memory per row

**Constraints**: the rules exist once (Domain); a refused request changes nothing; no exception for expected failures; the role is
checked before the invoice is looked at; "today" is the fixed UTC+03:00 date; amounts of a non-Draft invoice come only from the frozen
columns; exactly one of two simultaneous actions wins; `decimal(18,2)` only, no `float`/`double`

**Scale/Scope**: 2 new routes; 1 migration; 1 new Domain type (`InvoiceStatusRules`) and 3 new aggregate members; 1 concurrency
token; details, list and edit screens change, no new screen

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design, still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Invoices are a permitted domain. No payments (nothing makes an invoice Paid), no email/PDF delivery, no reasons, history, reminders, filters, shop, cart, tickets or WhatsApp (FR-015). |
| II. Sequential Spec Delivery | Pass | 006 was verified running by the developer on 2026-09-24 and closed. 007 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | Not found, illegal transition, locked edit and "changed by someone else" are `Result` conflicts/not-found mapped to `ProblemDetails` (404/409); a past due date on send is a 400 field error; an Agent cancelling is the existing policy 403. Only a defect in the state machine itself (an edit reaching a non-Draft aggregate) throws. |
| IV. Financial Data Integrity | Pass | Frozen amounts are `decimal(18,2)` SAR, written from the same `InvoiceMoney` calculation at the moment of leaving Draft, never `float`/`double`; consistency checks stop a non-Draft row without them. No secrets. |
| V. Deliberate API/UI Design | Pass | The new shapes below are to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `minimal-api-conventions` (two new routes), `angular-ui-conventions` (hide what the role cannot call; the API
still enforces 403), `sql-migrations` (one new migration, never edit an applied one) and `invoice-money` (the frozen amounts are the
calculator's output, stored). Before the spec counts as done run `api-reviewer`, `angular-reviewer` and `spec-auditor`, and also
**`security-reviewer`**: this spec adds an Admin-only operation and a role-dependent screen.

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (the `/api/invoices` group open to any signed-in user, the mixed-role group with `AdminOnly` on the
sensitive operations from products, `Result` to `ProblemDetails`, `Error.Conflict` to 409, `TimeProvider` and the fixed UTC+03:00
"today", `InvoiceMoney`, the 409 "changed by someone else" from 005, role gating with `auth.role()`) need no re-explanation. New in
this spec:

1. **Two action routes**, not a status body — `POST /api/invoices/{id}/send` and `POST /api/invoices/{id}/cancel`, no request body.
   Cancel adds the `AdminOnly` policy on top of the group, so an Agent is refused before the id is even looked up. This is a new
   endpoint shape (an action on a resource) (R1).
2. **The state machine lives in the Domain** — `InvoiceStatusRules` (the legal changes as a small table), `Invoice.Send`,
   `Invoice.Cancel`, and a guard so any edit of a non-Draft aggregate throws (a bug, since the service refuses first) (R2).
3. **Overdue is derived, not stored** — `isOverdue` is returned by the API; the stored status stays Draft/Sent/Paid/Cancelled (R3).
4. **Amounts are frozen when an invoice leaves Draft** — stored in new columns on the invoice and its lines; non-Draft invoices read
   them instead of calculating (R4).
5. **`RowVersion` on `Invoices` (optimistic concurrency)** — any two changes to the same invoice at the same instant: one wins, the
   other is a 409. This also closes the 006 review gap (a discount left above the net by two edits) and **replaces 005's "last save
   wins" for Drafts** (R5).
6. **Response additions** — `isOverdue`, `sentAtUtc`, `cancelledAtUtc`, `allowedTransitions` (what the status permits, before role),
   `isEditable`; the list rows gain `isOverdue` (R6).
7. **Error contract** — illegal change and locked edit are 409 naming the current status; sending a Draft whose due date is in the past
   is a 400 on `dueDate` (R7).
8. **Angular** — status badge shows Overdue; Send/Cancel/Edit offered only when legal and allowed; an inline two-step confirmation
   (no dialog component); a locked invoice's edit address shows a message, not the form (R8).

## Project Structure

### Documentation (this feature)

```text
specs/007-invoice-status/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── invoice-status-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 006 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── InvoiceStatus.cs                   # Draft, Sent, Paid, Cancelled
│   ├── InvoiceStatusRules.cs              # NEW: legal changes table, CanChange, NextStatuses
│   ├── Invoice.cs                         # + SentAtUtc, CancelledAtUtc, RowVersion, IsOverdue(today), Send, Cancel,
│   │                                      #   freeze on leaving Draft, edit guard, Amounts reads frozen when not Draft
│   └── InvoiceLine.cs                     # + frozen net / discount share / VAT (internal Freeze)
├── BillingCrm.Application/
│   └── Invoices/
│       ├── InvoiceService.cs              # + SendAsync, CancelAsync; UpdateAsync refuses non-Draft (409) first
│       └── InvoiceModels.cs               # DTO: isOverdue, sentAtUtc, cancelledAtUtc, allowedTransitions, isEditable;
│                                          #   summary: isOverdue
├── BillingCrm.Infrastructure/
│   └── Persistence/
│       ├── Configurations/InvoiceConfiguration.cs      # new columns, RowVersion, widened + new checks
│       ├── Configurations/InvoiceLineConfiguration.cs  # frozen columns
│       └── Migrations/                    # AddInvoiceStatuses
└── BillingCrm.Api/
    └── Endpoints/InvoiceEndpoints.cs      # + POST {id}/send, POST {id}/cancel (AdminOnly)

backend/tests/
├── BillingCrm.Application.Tests/          # InvoiceStatusRulesTests, InvoiceStatusTests (aggregate), InvoiceServiceStatusTests
├── BillingCrm.Api.Tests/                  # InvoiceStatusEndpointTests (+ access test updated)
└── BillingCrm.Infrastructure.Tests/       # migration/constraints, frozen round trip, concurrency

frontend/src/app/
├── core/invoices/{invoice.models.ts, invoice.service.ts}         # send(), cancel(), new fields
├── features/invoices/
│   ├── invoice-detail.component.ts        # status badge incl. Overdue, Send/Cancel/Edit gating, inline confirm, messages
│   ├── invoice-list.component.ts          # status shows Overdue
│   └── invoice-form.component.ts          # locked invoice shows a message instead of the form
└── (spec files beside them)

README.md, CLAUDE.md                       # the migration step, the status rules, the two routes, the layout
```

**Structure Decision**: Extend the 006 layout in place. One new Domain file for the transition table; everything else changes an
existing file. No new projects, packages, screens or Angular components.

## Known gaps carried forward

- Payments and a way to become Paid, credit notes, reopening or un-sending, a cancellation reason and history of who did what, PDF/email
  delivery, status filters and search on the list, reminders.
- The decimal-literal converter, `StaffOnly` policy and Development header leak deferred to 012-security still apply.

## Complexity Tracking

No constitution violations to justify. The new shapes (action routes, frozen amounts, row version) are each the minimum for the spec's
rules ("a sent invoice never changes", "exactly one of two simultaneous actions wins", "an Agent cannot cancel") and are listed for
confirmation above.
