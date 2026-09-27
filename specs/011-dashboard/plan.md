# Implementation Plan: Dashboard

**Branch**: `011-dashboard` | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-dashboard/spec.md`

## Summary

Home gains five figures, computed live from the existing `Invoices` and `Payments` tables: a Draft count, an Unpaid count (Sent, on-time
and overdue alike), an Overdue count, a Paid-this-month count, and the SAR sum of unpaid invoices' remaining balances. Each card links to
the invoice list already filtered to exactly that set. No new table or column is added: this is a read over data the application already
stores, in the same spirit as 009 (invoice print) and 010 (invoice history), neither of which needed a migration either.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript / Angular 21, standalone components (frontend) — unchanged from every prior spec

**Primary Dependencies**: ASP.NET Core Minimal API, EF Core 8 (SQL Server provider), Angular Material — all already in the solution; no new package

**Storage**: SQL Server LocalDB. No new table, column or migration: the five figures are computed by querying the existing `Invoices` and
`Payments` tables each time Home loads

**Testing**: xUnit (`BillingCrm.Application.Tests`, `BillingCrm.Api.Tests`, `BillingCrm.Infrastructure.Tests`); Vitest (Angular, `ng test`)

**Target Platform**: Web (existing .NET 8 API + Angular 21 SPA)

**Project Type**: Web application (existing `backend/` + `frontend/` split)

**Performance Goals**: Home's dashboard query returns well within the same page-load budget as the rest of Home, at the invoice volume the
Infrastructure performance tests already exercise (5,000 invoices, `EfInvoiceStorePerformanceTests`); the five figures are simple counts and
one sum, not per-row work, so they scale the same way the invoice list's own count query already does

**Constraints**: Money stays `decimal(18,2)` SAR end to end (Constitution IV); no monetary value is ever a `float`/`double`; every figure is
computed fresh on each load, never cached or precomputed; a Cancelled invoice is never counted anywhere on the dashboard

**Scale/Scope**: One new read endpoint, four new optional filter values on the existing invoice list read, one new Angular section on the
existing Home screen and its service; no new screen, no new table

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Scope Discipline**: a read-only summary of existing invoices and a filter to reach them; no shop, cart, payment gateway, ticket or
  WhatsApp concept anywhere near it. **Pass.**
- **II. Sequential Spec Delivery**: 010 is verified running; this is next in sequence, depends only on 005, 007 and 008, all verified.
  **Pass.**
- **III. Explicit Business Errors**: the summary read has no business-rule failure to model (any signed-in user may always read it); the
  invoice list's new filter values are additive to an existing `Result`/`ProblemDetails` endpoint and introduce no new failure mode beyond
  what a bad query value already produces (400 on the field, as customers'/products' filters do). **Pass.**
- **IV. Financial Data Integrity**: the unpaid amount sum is `decimal(18,2)` SAR throughout, summed from figures (`InvoiceMoney`/`Balance`)
  the invoice already calculates; nothing is a `float`/`double`; no secret is touched. **Pass.**
- **V. Deliberate API/UI Design**: one thing here is a genuinely new *kind* of endpoint — a summary/aggregate read that is not shaped like
  any existing resource CRUD route — and needs the developer's confirmation before being generated (research R1, confirmed at task T001,
  following the same gate 006-010 each used). The invoice list's new filter values reuse the already-approved "typed query filters on a
  list read" pattern (customers' `type`/`active`, products' `unit`/`status`) and need no re-explanation. The Angular side reuses the
  existing "feature-scoped service owns the HTTP call" pattern (a new `core/dashboard/dashboard.service.ts`, matching every other
  `core/<domain>` service) and extends the existing `HomeComponent`, not a new architectural shape. **Pass, with one confirmation gate.**

## Project Structure

### Documentation (this feature)

```text
specs/011-dashboard/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── BillingCrm.Domain/            (no change: no new entity, no new state)
│   ├── BillingCrm.Application/
│   │   └── Dashboard/                DashboardService, DashboardSummaryDto, IInvoiceSummaryStore-shaped query methods
│   │       (or added to Invoices/ — decided in research R1)
│   ├── BillingCrm.Infrastructure/
│   │   └── Invoices/EfInvoiceStore.cs    gains the summary query and the new filtered SearchAsync overload/parameters
│   └── BillingCrm.Api/
│       └── Endpoints/                new GET /api/dashboard; existing InvoiceEndpoints.cs's GET /api/invoices gains filter query params
├── tests/
│   ├── BillingCrm.Application.Tests/     DashboardService tests (fakes), InvoiceService search-filter tests
│   ├── BillingCrm.Api.Tests/              dashboard endpoint tests, invoice list filter tests, route-list pin update
│   └── BillingCrm.Infrastructure.Tests/   real-SQL dashboard query tests, real-SQL filter tests

frontend/
├── src/app/
│   ├── core/dashboard/                new DashboardSummary model + DashboardService (GET /api/dashboard)
│   └── features/home/                 home.component.ts gains the five cards, each linking to /invoices with a query parameter
└── (invoice-list.component.ts reads the new query parameter to preselect its filter; no new screen)
```

**Structure Decision**: Existing `backend/` (Domain/Application/Infrastructure/Api) + `frontend/` (Angular) split, unchanged. The dashboard
read is a new Application service (or a method added to the existing `InvoiceService` — decided in research R1) behind one new endpoint;
the invoice list's existing endpoint and screen are extended, not replaced or duplicated.

## Complexity Tracking

*No constitution violation requires justification. The one Principle V confirmation gate (a new endpoint shape) is a normal, expected use
of that principle, not a violation of it.*
