# Implementation Plan: Foundation

**Branch**: `001-foundation` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-foundation/spec.md`

## Summary

Stand up an empty but running BillingCRM: a four-layer .NET 8 back end (Domain, Application,
Infrastructure, Api) exposing an anonymous `GET /health` that also checks the LocalDB database
through EF Core, restricted-origin CORS for the Angular dev server, and an Angular standalone +
Material shell whose home page calls `/health` and shows the result. No business tables, no
login. Connection string stays out of git (user-secrets / env var, fail-fast when missing). A
README documents setup, and automated back-end and front-end tests prove the acceptance
scenarios. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8 (SDK 8.0.403); TypeScript on Angular 21 (CLI 21.2.8, Node 24)

**Primary Dependencies**: ASP.NET Core 8 Minimal API; EF Core 8 (`SqlServer`, `Design`);
`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 8; Angular Material

**Storage**: SQL Server LocalDB (`MSSQLLocalDB`), database `BillingCrm`; no tables beyond EF's
migration history

**Testing**: xUnit + `Microsoft.AspNetCore.Mvc.Testing`; Vitest via the Angular CLI's
`@angular/build:unit-test` builder (the CLI 21 default)

**Target Platform**: Developer workstation (Windows), localhost only

**Project Type**: Web application (back end + front end)

**Performance Goals**: `/health` responds in under 2 s when healthy (SC-002)

**Constraints**: No secrets in git; CORS never `AllowAnyOrigin`; `/health` anonymous; no
`Result` type or auth yet

**Scale/Scope**: 4 .NET projects + 1 test project, 1 Angular workspace, 2 screens' worth of UI
(shell + home), 1 endpoint

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design — still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | No customers/products/invoices; none of shop, cart, payments, tickets, WhatsApp. |
| II. Sequential Spec Delivery | Pass | This is spec 001. It is done only when verified running locally (quickstart) and `api-reviewer` has run. The `/health` question (R1) was resolved by the developer on 2026-09-24. |
| III. Explicit Business Errors | Pass | No business errors exist yet, so no `Result` type is introduced. Start-up misconfiguration throws (genuinely unexpected). Unhealthy DB is an infrastructure state, returned as 503 (see [contracts/health.md](contracts/health.md)). |
| IV. Financial Data Integrity | Pass | No monetary values. Connection string absent from all committed files. |
| V. Deliberate API/UI Design | Pass | The three patterns below were explained and confirmed by the developer before implementation (T001, 2026-09-24). |

**Workflow gates**: use `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations`
skills for the respective edits; run `api-reviewer` (and `angular-reviewer`, since UI changes)
before marking the spec done.

### Patterns to confirm with the developer before generating (Principle V)

These are new shapes in an empty repo, so each is explained and confirmed before code is written:

1. **Layer composition**: each layer exposes one `AddXxx(IServiceCollection[, IConfiguration])`
   extension; `Program.cs` only calls them. Dependency direction as in research R6.
2. **Health endpoint**: framework health-check services mapped at `/health` with a custom JSON
   writer (research R2), instead of a hand-written `MapGet`. Per the Minimal API skill, the
   endpoint will be explained in 5 lines using MVC terms.
3. **Angular structure**: `core/` (shell layout, `HealthService` as the "HttpClient service per
   resource"), `features/home/`, an `environments` file for the API base URL, no interceptor
   yet (auth interceptor arrives with spec 002).

## Project Structure

### Documentation (this feature)

```text
specs/001-foundation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── health.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created later by /speckit-tasks
```

### Source Code (repository root)

```text
.config/
└── dotnet-tools.json                 # pins dotnet-ef 8.x

backend/
├── BillingCrm.sln
├── src/
│   ├── BillingCrm.Domain/            # no references; assembly marker only
│   ├── BillingCrm.Application/       # → Domain; AddApplication() (empty)
│   ├── BillingCrm.Infrastructure/    # → Application; BillingDbContext, AddInfrastructure(),
│   │                                 #   DB health check registration, config validation
│   └── BillingCrm.Api/               # → Application, Infrastructure; Program.cs, CORS,
│                                     #   /health mapping, appsettings.json (no secrets),
│                                     #   launchSettings (http://localhost:5080), UserSecretsId
└── tests/
    └── BillingCrm.Api.Tests/         # xUnit + WebApplicationFactory: health, CORS, config

frontend/                             # Angular workspace (standalone, Angular Material)
├── src/
│   ├── app/
│   │   ├── core/                     # shell layout component, health.service.ts
│   │   └── features/home/            # home page: loading / healthy / error states
│   └── environments/                 # apiBaseUrl = http://localhost:5080
└── (component tests beside the shell and home page)

README.md                             # setup, secrets, run, health check, tests
.gitignore                            # extended: bin/, obj/, node_modules/, .angular/, dist/
```

**Structure Decision**: Web-application layout with `backend/` and `frontend/` at the repo root.
`CLAUDE.md` said "no `src/`, solution, or Angular workspace exists"; it was updated in T037. Once this spec is
implemented it (layout and run commands) must be updated with the real paths, as that file
itself requests.

## Complexity Tracking

No constitution violations; nothing to justify. The four-project back end is explicitly
requested by the spec (FR-001).
