---

description: "Task list for 002-staff-auth"
---

# Tasks: Staff Authentication

**Input**: Design documents from `/specs/002-staff-auth/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/auth-api.md, quickstart.md

**Tests**: Included. FR-017 requires automated tests, and contracts/auth-api.md lists contract
tests that map to test tasks below. Write each story's tests first and confirm they fail.

**Organization**: Grouped by user story (US1 sign in and reach the shell, US2 roles decide what I
can open, US3 sign out and expired sessions).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run
from the repository root unless a task says otherwise.

**Rules that apply to every task**: use the `minimal-api-conventions`, `angular-ui-conventions`
and `sql-migrations` skills for matching edits; expected failures are `Result` → `ProblemDetails`,
never exceptions (only a missing/short signing key at start-up throws); the signing key, any real
secret, and connection strings never enter a committed file (tests use an obviously fake key of
at least 32 characters); the only credentials in source are the documented dev seed values; every
API endpoint declares its access explicitly; roles are exactly `Admin` and `Agent`; nothing about
customers, products, invoices, shop, cart, payments, tickets or WhatsApp; passwords, tokens and
the signing key must never be logged or returned.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations, packages, test project, local key. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the six items in plan.md "Patterns and decisions to confirm": (1) `Result`/ProblemDetails abstraction, (2) `/api/auth` + `/api/admin` groups and fallback policy, (3) Application ports, (4) dev seed passwords in a Development-only seeder class in source vs user-secrets, (5) Angular functional interceptor/guards/`AuthService`, (6) `sessionStorage` vs `localStorage`. If (4) is "user-secrets", change T028 and T029 to read `Seed:AdminPassword` / `Seed:AgentPassword`; if (6) is `localStorage`, change T030 accordingly. Record answers in `specs/002-staff-auth/research.md` (R4, R8, R9)
- [X] T002 [P] Add packages: `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x to `backend/src/BillingCrm.Api/BillingCrm.Api.csproj`; `Microsoft.Extensions.Identity.Core` 8.x to `backend/src/BillingCrm.Infrastructure/BillingCrm.Infrastructure.csproj`
- [X] T003 [P] Create test project `backend/tests/BillingCrm.Application.Tests` (xUnit, net8.0) referencing `BillingCrm.Application` and `BillingCrm.Domain`; add it to `backend/BillingCrm.sln`
- [X] T004 Generate a random signing key and store it in the developer's local user-secrets only: run the PowerShell from quickstart.md step 1 (`dotnet user-secrets set "Jwt:SigningKey" ...` for `backend/src/BillingCrm.Api`). Do not print the key, write it to any file, or paste it into a task output

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain type, `Result`, ports, table, JWT wiring, error mapping, test harness.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T005 [P] Add `StaffRole` (enum `Admin`, `Agent`) in `backend/src/BillingCrm.Domain/StaffRole.cs` and `StaffUser` (Id, Email, NormalizedEmail, PasswordHash, Role; creation validates non-empty email and a defined role; static helper to normalise an email = trim + upper-invariant) in `backend/src/BillingCrm.Domain/StaffUser.cs`
- [X] T006 [P] Add `Result`, `Result<T>`, `Error` (code, message) and `ErrorKind` (`Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`) in `backend/src/BillingCrm.Application/Common/Result.cs`
- [X] T007 [P] Add the ports and DTOs in `backend/src/BillingCrm.Application/Auth/`: `IStaffUserStore` (find by normalized email, add), `IPasswordHasher` (hash, verify), `ITokenService` (issue token for a `StaffUser` → token + expiry), `SignInRequest(Email, Password)`, `SignInResponse(AccessToken, ExpiresAtUtc, Email, Role)`
- [X] T008 Add `DbSet<StaffUser>` to `backend/src/BillingCrm.Infrastructure/Persistence/BillingDbContext.cs` and entity configuration in `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/StaffUserConfiguration.cs`: table `StaffUsers`, columns per data-model.md (`Email` nvarchar(256), `NormalizedEmail` nvarchar(256), `PasswordHash` nvarchar(500), `Role` nvarchar(20) stored as the enum name), unique index `IX_StaffUsers_NormalizedEmail`, check constraint `CK_StaffUsers_Role` (Admin/Agent) (depends on T005)
- [X] T009 Create migration `AddStaffUsers` with `dotnet ef migrations add AddStaffUsers --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api --output-dir Persistence/Migrations`, review the generated SQL/columns against data-model.md, then `dotnet ef database update ...` and confirm with sqlcmd that `StaffUsers` exists with the unique index and check constraint and that no other business table exists (depends on T004, T008)
- [X] T010 [P] Add `JwtOptions` (`SigningKey`, `Issuer`, `Audience`, `ExpiryMinutes`) with validation in `backend/src/BillingCrm.Infrastructure/Auth/JwtOptions.cs`: key required and at least 32 bytes, expiry between 1 and 1440 minutes; failure throws `InvalidOperationException` naming the setting (`Jwt:SigningKey`, `Jwt:ExpiryMinutes`) and never the value
- [X] T011 Bind and validate `JwtOptions` inside `AddInfrastructure` (`backend/src/BillingCrm.Infrastructure/DependencyInjection.cs`) so start-up fails fast; add `Jwt` section `Issuer`, `Audience`, `ExpiryMinutes: 60` (and **no** key) to `backend/src/BillingCrm.Api/appsettings.json` (depends on T010)
- [X] T012 Update the test harness in `backend/tests/BillingCrm.Api.Tests/ApiFactory.cs`: supply an obviously fake test signing key (32+ chars, constant `TestSigningKey`) via `UseSetting("Jwt:SigningKey", ...)`, replace `IStaffUserStore` with an in-memory fake exposing seeded Admin and Agent test users (hashes computed with the real hasher once T023 exists; until then use a stub hasher), keep the health-check fake and CORS override; add `backend/tests/BillingCrm.Api.Tests/TestTokens.cs` that mints tokens with the test key: valid Admin, valid Agent, expired (`nbf`/`exp` in the past), and tampered (signature altered) (depends on T011)
- [X] T013 Configure authentication and authorization in `backend/src/BillingCrm.Api/Program.cs`: `AddProblemDetails()` + `UseStatusCodePages()` so 401/403 return `application/problem+json`; `AddAuthentication().AddJwtBearer(...)` validating signature, issuer, audience and lifetime with `ClockSkew = TimeSpan.Zero`, `MapInboundClaims = false`, role claim type `role`; `AddAuthorization` with `FallbackPolicy = RequireAuthenticatedUser` and policy `AdminOnly` (role `Admin`); `UseAuthentication()` then `UseAuthorization()` after `UseCors()` and before endpoint mapping (depends on T011)
- [X] T014 [P] Add `ResultExtensions.ToProblem()` in `backend/src/BillingCrm.Api/Endpoints/ResultExtensions.cs`: maps a failed `Result` to `ProblemDetails` by `ErrorKind` (Validation → 400 with an `errors` dictionary, Unauthorized → 401, Forbidden → 403, NotFound → 404, Conflict → 409); never includes exception text (depends on T006)

