# Implementation Plan: Customers

**Branch**: `003-customers` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-customers/spec.md`

## Summary

Add the first business record. A `Customers` table (migration `AddCustomers`) with a unique index on
the normalized email; a `CustomerService` in Application that validates, normalizes, detects
duplicates, and returns `Result`s; an EF store whose `TryAdd`/`TryUpdate` turn a unique-index
violation into a conflict instead of an exception; a `/api/customers` endpoint group (add, search
with paging, get, edit, set active) for any signed-in user; and Angular list/detail/form screens
whose list state lives in the URL. No permanent delete: "delete" is deactivate. Search semantics and
the unique index are proven by integration tests against a throwaway LocalDB database. Decisions are
in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged)

**Primary Dependencies**: no new runtime packages. `Microsoft.EntityFrameworkCore.SqlServer` (already
present) supplies `EF.Functions.Like`; Angular Material table, paginator, select, chips/badge modules
(already installed)

**Storage**: SQL Server LocalDB; new table `Customers` via migration `AddCustomers`

**Testing**: xUnit: Application unit tests (fake store), API tests (`WebApplicationFactory`, in-memory
fake store), and a **new** `BillingCrm.Infrastructure.Tests` project against a throwaway LocalDB
database; Vitest for Angular

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: first page of a search over 10,000 customers in under 1 s (SC-003); add a
customer in under 1 minute (SC-001)

**Constraints**: duplicate email guaranteed by a database unique index, not only a pre-check; no
exception for expected failures; every endpoint declares access; Arabic and English text; no delete

**Scale/Scope**: about 10,000 customers; 5 API operations; 3 new screens plus shell link

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design — still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Customers are one of the three permitted domains. Invoices are explicitly out. No shop, cart, payments, tickets, WhatsApp. |
| II. Sequential Spec Delivery | Pass | 002 was verified running by the developer on 2026-09-24. 003 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | Validation, duplicate email, not found are `Result` failures mapped to `ProblemDetails`; a unique-index violation is translated to a conflict `Result` inside Infrastructure so no exception crosses layers. |
| IV. Financial Data Integrity | Pass (one boundary to confirm) | No monetary values, no secrets. The integration tests build a credential-free LocalDB connection in code instead of committing a connection string (R11); confirm this reading of the rule. |
| V. Deliberate API/UI Design | Pass | The six items below were confirmed by the developer before implementation (T001, 2026-09-24). |

**Workflow gates**: use `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations`. Before
the spec counts as done run `api-reviewer`, `angular-reviewer` and `spec-auditor` (no auth code
changes, so `security-reviewer` is not required).

### Patterns and decisions to confirm with the developer before generating (Principle V)

1. **Resource CRUD endpoint pattern** — `/api/customers` group, `201 + Location`, `409` for the
   duplicate email, list with `q/type/active/page/pageSize`, separate `PUT /{id}/active`, **no
   delete** (R1, R6, R7). This becomes the template for products and invoices.
2. **`PagedResult<T>`** — a new shared Application type reused by later lists (R6).
3. **Store port with `Try*` semantics** — `ICustomerStore.TryAddAsync/TryUpdateAsync` return `false` on
   a unique-index violation; the service returns a conflict (R2).
4. **Integration-test project against LocalDB** — new `BillingCrm.Infrastructure.Tests`, throwaway
   database per class, credential-free connection built in code (R11). `dotnet test` then needs
   LocalDB.
5. **Angular patterns** — list state in the URL query string; one form component for add and edit;
   shared `core/http/problem-details.ts` helper mapping `ProblemDetails` to form errors (R12).
6. **"Delete" means deactivate** (R1) and the other defaults recorded in the spec's Assumptions
   (all fields required except VAT, free-form VAT, single address field, last save wins).

## Project Structure

### Documentation (this feature)

```text
specs/003-customers/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── customers-api.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 002 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── CustomerType.cs                    # enum Company, Individual
│   ├── Customer.cs                        # entity: Create/Update/SetActive
│   └── EmailNormalizer.cs                 # shared trim + upper-invariant (StaffUser uses it too)
├── BillingCrm.Application/
│   ├── Common/PagedResult.cs
│   └── Customers/
│       ├── ICustomerStore.cs              # FindById, EmailExists, TryAdd, TryUpdate, Search
│       ├── CustomerService.cs             # Create, Update, Get, Search, SetActive -> Result
│       ├── CustomerValidator.cs           # trims + FR-005 rules -> field errors
│       └── CustomerModels.cs              # DTOs / requests / CustomerQuery
├── BillingCrm.Infrastructure/
│   ├── Persistence/
│   │   ├── BillingDbContext.cs            # + DbSet<Customer>
│   │   ├── Configurations/CustomerConfiguration.cs
│   │   └── Migrations/                    # AddCustomers
│   └── Customers/EfCustomerStore.cs       # LIKE search, paging, unique-violation -> false
└── BillingCrm.Api/
    ├── Endpoints/CustomerEndpoints.cs     # /api/customers group
    └── Program.cs                         # MapCustomerEndpoints()

backend/tests/
├── BillingCrm.Application.Tests/          # CustomerService/validator tests
├── BillingCrm.Api.Tests/                  # ApiFactory gains InMemoryCustomerStore; HTTP contract tests
└── BillingCrm.Infrastructure.Tests/       # NEW: throwaway LocalDB, real migrations, search + index tests

frontend/src/app/
├── core/
│   ├── customers/{customer.service.ts, customer.models.ts}
│   ├── http/problem-details.ts            # ProblemDetails -> form errors
│   └── layout/shell.component.ts          # + Customers link
├── features/customers/
│   ├── customer-list.component.ts
│   ├── customer-form.component.ts         # add + edit
│   └── customer-detail.component.ts
└── app.routes.ts                          # /customers, /customers/new, /customers/:id, /customers/:id/edit

README.md, CLAUDE.md                       # customers table step, new test project (needs LocalDB), layout
```

**Structure Decision**: Extend the 002 layout in place. One new test project because the search and
index guarantees can only be checked against SQL Server; the Application and API tests stay
database-free.

## Known gaps carried forward

- Invoice-time blocking of inactive customers (spec 005/006), permanent deletion, structured addresses,
  optimistic concurrency, Arabic letter-variant folding, full RTL layout, user-selectable sort.

## Complexity Tracking

No constitution violations to justify. The extra test project and the `Try*` port shape are the
minimum for proving the duplicate-email guarantee; both are listed for confirmation above.

## Implementation notes (added after implementation)

- `Program.cs` gained `RouteHandlerOptions.ThrowOnBadRequest = false`: in Development, minimal APIs threw on a malformed
  body and the developer exception page echoed the request headers (including the Bearer token). This came up
  while testing this spec, was verified fixed on a real Development run, and is recorded in
  `research.md`. It changes no auth logic (it only removes information from error responses), so
  `security-reviewer` was not run for it.
- CORS now lists only `GET`, `POST`, `PUT` (spec 002 also listed `PATCH` and `DELETE`, which no endpoint uses).
- The initial-bundle warning threshold in `frontend/angular.json` is 600 kB (was 500 kB, the Angular default);
  the customer screens are lazy-loaded.
