# Quickstart: Foundation

**Feature**: 001-foundation | Validation guide; the README carries the same steps for developers.
Commands are run from the repository root unless stated. Project names and ports are defined in
[plan.md](plan.md).

## Prerequisites

.NET SDK 8, Node.js (20.19+, 22.12+ or 24) with npm (11+ recommended; npm 9/10 install from the lock file but cannot add or update
Angular 21 dependencies), Angular CLI, SQL Server LocalDB (`sqllocaldb i` lists `MSSQLLocalDB`).

## 1. Supply the local database setting (outside git)

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your LocalDB connection string>" --project backend/src/BillingCrm.Api
```

The connection string points at LocalDB instance `(localdb)\MSSQLLocalDB` and a database named
`BillingCrm`. Do not commit it anywhere.

## 2. Create the empty database

```powershell
dotnet tool restore
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

## 3. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api          # http://localhost:5080
cd frontend; npm install; ng serve                        # http://localhost:4200
```

## 4. Validate (maps to the spec's acceptance scenarios)

| Check | Expected |
|-------|----------|
| Open `http://localhost:5080/health` | 200, `{"status":"Healthy","checks":{"database":"Healthy"}}` (User Story 1.1, 3.1) |
| Open `http://localhost:4200` | Shell with header showing the product name; home page shows API status "Healthy"; no browser console errors (US 1.2, 1.3) |
| Stop the API, reload the home page | Shell still renders; home shows a friendly "API unreachable" state (Edge Cases) |
| Start the API with `ConnectionStrings__DefaultConnection` set to a database name that does not exist (env var overrides user-secrets), request `/health` | 503, `checks.database` = `Unhealthy`, no connection details in the body (US 3.2, SC-004). Note: stopping LocalDB does **not** work for this check, because LocalDB restarts itself on the next connection |
| Start the API with no connection string set | Fails at start-up, message names `ConnectionStrings:DefaultConnection`, prints no value (US 2.2, FR-011) |
| `dotnet test backend` and `cd frontend; ng test --watch=false` | All pass (FR-013, SC-006) |
| Search the repo for the connection string | No matches (SC-005) |

Contract details: [contracts/health.md](contracts/health.md). Data model:
[data-model.md](data-model.md).