**Checkpoint**: `dotnet build backend/BillingCrm.sln` succeeds; every 001 back-end test still passes (in particular `/health` stays 200 with no token, and the CORS tests pass) — proving the fallback policy did not close `/health`; `StaffUsers` exists in LocalDB.

---

## Phase 3: User Story 1 - Sign in and reach the app shell (Priority: P1) MVP

**Goal**: Correct credentials for either seeded account reach the shell showing email and role;
wrong or unknown credentials get nothing; signed-out visitors are sent to sign-in and returned
afterwards.

**Independent Test**: Sign in as both seeded users and reach the shell; wrong password and
unknown email both fail identically with no token; opening a page while signed out redirects to
`/login`.

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [X] T015 [P] [US1] `AuthService` unit tests in `backend/tests/BillingCrm.Application.Tests/AuthServiceTests.cs` with fake store/hasher/token service: success returns token, expiry, email, role; wrong password → `Unauthorized` `Result` with the generic message and no token; unknown email → same result and the hasher is still invoked (dummy verify); empty/whitespace email or password → `Validation` failure naming the field; email matched case-insensitively and trimmed
- [X] T016 [P] [US1] Login/me contract tests in `backend/tests/BillingCrm.Api.Tests/LoginEndpointTests.cs` (contracts/auth-api.md tests 1–3, and `/api/auth/me` with and without a token): Admin and Agent → 200 with correct role and a token `/api/auth/me` accepts; wrong password and unknown email → 401 with byte-identical bodies and no `accessToken`; empty fields → 400 problem details naming the field; `/api/auth/me` no token → 401
- [X] T017 [P] [US1] Seeder tests in `backend/tests/BillingCrm.Api.Tests/DevelopmentSeederTests.cs`: creates exactly the two accounts with roles Admin/Agent and hashed (not plain) passwords; second run creates no duplicates and does not change existing passwords; does nothing when the environment is not Development
- [X] T018 [P] [US1] `AuthService` (Angular) spec in `frontend/src/app/core/auth/auth.service.spec.ts` using `HttpTestingController`: successful sign-in stores the session and exposes email/role; a 401 leaves no session; storage failure (throwing `sessionStorage`) does not crash
- [X] T019 [P] [US1] Login component spec in `frontend/src/app/features/login/login.component.spec.ts`: empty fields show required errors and send nothing; bad credentials show "Email or password is incorrect."; loading state disables the button; success navigates to `returnUrl` or `/`
- [X] T020 [P] [US1] Guard and interceptor specs in `frontend/src/app/core/auth/auth.guard.spec.ts` and `frontend/src/app/core/auth/auth.interceptor.spec.ts`: `authGuard` sends a signed-out visitor to `/login?returnUrl=...`; the interceptor adds `Authorization: Bearer` only to requests under `environment.apiBaseUrl` and never to other hosts

