# Implementation Plan: Invoice History

**Branch**: `010-invoice-history` | **Date**: 2026-09-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-invoice-history/spec.md`

## Summary

Give every invoice a permanent, readable history. A new table `InvoiceHistoryEntries` holds one row per change: its kind (Created, Draft
edited, Sent, Payment, Cancelled), the time, the **user** (email and role, taken from the signed-in user's token, never from a request) and
the list of changes as **text values as they were at that moment**, each with a before and an after. The entry is added to the same
database context as the change and is saved by the **same `SaveChanges`** (one transaction), so a saved change always has its entry, and a
refused change, a validation error or a lost race leaves none (FR-002). The invoice aggregate does **not** own or load its history (it grows
without bound and is only ever appended and read): the store gains `RecordHistory(entry)`, `ListHistoryAsync(invoiceId)` and
`FindNumberAsync(id)`; an Application-level builder turns "before and after" of each of the five events into the change list; and a new port,
`ICurrentUser`, tells `InvoiceService` who is acting (implemented in the API from the JWT claims). One new read-only route,
`GET /api/invoices/{id}/history` (any signed-in user), answers newest first with a flag saying whether the invoice's history begins with its
creation (older invoices do not, and the screen says so). **There is no route, method or screen that writes, edits or deletes an entry**;
and as defence in depth the database itself refuses `UPDATE` and `DELETE` on the table with an `INSTEAD OF` trigger. Angular gets one new
screen, `/invoices/:id/history`, and a **History** link on the details. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new packages. EF Core 8 (`HasTrigger`, an identity column), `IHttpContextAccessor` (built in), `System.Text.Json`

**Storage**: SQL Server LocalDB; migration `AddInvoiceHistory`: table `InvoiceHistoryEntries` (`Id`, `Sequence` identity, `InvoiceId` foreign
key without cascade, `Kind`, `OccurredAtUtc`, `ActorEmail`, `ActorRole`, `ChangesJson`), check constraints (kind, role, `ISJSON`), an index on
(`InvoiceId`, `Sequence`), and the trigger `TR_InvoiceHistoryEntries_Immutable`. `Invoices` is unchanged

**Testing**: xUnit Application tests (the builder for each event, the service writing exactly one entry per saved change and none per refusal
with fakes that model the all-or-nothing save, the actor from `ICurrentUser`, values as at the time), API tests (`WebApplicationFactory`, in-memory
stores; the actor is the token's user for Admin and Agent, 401, 404, order, the flag, the pinned route list with exactly one new `GET`), Infrastructure tests on a
throwaway LocalDB database (migration objects, the trigger refusing `UPDATE` and `DELETE` even from raw SQL, entry and change saved together and a lost race
leaving neither, order by `Sequence`), Vitest for Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end)

**Performance Goals**: none new. One indexed query per History screen; the invoice load is unchanged (history is not loaded with it)

**Constraints**: an entry exists exactly when its change was saved (same transaction); no operation anywhere updates or deletes an entry; the
user comes from the token only; values are stored as text or exact two-decimal SAR **as at the time** and are never recalculated, rounded or
re-read from a customer, product or user; expected failures are `Result` -> `ProblemDetails`; `decimal(18,2)` money only, no `float`/`double`

**Scale/Scope**: 1 new route (`GET`), 1 migration (1 table, 1 trigger), 3 new Domain types, 1 Application builder, 1 new port (`ICurrentUser`), the
five write paths of `InvoiceService` each gain one entry, 1 new screen and 1 link; no new package

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design, still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Invoices are a permitted domain. No history for customers, products or staff, no cross-invoice list, search, export, undo, comments, purge, backfill, shop, cart, payment gateway, tickets or WhatsApp (FR-017). |
| II. Sequential Spec Delivery | Pass | 009 was verified running by the developer on 2026-09-26 and closed. 010 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | The one new read answers `404 invoice_not_found` (an existing `Result` error) or `200`; nothing new throws for an expected outcome. A missing signed-in user inside `ICurrentUser` is a defect (the endpoints require sign-in) and throws. |
| IV. Financial Data Integrity | Pass | Money in an entry is the exact two-decimal SAR value as text, taken from the already-calculated invoice (the frozen or drafted figures and `Invoice.Paid`/`Remaining`); nothing is recalculated, no `float`/`double`. No secrets. |
| V. Deliberate API/UI Design | Pass | The new shapes below are to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `minimal-api-conventions` (one new `GET`), `angular-ui-conventions` (one new screen with the four states), `sql-migrations`
(one new migration, never edit an applied one, the trigger is raw SQL in the migration) and `invoice-money` (values are the server's exact
figures as text). Before the spec counts as done run `api-reviewer`, `angular-reviewer`, `spec-auditor` and **`security-reviewer`** (an audit
trail, a trigger, the actor taken from a token, a new read of financial history).

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (the `/api/invoices` group open to any signed-in user, `Result` to `ProblemDetails`, `TimeProvider`, the
invoice store and its all-or-nothing `SaveChangesAsync` with the row version, `InvoiceDto`-style read DTOs, a lazy Angular screen with the
four states, `data-testid`s, plain text interpolation) need no re-explanation. New in this spec:

1. **History entries live outside the invoice aggregate** — a separate table written through the store (`RecordHistory`) in the same save as
   the change, listed by a new store read; the aggregate does not own or load them (R1).
2. **A current-user port** — `ICurrentUser` in the Application layer (the signed-in user's email and role), implemented in the API from the JWT
   claims and injected into `InvoiceService`; the actor is never a request field. This is a new shared abstraction and adds a constructor
   argument to `InvoiceService` (R2).
3. **Changes stored as JSON text in one column** — a list of `{field, before, after}` where every value is text as at the time; no child table,
   no links to customers, products or users (R3).
4. **A database trigger that refuses `UPDATE` and `DELETE`** on the history table (`INSTEAD OF`, raising an error), created by the migration —
   the first trigger in the database (raw SQL in the migration, as `NoCacheInvoiceNumbers` already does for its sequence); EF is told about it with `HasTrigger` (R5).
5. **A read route and a screen** — `GET /api/invoices/{id}/history` (newest first, with a `startsWithCreation` flag) and the screen
   `/invoices/:id/history` under the signed-in shell, reached from a **History** link on the details (R6, R8).

## Project Structure

### Documentation (this feature)

```text
specs/010-invoice-history/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── invoice-history-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 009 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── HistoryKind.cs                     # NEW: Created, DraftEdited, Sent, Payment, Cancelled
│   ├── HistoryChange.cs                   # NEW: record (Field, Before, After), all text
│   └── InvoiceHistoryEntry.cs             # NEW: immutable entry (no setters, no methods), Create(...)
├── BillingCrm.Application/
│   ├── Auth/ICurrentUser.cs               # NEW: the signed-in user's email and role
│   └── Invoices/
│       ├── InvoiceHistoryBuilder.cs       # NEW: pure: before/after of each event -> the entry's changes
│       ├── IInvoiceStore.cs               # + RecordHistory, ListHistoryAsync, FindNumberAsync
│       ├── InvoiceService.cs              # + ICurrentUser; one entry per saved change; GetHistoryAsync
│       └── InvoiceModels.cs               # + InvoiceHistoryDto, HistoryEntryDto, HistoryChangeDto
├── BillingCrm.Infrastructure/
│   ├── Invoices/EfInvoiceStore.cs         # + RecordHistory, ListHistoryAsync, FindNumberAsync
│   └── Persistence/
│       ├── BillingDbContext.cs            # + DbSet<InvoiceHistoryEntry>
│       ├── Configurations/InvoiceHistoryEntryConfiguration.cs   # NEW: table, checks, index, HasTrigger, JSON conversion
│       └── Migrations/                    # AddInvoiceHistory (table + trigger)
└── BillingCrm.Api/
    ├── Auth/HttpContextCurrentUser.cs     # NEW: ICurrentUser from the JWT claims (sub is not needed: email and role)
    ├── Program.cs                         # + AddHttpContextAccessor, AddScoped<ICurrentUser, HttpContextCurrentUser>
    └── Endpoints/InvoiceEndpoints.cs      # + GET {id}/history

