# Implementation Plan: Staff Authentication

**Branch**: `002-staff-auth` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-staff-auth/spec.md`

## Summary

Add JWT sign-in for staff with two roles. A `StaffUser` table (first migration) holds hashed
passwords; two dev accounts are seeded only in Development. `POST /api/auth/login` returns a
short-lived signed token plus the user's email and role, or a uniform 401. Every API operation
declares its access; the fallback policy closes anything undeclared, and `/api/admin/*` requires
the Admin role. This spec introduces the project's `Result` → `ProblemDetails` pattern. In
Angular, a login page, an auth service, a Bearer/401 interceptor, and two functional route guards
protect the shell; Agents get no Admin link and are refused `/admin`; sign-out and expiry both
land on `/login`. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 8; TypeScript on Angular 21 (unchanged from 001)

**Primary Dependencies**: adds `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x (Api),
`Microsoft.Extensions.Identity.Core` 8.x (Infrastructure, password hashing); Angular Material
forms components (already installed)

**Storage**: SQL Server LocalDB; new table `StaffUsers` via migration `AddStaffUsers`

**Testing**: xUnit (Application unit tests; Api tests via `WebApplicationFactory` with an in-memory
user store, no LocalDB); Vitest via the Angular CLI unit-test builder

**Target Platform**: Developer workstation, localhost

**Project Type**: Web application (back end + front end), unchanged

**Performance Goals**: valid sign-in reaches the shell in under 3 s (SC-001)

**Constraints**: signing key never in git; zero clock skew; identical 401 for wrong password and
unknown email; only `/health` and login are anonymous; roles exactly `Admin`/`Agent`

**Scale/Scope**: 2 seeded users; 3 new API operations (login, me, admin status); 3 new UI screens (login, admin
placeholder, access denied) plus shell changes

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design — still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Only authentication. No customers/products/invoices. The Admin placeholder has no business content. None of shop, cart, payments, tickets, WhatsApp. |
| II. Sequential Spec Delivery | Pass | 001 was verified running by the developer on 2026-09-24. 002 is done only when verified running locally and reviewers have run. |
| III. Explicit Business Errors | Pass | Bad credentials, validation, forbidden are `Result` failures mapped to `ProblemDetails`; nothing is thrown for them. Start-up misconfiguration (missing signing key) still throws, as in 001. |
| IV. Financial Data Integrity | Pass | No monetary values. Signing key and connection string outside git. Passwords stored only as hashes. Seed passwords are the documented non-real dev values in `CLAUDE.md`; see R4 for the alternative if you prefer them out of source. |
| V. Deliberate API/UI Design | Pass | The six items below were confirmed by the developer before implementation (T001, 2026-09-24). |

**Workflow gates**: use `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations`
(and `invoice-money` is not relevant). Before the spec counts as done run `api-reviewer`,
`angular-reviewer`, `spec-auditor`, and **`security-reviewer`** (the project requires it after
002 and after any auth change).

### Patterns and decisions to confirm with the developer before generating (Principle V)

1. **`Result` / `Result<T>` + ProblemDetails mapping** — new shared abstraction (research R5).
2. **Endpoint groups and authorization model** — `/api/auth` and `/api/admin` groups, fallback
   "authenticated" policy, explicit `AllowAnonymous` / `RequireAuthorization("AdminOnly")` on each
   endpoint (R6, R7).
3. **Application ports** — `IStaffUserStore`, `IPasswordHasher`, `ITokenService` in Application,
   implemented in Infrastructure, with the sign-in service in Application (R3, R5).
4. **Dev seeding location** — Development-only seeder with the documented dev passwords in source
   vs reading them from user-secrets (R4). *This is a judgement call about credentials in source.*
5. **Angular auth structure** — functional interceptor + functional guards + `AuthService` with
   the session in `sessionStorage`; login/admin/access-denied features (R8, R9).
6. **Session storage choice** — `sessionStorage` (recommended) vs `localStorage` (R8).

## Project Structure

### Documentation (this feature)

```text
specs/002-staff-auth/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── auth-api.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (changes to the 001 layout)

```text
backend/src/
├── BillingCrm.Domain/
│   ├── StaffRole.cs                       # enum Admin, Agent
│   └── StaffUser.cs                       # entity
├── BillingCrm.Application/
│   ├── Common/Result.cs                   # Result, Result<T>, Error, ErrorKind
│   └── Auth/
│       ├── IStaffUserStore.cs             # find by normalized email; add
│       ├── IPasswordHasher.cs
│       ├── ITokenService.cs               # issue token for a StaffUser
│       ├── AuthService.cs                 # SignInAsync -> Result<SignInResponse>
│       └── SignInRequest/Response records
├── BillingCrm.Infrastructure/
│   ├── Persistence/
│   │   ├── BillingDbContext.cs            # + DbSet<StaffUser>, configuration
│   │   └── Migrations/                    # AddStaffUsers
│   └── Auth/
│       ├── EfStaffUserStore.cs
│       ├── IdentityPasswordHasher.cs      # wraps PasswordHasher<T>
│       ├── JwtTokenService.cs             # uses TimeProvider
│       └── JwtOptions.cs                  # validation: key required, >= 32 bytes
└── BillingCrm.Api/
    ├── Program.cs                         # JwtBearer, policies, ProblemDetails, fallback policy
    ├── Endpoints/
    │   ├── AuthEndpoints.cs               # /api/auth: login (anon), me
    │   ├── AdminEndpoints.cs              # /api/admin: status (AdminOnly)
    │   ├── HealthEndpoint.cs              # unchanged (already AllowAnonymous)
    │   └── ResultExtensions.cs            # Result -> ProblemDetails
    ├── Development/DevelopmentSeeder.cs   # runs only in Development
    └── appsettings.json                   # + Jwt:Issuer/Audience/ExpiryMinutes (no key)

backend/tests/
├── BillingCrm.Application.Tests/          # new: AuthService unit tests
└── BillingCrm.Api.Tests/                  # ApiFactory gains fake store + test key; new auth tests

frontend/src/app/
├── core/
│   ├── auth/
│   │   ├── auth.service.ts                # sign in/out, session signal, sessionStorage
│   │   ├── auth.interceptor.ts            # Bearer + 401 handling
│   │   ├── auth.guard.ts                  # authGuard, roleGuard('Admin')
│   │   └── auth.models.ts
│   └── layout/shell.component.ts          # + email, role, Sign out, Admin link (Admin only)
├── features/
│   ├── login/login.component.ts           # reactive form, loading/error states
│   ├── admin/admin.component.ts           # placeholder
│   └── access-denied/access-denied.component.ts
└── app.routes.ts / app.config.ts          # guards, provideHttpClient(withInterceptors(...))

README.md                                  # + signing-key step, migration, seed accounts
CLAUDE.md                                  # layout/run commands updated after implementation
```

**Structure Decision**: Extend the 001 layout in place. One new test project for Application unit
tests keeps the layers' tests separate; Api-level tests stay in `BillingCrm.Api.Tests`.

## Known gaps carried forward (deliberate, per spec FR-018)

No lockout/throttling, no server-side revocation, no refresh, no password change. Record these
for `012-security`.

## Complexity Tracking

No constitution violations; nothing to justify. The one new test project and the three
Application ports are the minimum for testing sign-in without a database.