### Implementation for User Story 1 (back end)

- [X] T021 [US1] Implement `AuthService.SignInAsync` in `backend/src/BillingCrm.Application/Auth/AuthService.cs`: validate inputs → normalise email → find user → verify hash (dummy verify when not found) → issue token; every credential failure returns the same `Unauthorized` `Result` ("Email or password is incorrect."); nothing logged that contains a password (depends on T006, T007)
- [X] T022 [P] [US1] Implement `EfStaffUserStore` in `backend/src/BillingCrm.Infrastructure/Auth/EfStaffUserStore.cs` (lookup by `NormalizedEmail`, add) (depends on T008)
- [X] T023 [P] [US1] Implement `IdentityPasswordHasher` wrapping `PasswordHasher<StaffUser>` in `backend/src/BillingCrm.Infrastructure/Auth/IdentityPasswordHasher.cs`
- [X] T024 [P] [US1] Implement `JwtTokenService` in `backend/src/BillingCrm.Infrastructure/Auth/JwtTokenService.cs` using `TimeProvider`: claims `sub`, `email`, `role`, `jti`, `iat`, `nbf`, `exp` = now + `Jwt:ExpiryMinutes`, HS256 with `Jwt:SigningKey` (depends on T010)
- [X] T025 [US1] Register services: `AuthService` and `TimeProvider.System` in `backend/src/BillingCrm.Application/DependencyInjection.cs` / `BillingCrm.Infrastructure/DependencyInjection.cs` (store, hasher, token service) (depends on T021–T024)
- [X] T026 [US1] Add `backend/src/BillingCrm.Api/Endpoints/AuthEndpoints.cs`: `MapGroup("/api/auth")` with `POST /login` (`.AllowAnonymous()`, calls `AuthService`, maps failures via `ToProblem()`) and `GET /me` (`.RequireAuthorization()`, returns email and role from the token claims); map it in `Program.cs` (depends on T013, T014, T025)
- [X] T027 [US1] After T026, explain the two endpoints to the developer in 5 lines using MVC words (per `minimal-api-conventions` skill); no file output
- [X] T028 [US1] Add `backend/src/BillingCrm.Api/Development/DevelopmentSeeder.cs`: only when `IHostEnvironment.IsDevelopment()`; if migrations are pending, log a warning naming `dotnet ef database update` and skip; otherwise create `admin@crm.local` (Admin) and `agent1@crm.local` (Agent) if missing, hashing at runtime; never update or duplicate an existing account; passwords are the documented dev values from `CLAUDE.md` in this one class, named and commented as development-only (or read from `Seed:*` if T001 chose user-secrets); register it as a startup task in `Program.cs` (depends on T023, T025)

