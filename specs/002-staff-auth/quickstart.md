# Quickstart: Staff Authentication

**Feature**: 002-staff-auth | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001's setup (LocalDB, connection string in user-secrets).

## Prerequisites

Everything from 001, working. Spec 001 verified.

## 1. Supply the signing key (outside git)

Generate a random key and store it in user-secrets. The value is never committed and never
printed to the README or logs:

```powershell
$key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:SigningKey" $key --project backend/src/BillingCrm.Api
```

(Or set the environment variable `Jwt__SigningKey`.) `dotnet ef` builds the API, so do this
**before** the next step.

## 2. Create the staff table

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies the `AddStaffUsers` migration. The seed accounts are created the first time the API
starts in the `Development` environment (plain `dotnet run` uses it).

## 3. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 4. Validate (maps to the spec's acceptance scenarios)

| Check | Expected |
|-------|----------|
| Open `http://localhost:4200` while signed out | Redirected to `/login` (US1.6) |
| Sign in as `admin@crm.local` (password in `CLAUDE.md`) | Shell shows the email, role **Admin**, a Sign out button and an **Administration** link (US1.1, FR-007) |
| Open Administration | Placeholder page (US2.1) |
| Sign out | Back at `/login`; browser Back button does not show protected pages (US3.1, US3.2) |
| Sign in as `agent1@crm.local` | Shell shows role **Agent** and **no** Administration link (US1.2, US2.2) |
| While signed in as the Agent, type `http://localhost:4200/admin` | "Access denied" page, no Admin content (US2.3) |
| Agent token against the API: `POST /api/auth/login` for the Agent, then `GET /api/admin/status` with the returned token | 403 (US2.4) |
| Same request with no token / a garbled token | 401 (US2.5) |
| Wrong password, and unknown email | Both: 401, identical message, no token; the page shows "Email or password is incorrect" (US1.3, US1.4, SC-002) |
| Set `Jwt__ExpiryMinutes=1`, sign in, wait 61 seconds, click any link | Redirected to `/login` with "Your session expired" (US3.3, FR-010) |
| Sign in again after that | Returns to the page you were on (US3.4) |
| `GET http://localhost:5080/health` with no token | Still 200 (FR-013) |
| Start the API with no `Jwt:SigningKey` | Fails at start-up, message names `Jwt:SigningKey`, prints no value (FR-016) |
| `dotnet test backend/BillingCrm.sln`; `cd frontend; ng test --watch=false` | All pass (FR-017, SC-007) |
| Search the repo for the signing key you generated | No matches (SC-006) |

Contract details: [contracts/auth-api.md](contracts/auth-api.md). Data model:
[data-model.md](data-model.md).