backend/tests/
├── BillingCrm.Application.Tests/          # InvoiceHistoryBuilderTests, InvoiceServiceHistoryTests, fakes updated (store, current user)
├── BillingCrm.Api.Tests/                  # InvoiceHistoryEndpointTests (+ access test updated, in-memory store updated)
└── BillingCrm.Infrastructure.Tests/       # EfInvoiceHistoryTests (migration, trigger, atomicity, order), MigrationSmokeTests

frontend/src/app/
├── core/invoices/{invoice.models.ts, invoice.service.ts}     # history types and history(id)
├── features/invoices/
│   ├── invoice-history.component.ts       # NEW: the History screen
│   └── invoice-detail.component.ts        # + History link
├── app.routes.ts                          # + invoices/:id/history (lazy, under the shell)
└── (spec files beside them)

README.md, CLAUDE.md                       # the migration step, the History screen and route, the rules (listed tasks, as for 007 to 009)
```

**Structure Decision**: Extend the 009 layout in place. Three small Domain types, one Application builder and one port, one API adapter for
the port, one store extension, one screen. No new project or package. The existing tests that construct `InvoiceService` (fifteen places) gain
one argument, a fake `ICurrentUser`.

## Known gaps carried forward

- Older invoices have no earlier history and no backfill (Clarifications); their History says so.
- The user is kept as email and role only (no user id), as the spec says; an account that is renamed later is a different email in later entries.
- Someone with the database's own administrator rights can drop the trigger or the table: the trigger stops the application and casual
  direct edits, not a determined administrator. Nothing in this system prevents that; it is recorded, not solved.
- No paging (an invoice's life is bounded), no retention or purge, and no history for customers, products or staff.
- The times on the History screen follow the details' time-zone behaviour (the viewer's browser), the open question left from 009.
