# Research: Foundation

**Feature**: 001-foundation | **Date**: 2026-09-24

No `NEEDS CLARIFICATION` remained in the Technical Context. The items below record the decisions
the plan depends on, including the one open spec question.

## R1. Does `/health` check the database? (open spec question)

- **Confirmed by developer 2026-09-24** (T001): Option A, plus the layer-DI, built-in
  health-check, and Angular `core/`+`features/` patterns in plan.md.
- **Decision**: Yes — **Option A** from the `/speckit-clarify` question:
  `/health` returns 200 only when the API is up *and* the database is reachable; otherwise 503
  with a body naming the database.
- **Rationale**: It matches the spec's stated default (FR-004, User Story 3) and proves the
  LocalDB + EF Core wiring on day one.
- **Alternatives considered**: (B) liveness only; (C) always 200, database status in the body.
- **If the answer changes**: only the health-check registration, the `/health` response mapping,
  and the health tests change. No structural impact on the projects or the Angular shell.

## R2. How is `/health` implemented?

- **Decision**: ASP.NET Core's built-in health-check services plus the EF Core health-check
  package (`AddDbContextCheck<BillingDbContext>("database")`), mapped at `/health` with a small
  custom response writer and `.AllowAnonymous()`.
- **Rationale**: No hand-rolled probing code; the framework already maps Healthy→200 and
  Unhealthy→503. `.AllowAnonymous()` is a no-op now and stays correct once spec 002 adds
  authorization.
- **Alternatives considered**: a hand-written `MapGet("/health")` that calls the DbContext
  directly (more code, reinvents the status mapping; would put data access in the endpoint,
  which the Minimal API skill forbids).
- **Response shape**: same JSON body for 200 and 503 (`status` + `checks.database`). An
  unhealthy database is an infrastructure state, not an expected business error, so it is not
  modelled as `Result`/`ProblemDetails` (Constitution III applies to business errors).

## R3. A database must exist for the health check to pass

- **Decision**: The README makes `dotnet ef database update` an explicit setup step. With no
  migrations, this creates the empty database and the migration-history table. The API does
  **not** auto-migrate or auto-create the database at start-up.
- **Rationale**: Health then honestly reports "database unreachable" until setup is done, and
  the `sql-migrations` rule "do not drop the database" is trivially respected. Auto-migrating
  at start-up hides setup problems.
- **Verified 2026-09-24 (T034)**: `dotnet ef database update` with zero migrations creates the
  empty `BillingCrm` database on LocalDB; its only table is `__EFMigrationsHistory`. No
  `Initial` migration is needed.
- **Finding (T035)**: stopping the LocalDB instance does **not** make `/health` unhealthy —
  LocalDB auto-starts on the next connection attempt. The reliable way to force the failure
  path is a connection string naming a database that does not exist (verified: 503,
  `{"status":"Unhealthy","checks":{"database":"Unhealthy"}}`, no connection details).

## R4. Connection string and required settings

- **Decision**: `ConnectionStrings:DefaultConnection` is **absent** from every committed
  `appsettings*.json`. Developers supply it through .NET user-secrets (the Api project carries a
  `UserSecretsId`, which is an identifier, not a secret) or the environment variable
  `ConnectionStrings__DefaultConnection`. `AddInfrastructure` throws an
  `InvalidOperationException` at start-up naming the missing key. The message never includes a
  value.
- **Rationale**: Constitution IV forbids committed connection strings, even for LocalDB. A
  start-up misconfiguration is a genuinely unexpected failure, so an exception is appropriate
  (Constitution III).
- **Alternatives considered**: committing a LocalDB string in `appsettings.Development.json`
  (rejected: violates IV); `.gitignore`d `appsettings.Local.json` (already partly supported by
  the existing `.gitignore` pattern but less standard than user-secrets).

## R5. CORS

- **Decision**: One named policy. Allowed origins come from `Cors:AllowedOrigins` (array) in
  `appsettings.json`, default `["http://localhost:4200"]`. No `AllowAnyOrigin`. Methods and
  headers are open for those origins for now; credentials are not enabled (JWT bearer will use
  the `Authorization` header, not cookies).
- **Rationale**: Satisfies FR-008/FR-009. Origins are not secrets, so committing the default is
  allowed.
- **Alternatives considered**: a Angular dev-server proxy instead of CORS (rejected: the request
  explicitly lists CORS).

## R6. Layer layout and dependency direction

- **Decision**: `Domain` (no references) ← `Application` ← `Infrastructure`; `Api` references
  `Application` and `Infrastructure` as the composition root. `Domain` and `Application` are
  intentionally near-empty in this spec (an assembly marker each, plus an `AddApplication`
  extension with no registrations yet).
- **Rationale**: FR-001. A `Result` type is **not** introduced yet — no business errors exist in
  this spec; it arrives with the first business endpoint (avoids a speculative shared
  abstraction, Constitution V).
- **Alternatives considered**: creating `Result`/`ProblemDetails` mapping now (rejected as
  speculative).

## R7. Toolchain on the developer's machine

- **Observed**: .NET SDK 8.0.403; Node 24.15.0; npm 9.7.1; Angular CLI 21.2.8; LocalDB instance
  `MSSQLLocalDB` present; global `dotnet-ef` is **10.0.11**.
- **Decision**: Target `net8.0` and EF Core **8.0.x** packages. Pin `dotnet-ef` **8.0.x** in a
  repo-local tool manifest (`.config/dotnet-tools.json`) so the README's commands do not depend
  on the newer global tool. Angular workspace generated with the installed CLI 21 (standalone by
  default) and Angular Material via `ng add`.
- **Rationale**: A 10.x tool against EF Core 8 packages warns and can behave differently; a
  local manifest makes the setup reproducible.

## R8. Test tooling

- **Decision**: Backend — xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`),
  replacing the database health check with a fake healthy/unhealthy check so tests need no
  LocalDB. Frontend — the runner the Angular CLI generates by default, with one component test
  for the shell and one for the home page states.
- **Rationale**: Fast, deterministic tests that satisfy FR-013 and SC-006 on any machine.