### Implementation for User Story 1 (front end)

- [X] T029 [P] [US1] Add `frontend/src/app/core/auth/auth.models.ts` (`SignInRequest`, `SignInResponse`, `Session`, `Role = 'Admin' | 'Agent'`)
- [X] T030 [US1] Implement `frontend/src/app/core/auth/auth.service.ts`: `signIn(email, password)` posting to `{apiBaseUrl}/api/auth/login`, session kept in a signal and mirrored to `sessionStorage` (try/catch around every read/write; render correctly if storage is unavailable), `isSignedIn`, `email`, `role` read-only signals (depends on T029)
- [X] T031 [US1] Implement `frontend/src/app/core/auth/auth.interceptor.ts` (functional `HttpInterceptorFn`): adds `Authorization: Bearer <token>` only to requests whose URL starts with `environment.apiBaseUrl` (depends on T030)
- [X] T032 [US1] Implement `authGuard` (functional `CanActivateFn`) in `frontend/src/app/core/auth/auth.guard.ts`: signed out → `UrlTree` to `/login` with `returnUrl` (depends on T030)
- [X] T033 [US1] Implement `frontend/src/app/features/login/login.component.ts`: standalone Material reactive form (email, password), required-field errors, loading state, generic error message on 401, "cannot reach the service" message on network failure, navigates to `returnUrl` or `/` on success; a signed-in visitor is redirected to `/` (depends on T030)
- [X] T034 [US1] Update `frontend/src/app/core/layout/shell.component.ts` to show the signed-in email and role in the toolbar, and update `frontend/src/app/app.routes.ts` (`/login` public; shell + home under `authGuard`) and `frontend/src/app/app.config.ts` (`provideHttpClient(withInterceptors([authInterceptor]))`); update the 001 shell/app specs accordingly (depends on T031–T033)
- [X] T035 [US1] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false` in `frontend/` (from PowerShell); fix until T015–T020 pass
- [X] T036 [US1] Manual check on the real database: run the API in Development (seeder runs), `curl` login as both seeded users (200, correct role), wrong password and unknown email (401, identical bodies); record results in `specs/002-staff-auth/research.md` (depends on T028)

**Checkpoint**: US1 acceptance scenarios 1–6 pass via tests; browser check is in T062.

---

## Phase 4: User Story 2 - Roles decide what I can open (Priority: P2)

**Goal**: Admin-only area open to Admin only, enforced by the API; Agents get no link and are refused.

**Independent Test**: As Agent: no Admin link, `/admin` shows access denied, API returns 403 with the Agent token; as Admin: everything works.

### Tests for User Story 2

- [X] T037 [P] [US2] Admin endpoint tests in `backend/tests/BillingCrm.Api.Tests/AdminEndpointTests.cs` (contracts/auth-api.md tests 5 and 6): Admin token → 200 `{"area":"admin","status":"ok"}`; Agent token → 403 `application/problem+json`; no token → 401; `/health` with no token still 200
- [X] T038 [P] [US2] `roleGuard` spec added to `frontend/src/app/core/auth/auth.guard.spec.ts`: Admin allowed; Agent redirected to `/access-denied`; signed-out redirected to `/login`
- [X] T039 [P] [US2] Shell and page specs: `frontend/src/app/core/layout/shell.component.spec.ts` (Administration link present for Admin, absent for Agent), `frontend/src/app/features/admin/admin.component.spec.ts` (shows placeholder; calls the admin status through `AdminService`), `frontend/src/app/features/access-denied/access-denied.component.spec.ts` (shows the message)

### Implementation for User Story 2

- [X] T040 [US2] Add `backend/src/BillingCrm.Api/Endpoints/AdminEndpoints.cs`: `MapGroup("/api/admin")` with `GET /status` `.RequireAuthorization("AdminOnly")` returning the placeholder body; map in `Program.cs` (depends on T013)
- [X] T041 [US2] After T040, explain the endpoint to the developer in 5 lines using MVC words; no file output
- [X] T042 [US2] Add `roleGuard(role)` to `frontend/src/app/core/auth/auth.guard.ts` (depends on T032)
- [X] T043 [P] [US2] Add `frontend/src/app/core/admin/admin.service.ts` (HttpClient service for `GET {apiBaseUrl}/api/admin/status`) and `frontend/src/app/features/admin/admin.component.ts` (Material card "Administration", placeholder text, shows the status result; no business content)
- [X] T044 [P] [US2] Add `frontend/src/app/features/access-denied/access-denied.component.ts` ("You do not have access to this area.")
- [X] T045 [US2] Add routes `/admin` (`authGuard` + `roleGuard('Admin')`) and `/access-denied` inside the shell in `frontend/src/app/app.routes.ts`, and the "Administration" link shown only when `role() === 'Admin'` in `frontend/src/app/core/layout/shell.component.ts` (depends on T042–T044)
- [X] T046 [US2] Run all back-end and front-end tests; fix until T037–T039 pass

**Checkpoint**: US2 acceptance scenarios 1–5 pass via tests.

---

## Phase 5: User Story 3 - Sign out and expired sessions (Priority: P3)

**Goal**: Sign-out returns to sign-in and removes access; an expired session lands on sign-in with a message and returns to the page afterwards.

**Independent Test**: Sign in, sign out, confirm protected pages unreachable; with a 1-minute lifetime wait and confirm redirect with the expired message.

### Tests for User Story 3

- [X] T047 [P] [US3] Expiry contract tests in `backend/tests/BillingCrm.Api.Tests/TokenValidationTests.cs` (contracts/auth-api.md test 4, plus test 7): expired token → 401; tampered token → 401; wrong-issuer token → 401; missing or short `Jwt:SigningKey` → start-up fails naming `Jwt:SigningKey` with no value in the message
- [X] T048 [P] [US3] Extend `frontend/src/app/core/auth/auth.service.spec.ts`: `signOut()` clears the signal and storage; a stored session past `expiresAtUtc` is treated as signed out
- [X] T049 [P] [US3] Extend `auth.guard.spec.ts` and `auth.interceptor.spec.ts`: an expired session sends `authGuard` to `/login?reason=expired&returnUrl=...`; any 401 (except from the login request) clears the session and navigates to `/login?reason=expired&returnUrl=...`
- [X] T050 [P] [US3] Extend `login.component.spec.ts` and `shell.component.spec.ts`: `reason=expired` shows "Your session expired. Sign in again."; after re-login the user returns to `returnUrl`; the shell's **Sign out** button signs out and navigates to `/login`

### Implementation for User Story 3

- [X] T051 [US3] Add `signOut()` and expiry evaluation (`isExpired`, treating a stored past-expiry session as signed out) to `frontend/src/app/core/auth/auth.service.ts` (depends on T030)
- [X] T052 [US3] Update `authGuard`/`roleGuard` in `frontend/src/app/core/auth/auth.guard.ts` to redirect an expired session to `/login?reason=expired&returnUrl=...` (depends on T051)
- [X] T053 [US3] Update `frontend/src/app/core/auth/auth.interceptor.ts`: on a 401 (not from `/api/auth/login`) call `signOut()` and navigate to `/login?reason=expired&returnUrl=...` (depends on T051)
- [X] T054 [US3] Update `frontend/src/app/features/login/login.component.ts` to show the expired message when `reason=expired` (depends on T033)
- [X] T055 [US3] Add the **Sign out** button to `frontend/src/app/core/layout/shell.component.ts`; navigate with `replaceUrl: true` so the Back button does not return to a protected page (depends on T051)
- [X] T056 [US3] Run all back-end and front-end tests; fix until T047–T050 pass
- [X] T057 [US3] Manual check with the real API: with `Jwt__ExpiryMinutes=1` sign in, wait 61 s, confirm the next `curl` of `/api/auth/me` is 401 and record it in `specs/002-staff-auth/research.md`

**Checkpoint**: US3 acceptance scenarios 1–4 pass via tests.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T058 [P] Update `README.md`: signing-key step (before `dotnet ef`), applying the new migration, that the seed accounts are created on the first Development start (point to `CLAUDE.md` for the values, do not repeat passwords), sign-in URL, new tests, troubleshooting for a missing signing key and for "seeder skipped: pending migrations"
- [X] T059 [P] Update `CLAUDE.md` layout and run commands (new `Application.Tests` project, `Jwt:SigningKey` secret, `/api/auth` and `/api/admin` groups, `Result` type), keeping the seed users section
- [X] T060 [P] Secret scan of everything committable: search for the generated signing key value, `Jwt:SigningKey` assignments in any `appsettings*.json`, `Bearer ey`, `Server=` outside the known placeholders; confirm only the fake test key and documented dev seed values exist (SC-006)
- [X] T061 [P] Logging and leakage check: search `backend/src` for log statements or responses that could include a password, token, hash or signing key; confirm 401/403/400 bodies match contracts/auth-api.md and contain no exception text
- [X] T062 Run the full quickstart validation (`specs/002-staff-auth/quickstart.md`) on the developer's machine, including all browser checks (sign in as both roles, Admin link visibility, direct `/admin` as Agent, sign out and Back button, expiry redirect and return); the developer confirms it is running (Constitution II)
- [X] T063 Run `api-reviewer`, `angular-reviewer`, `spec-auditor` and **`security-reviewer`**; fix or explicitly record every finding (Constitution workflow gate; the project requires `security-reviewer` after 002)
- [X] T064 Re-run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; all pass (SC-007)
- [X] T065 Mark spec 002 done only after T062 and T063 are complete; do not start spec 003 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates all code generation. T002, T003 parallel; T004 before T009.
- **Foundational**: T005–T007 parallel; T008 after T005; T009 after T004 and T008; T010 parallel with T005–T008; T011 after T010; T012 after T011 (and uses the real hasher once T023 exists); T013 after T011; T014 after T006.
- **US1**: after Foundational. Back end (T021–T028) and front end (T029–T034) are independent tracks. T036 needs T028.
- **US2**: after US1 (needs the login flow and `AuthService`); T040 needs only Foundational.
- **US3**: after US1; the front-end edits (T051–T055) touch files created in US1, so they follow it.
- **Polish**: after all stories. T062 and T063 are the done-ness gate.

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories.
- **US2 (P2)**: needs US1's sign-in.
- **US3 (P3)**: needs US1's sign-in; its back-end token tests (T047) need only Foundational.

### Within Each Story

Tests first and failing → implementation → run tests. Back end: Application → Infrastructure → registration → endpoint. Front end: models → `AuthService` → interceptor/guard → pages → routes.

### Parallel Opportunities

- Setup: T002, T003.
- Foundational: T005, T006, T007, T010, then T014.
- US1: tests `T015–T020` together; then `T022, T023, T024` together; `T029` alongside back-end tasks.
- US2: tests `T037–T039` together; `T043, T044` together.
- US3: tests `T047–T050` together.
- Polish: T058, T059, T060, T061.

---

## Parallel Example: User Story 1

```text
# Tests, all at once:
T015 AuthServiceTests.cs   T016 LoginEndpointTests.cs   T017 DevelopmentSeederTests.cs
T018 auth.service.spec.ts  T019 login.component.spec.ts  T020 guard + interceptor specs

# Then two tracks:
Back end:   T021 -> (T022 || T023 || T024) -> T025 -> T026 -> T027 -> T028
Front end:  T029 -> T030 -> (T031 || T032 || T033) -> T034
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001–T004) and Foundational (T005–T014).
2. US1 (T015–T036).
3. Stop and validate: both seeded users sign in and reach the shell, bad credentials get nothing. This matches the request's acceptance scenario.

### Incremental Delivery

1. Add US2 (T037–T046): roles enforced in the API and UI.
2. Add US3 (T047–T057): sign-out and expiry.
3. Polish, four reviewers, developer verification (T058–T065).

---

## Notes

- T001 is a hard gate; do not generate code before the developer answers.
- The signing key is generated and stored on the developer's machine only (T004); never write it into a file, command history in docs, or task output.
- Known gaps deliberately left for `012-security` (spec FR-018): lockout/throttling, server-side revocation, refresh tokens, password change.
- Commit only when the developer asks; there are no commits yet.
