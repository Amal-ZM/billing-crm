---

description: "Task list for 001-foundation"
---

# Tasks: Foundation

**Input**: Design documents from `/specs/001-foundation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/health.md, quickstart.md

**Tests**: Included. FR-013 requires an automated health test and a shell-render test, and the
plan's contract tests (contracts/health.md) map to test tasks below.

**Organization**: Grouped by user story (US1 run end to end, US2 README onboarding + fail-fast
config, US3 database connectivity proven).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

Web application layout from plan.md: `backend/src/BillingCrm.*`, `backend/tests/`, `frontend/`.
All commands run from the repository root unless a task says otherwise.

**Project rules that apply to every task**: use the `minimal-api-conventions`,
`angular-ui-conventions` and `sql-migrations` skills for the matching edits; no connection
string, key or credential in any committed file (even test fixtures must use an obviously fake
value that is not a real server); no `float`/`double` money (none exists yet).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution, projects, tooling. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating any code, explain to the developer and get confirmation on the three patterns in plan.md ("Patterns to confirm"): layer `AddXxx` extensions, `/health` via built-in health checks + custom writer, Angular `core/` + `features/` + environments structure. Also get the answer to the open `/health` database question (spec Clarifications); if the answer is B or C, adjust T019, T020, T033 and User Story 3 before proceeding. No files are produced; record the outcome in `specs/001-foundation/research.md` (R1)
- [X] T002 Create `backend/BillingCrm.sln` and the four projects targeting `net8.0`: classlibs `backend/src/BillingCrm.Domain`, `backend/src/BillingCrm.Application`, `backend/src/BillingCrm.Infrastructure`, and an empty web project `backend/src/BillingCrm.Api` (use `dotnet new`, then `dotnet sln add`); delete template sample code such as any WeatherForecast endpoint
- [X] T003 Add project references in the `.csproj` files: Application → Domain; Infrastructure → Application; Api → Application and Infrastructure (Domain references nothing) in `backend/src/*/*.csproj`
- [X] T004 [P] Create test project `backend/tests/BillingCrm.Api.Tests` (xUnit, `Microsoft.AspNetCore.Mvc.Testing` 8.x) referencing `BillingCrm.Api`; add to `backend/BillingCrm.sln`
- [X] T005 [P] Extend `.gitignore` with `bin/`, `obj/`, `.vs/`, `TestResults/`, `node_modules/`, `.angular/`, `dist/`, keeping the existing secrets/local-override entries
- [X] T006 [P] Create repo-local tool manifest `.config/dotnet-tools.json` pinning `dotnet-ef` 8.x (`dotnet new tool-manifest` then `dotnet tool install dotnet-ef --version 8.*`)
- [X] T007 [P] Generate the Angular workspace in `frontend/` with the installed CLI (standalone components, no SSR, routing on), then `ng add @angular/material` and `ng generate environments` so `frontend/src/environments/` exists
- [X] T008 Add NuGet packages (8.x): `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` to `BillingCrm.Infrastructure`; `Microsoft.EntityFrameworkCore.Design` to `BillingCrm.Api`; add a `UserSecretsId` (a GUID, not a secret) to `backend/src/BillingCrm.Api/BillingCrm.Api.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Layers exist, compose, and the solution builds.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T009 [P] Add an assembly marker type in `backend/src/BillingCrm.Domain/AssemblyReference.cs` (Domain stays otherwise empty)
- [X] T010 [P] Add `AddApplication(this IServiceCollection)` with no registrations yet in `backend/src/BillingCrm.Application/DependencyInjection.cs`
- [X] T011 [P] Create the empty `BillingDbContext` (no `DbSet`s) in `backend/src/BillingCrm.Infrastructure/Persistence/BillingDbContext.cs`
- [X] T012 Add `AddInfrastructure(this IServiceCollection, IConfiguration)` registering `BillingDbContext` with the SQL Server provider, reading `ConnectionStrings:DefaultConnection`, migrations assembly = Infrastructure, in `backend/src/BillingCrm.Infrastructure/DependencyInjection.cs` (depends on T011)
- [X] T013 Wire `backend/src/BillingCrm.Api/Program.cs` to call `AddApplication()` and `AddInfrastructure(configuration)`; create `backend/src/BillingCrm.Api/appsettings.json` with logging defaults and `Cors:AllowedOrigins` = `["http://localhost:4200"]` and **no** connection string; set the launch profile to `http://localhost:5080` in `backend/src/BillingCrm.Api/Properties/launchSettings.json` (depends on T010, T012)

**Checkpoint**: `dotnet build backend/BillingCrm.sln` succeeds with no warnings about references.

---

## Phase 3: User Story 1 - Run the empty BillingCRM end to end (Priority: P1) MVP

**Goal**: API `/health` and Angular home both succeed with the browser not blocked by CORS.

**Independent Test**: Run the API and `ng serve`; open `http://localhost:5080/health` (200) and
`http://localhost:4200` (shell + "Healthy" status, no console errors).

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [X] T014 [P] [US1] Create `WebApplicationFactory` fixture in `backend/tests/BillingCrm.Api.Tests/ApiFactory.cs` that supplies an obviously fake, non-secret in-memory connection string (so tests never touch LocalDB) and lets a test swap the `database` health check for a fake healthy/unhealthy one
- [X] T015 [P] [US1] Health test in `backend/tests/BillingCrm.Api.Tests/HealthEndpointTests.cs`: healthy fake check → `GET /health` is 200 with `status` = `Healthy`, `checks.database` = `Healthy`; request has no `Authorization` header (contracts/health.md tests 1 and 3)
- [X] T016 [P] [US1] CORS tests in `backend/tests/BillingCrm.Api.Tests/CorsTests.cs`: `Origin: http://localhost:4200` gets `Access-Control-Allow-Origin` echoed; `Origin: http://evil.example` gets none (contracts/health.md test 4)
- [X] T017 [P] [US1] Shell component test beside the shell in `frontend/src/app/core/layout/shell.component.spec.ts`: renders a toolbar containing the product name "BillingCRM" and a content outlet
- [X] T018 [P] [US1] Home page test in `frontend/src/app/features/home/home.component.spec.ts` using a stubbed `HealthService`: shows a loading state, then "Healthy"; on error shows the "API unreachable" state and the page does not crash

### Implementation for User Story 1

- [X] T019 [US1] Register the database health check (`AddHealthChecks().AddDbContextCheck<BillingDbContext>("database")`) inside `AddInfrastructure` in `backend/src/BillingCrm.Infrastructure/DependencyInjection.cs`
- [X] T020 [US1] Add `MapHealthEndpoint` in `backend/src/BillingCrm.Api/Endpoints/HealthEndpoint.cs`: `MapHealthChecks("/health")` with a JSON response writer emitting only `status` and `checks.database` (no exception text, no connection details), `.AllowAnonymous()`; call it from `Program.cs` (depends on T019, T013)
- [X] T021 [US1] After T020, explain the `/health` endpoint to the developer in 5 lines using MVC words (per `minimal-api-conventions` skill); no file output
- [X] T022 [US1] Add the CORS policy in `backend/src/BillingCrm.Api/Program.cs` (or an `Extensions/CorsExtensions.cs`): origins read from `Cors:AllowedOrigins`, never `AllowAnyOrigin`, no credentials; apply with `UseCors` before the endpoints (depends on T013)
- [X] T023 [P] [US1] Set `apiBaseUrl: 'http://localhost:5080'` in `frontend/src/environments/environment.ts` (and `environment.development.ts` if generated)
- [X] T024 [P] [US1] Create `frontend/src/app/core/health/health.service.ts`: an `HttpClient` service calling `GET {apiBaseUrl}/health` and exposing the parsed `status` (depends on T023)
- [X] T025 [P] [US1] Create the shell in `frontend/src/app/core/layout/shell.component.ts`: standalone, Angular Material toolbar showing "BillingCRM" and a `router-outlet` main area
- [X] T026 [US1] Create `frontend/src/app/features/home/home.component.ts`: standalone Material card that calls `HealthService` and renders loading / healthy / "API unreachable" states (depends on T024)
- [X] T027 [US1] Wire `frontend/src/app/app.routes.ts` (home route inside the shell), `frontend/src/app/app.config.ts` (`provideHttpClient`, router, animations as required by the installed Material version) and the root component template to use the shell (depends on T025, T026)
- [X] T028 [US1] Run `dotnet test backend` and `ng test --watch=false` in `frontend/`; fix until T015–T018 pass

**Checkpoint**: US1 acceptance scenarios 1–3 pass via tests; manual browser check happens in T041.

---

## Phase 4: User Story 2 - Onboard from the README alone (Priority: P2)

**Goal**: A new developer reaches the running state from the README, and a missing setting fails
clearly.

**Independent Test**: Start the API with no connection string and get a clear start-up message;
follow the README on a clean clone to a running system.

### Tests for User Story 2

- [X] T029 [P] [US2] Start-up config test in `backend/tests/BillingCrm.Api.Tests/MissingConnectionStringTests.cs`: a factory with no connection string fails to start with an `InvalidOperationException` whose message names `ConnectionStrings:DefaultConnection` and contains no connection value

### Implementation for User Story 2

- [X] T030 [US2] In `AddInfrastructure` (`backend/src/BillingCrm.Infrastructure/DependencyInjection.cs`) throw `InvalidOperationException` naming `ConnectionStrings:DefaultConnection` (and pointing at the README section) when the value is missing or blank; never echo a value (depends on T019 to avoid same-file conflicts; T014's factory already supplies a fake string so US1 tests are unaffected)
- [X] T031 [US2] Write `README.md` at the repo root covering: prerequisites; supplying the connection string with `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your LocalDB connection string>" --project backend/src/BillingCrm.Api` (placeholder only, no real string) or the `ConnectionStrings__DefaultConnection` env var; `dotnet tool restore` and `dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api`; running the API (`http://localhost:5080`) and `ng serve` (`http://localhost:4200`); opening `/health` and the home page; running `dotnet test backend` and `ng test`; troubleshooting the missing-setting message and the 503-until-database-exists behaviour
- [X] T032 [US2] Dry-run the README literally on a fresh clone in a temp folder (scratchpad), recording any missing or wrong step and fixing `README.md` accordingly (depends on T031 and on US1 being complete; needs LocalDB available)

**Checkpoint**: US2 scenarios 1–2 satisfied; T029 passes.

---

## Phase 5: User Story 3 - Database connectivity is proven, empty (Priority: P3)

**Goal**: The real LocalDB connection is exercised and an unavailable database is reported, not
hidden.

**Independent Test**: Healthy with the LocalDB database present; 503 naming the database when
LocalDB is stopped.

### Tests for User Story 3

- [X] T033 [P] [US3] Unhealthy-path test added to `backend/tests/BillingCrm.Api.Tests/HealthEndpointTests.cs`: unhealthy fake database check → 503, `checks.database` = `Unhealthy`, body contains no connection-string text (contracts/health.md test 2)

### Implementation for User Story 3

- [X] T034 [US3] Verify research R3 on the developer's machine: with the connection string in user-secrets, `dotnet ef database update` (zero migrations) creates the empty `BillingCrm` database on LocalDB. Record the result in `specs/001-foundation/research.md` R3; if it does not create the database, stop and raise a spec change (fallback: an empty `Initial` migration) rather than adding one silently
- [X] T035 [US3] With a real database, confirm `GET /health` returns 200/`Healthy`, then confirm 503/`Unhealthy` with no connection details; note results for T041. **Deviation (recorded in research R3)**: stopping LocalDB does not work for this, because LocalDB auto-starts on the next connection; the failure was forced with a nonexistent database name instead

**Checkpoint**: US3 scenarios 1–2 verified on real LocalDB.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T036 [P] Secret scan: search the whole repo (including `appsettings*.json`, tests, README, specs) for `Server=`, `Data Source`, `Password`, `Trusted_Connection` and confirm only placeholders remain (SC-005, FR-010)
- [X] T037 [P] Update `CLAUDE.md` "Current Repository Layout" and "Run Commands" with the real paths, project names, ports and commands from this spec, as that file instructs
- [X] T038 Run the full quickstart validation (`specs/001-foundation/quickstart.md`) on the developer's machine, including the browser checks for `http://localhost:5080/health` and `http://localhost:4200` and the API-stopped state of the home page; the developer confirms it is running (Constitution II)
- [X] T039 Run the `angular-reviewer` and `api-reviewer` agents and `spec-auditor` against this spec; fix or explicitly record every finding (Constitution workflow gate)
- [X] T040 Re-run `dotnet test backend` and `ng test --watch=false`; all pass (SC-006)
- [X] T041 Mark spec 001 done only after T038 and T039 are complete; do not start spec 002 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates everything that generates code. T002 → T003 → T008. T004–T007 parallel after T002 (T004 also after T003).
- **Foundational**: after Setup; T009–T011 parallel; T012 after T011; T013 after T010 and T012.
- **US1**: after Foundational. Back-end tasks (T014–T016, T019–T022) and front-end tasks (T017, T018, T023–T027) are independent tracks and can run in parallel.
- **US2**: after Foundational; T030 must follow T019 (same file). T032 needs the working system.
- **US3**: after US1 (needs the health endpoint and the real database wiring).
- **Polish**: after all stories.

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories.
- **US2 (P2)**: independent of US1 except that T030 edits the file US1's T019 edits (do T019 first).
- **US3 (P3)**: depends on US1's `/health`.

### Within Each Story

Tests first and failing → implementation → run tests. Front-end order: environment → service →
components → routing.

### Parallel Opportunities

- Setup: T004, T005, T006, T007 together.
- Foundational: T009, T010, T011 together.
- US1: `T014, T015, T016, T017, T018` together; then back-end track (T019 → T020 → T022) alongside
  front-end track (T023 → T024 → T026, with T025 parallel).
- Polish: T036, T037 together.

---

## Parallel Example: User Story 1

```text
# Tests, all at once:
T014 ApiFactory.cs   T015 HealthEndpointTests.cs   T016 CorsTests.cs
T017 shell.component.spec.ts   T018 home.component.spec.ts

# Then two tracks:
Back end:   T019 → T020 → T021 → T022
Front end:  T023 → T024 → T026 ;  T025 in parallel ;  then T027
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001–T008) and Foundational (T009–T013).
2. US1 (T014–T028).
3. Stop and validate: both `/health` and the home page succeed. This is the request's stated
   acceptance scenario.

### Incremental Delivery

1. Add US2 (fail-fast config + README) → onboarding proven.
2. Add US3 (real database verification) → persistence proven.
3. Polish, reviewers, developer verification (T036–T041) → spec 001 done, spec 002 may start.

---

## Notes

- T001 is a hard gate; do not generate code before the developer confirms the patterns.
- If the `/health` database question resolves to B (liveness only), drop T019's health check
  registration, T033, T035 and User Story 3, and simplify T020's writer accordingly.
- Commit after each logical group only when the developer asks; there are no commits yet.
