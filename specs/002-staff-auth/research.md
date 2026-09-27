# Research: Staff Authentication

**Feature**: 002-staff-auth | **Date**: 2026-09-24

No `NEEDS CLARIFICATION` markers remain in the spec. The items below record the decisions the
plan depends on. Items marked **Confirm** are new patterns or judgement calls that need the
developer's answer before code is generated (Constitution V), and are repeated in plan.md.

## R1. Token format and validation

- **Decision**: Signed JWT bearer tokens, HS256 with one symmetric key. Issued by the API at
  sign-in, validated by ASP.NET Core's `JwtBearer` handler. Claims: `sub` (user id), `email`,
  `role` (exactly `Admin` or `Agent`), `jti`, `iat`, `nbf`, `exp`. Issuer/audience/lifetime are checked;
  `ClockSkew` is zero so "expired" means expired.
- **Rationale**: JWT is fixed by the constitution. A symmetric key is enough because the same
  service issues and validates; asymmetric keys add rotation machinery nothing needs yet. Zero
  skew avoids the default 5-minute grace that would contradict spec FR-010 / SC-004.
- **Alternatives considered**: RS256 (rejected: no second consumer); cookie sessions (rejected:
  constitution says JWT bearer).

## R2. Signing key handling

- **Decision**: `Jwt:SigningKey` is **required**, supplied via user-secrets or env var
  `Jwt__SigningKey`, never in a committed file. Minimum 32 bytes of key material; a missing or
  too-short key stops start-up with an `InvalidOperationException` naming `Jwt:SigningKey` (never
  the value) — same pattern as the 001 connection string. Non-secret settings `Jwt:Issuer`,
  `Jwt:Audience`, `Jwt:ExpiryMinutes` (default 60) are committed in `appsettings.json`.
- **Rationale**: Constitution IV, spec FR-015/FR-016.
- **Consequence**: `dotnet ef ...` builds the host, so the key must be in user-secrets before any
  EF command; README setup order is updated accordingly.

## R3. Password hashing

- **Decision**: ASP.NET Core Identity's `PasswordHasher<T>` (package
  `Microsoft.Extensions.Identity.Core`, 8.x) behind an Application-level `IPasswordHasher`
  interface. Only the hash is stored.
- **Rationale**: Salted, iterated, versioned, and maintained by Microsoft; no hand-rolled
  cryptography.
- **Alternatives considered**: BCrypt/Argon2 packages (rejected: an extra dependency for no gain
  here).
- **User-enumeration defence**: when the email is unknown, sign-in still performs a dummy hash
  verification and returns the identical 401 response, so neither message nor timing reveals
  whether the email exists (spec FR-003).

## R4. Seeding the two dev accounts — confirmed 2026-09-24 (seeder class in source)

- **Decision (recommended)**: A Development-only startup seeder in the Api project creates
  `admin@crm.local` (Admin) and `agent1@crm.local` (Agent) if missing, hashing the passwords at
  runtime. It never runs outside the `Development` environment. It is skipped with a logged
  warning if migrations have not been applied. The two known dev passwords live in that one
  seeder class, clearly named as development-only; they are the values already documented in
  `CLAUDE.md` ("not real credentials, never reuse outside a developer's own LocalDB").
- **Rationale**: Only the hash is stored; the plain values exist in source only as documented,
  non-real dev credentials. A migration `HasData` seed was rejected because it would apply in
  every environment, violating spec FR-005 ("MUST NOT run outside local development"), and would
  bake a hash into a migration.
- **Alternatives considered**: (a) read the seed passwords from user-secrets (`Seed:AdminPassword`,
  `Seed:AgentPassword`): keeps every credential out of source, but each developer must set two more
  values and the documented dev logins would not work by default. (b) put them
  in `appsettings.Development.json` (rejected: same exposure as (recommended) but hidden in config
  and it would trip the repo's secret-blocking hook's intent).

## R5. Expected failures as `Result` → `ProblemDetails` — confirmed 2026-09-24

- **Decision**: Introduce the `Result` / `Result<T>` type in Application now (first business
  failure exists: bad credentials). `Error` carries a machine code, a message, and a kind
  (`Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`). One Api extension maps a
  failed `Result` to `ProblemDetails` with the right status (400/401/403/404/409). Missing or
  invalid tokens and forbidden access are turned into `ProblemDetails` by
  `AddProblemDetails()` + status-code pages, so 401/403 bodies are uniform.
- **Rationale**: Constitution III and `.claude/rules/api.md`; this is the pattern every later spec
  reuses.
- **Alternatives considered**: throwing for bad credentials (rejected by constitution); a
  third-party Result library (rejected: a 30-line type suffices).

## R6. Authorization model

- **Decision**: `FallbackPolicy = RequireAuthenticatedUser`, so any endpoint someone forgets to
  annotate is closed, plus explicit per-endpoint declarations: `/health` and `POST
  /api/auth/login` use `.AllowAnonymous()`; `GET /api/auth/me` uses `.RequireAuthorization()`;
  `/api/admin/*` uses `.RequireAuthorization("AdminOnly")` where `AdminOnly` requires role
  `Admin`. Endpoints grouped per resource with `MapGroup` (`/api/auth`, `/api/admin`), per the
  Minimal API skill.
- **Rationale**: `.claude/rules/api.md` requires an explicit authorization requirement on every
  endpoint; the fallback policy is the safety net that makes "forgot to annotate" fail closed.
- **Note**: Only two roles exist; the `AdminOnly` policy is the only policy needed now. `Agent`
  needs no policy of its own (authenticated is enough).

## R7. The Admin-only placeholder (spec FR-011)

- **Decision**: One back-end check `GET /api/admin/status` (Admin only, returns a tiny
  placeholder body) and one Angular route `/admin` (Admin only, placeholder page). No business
  content. Later specs attach real Admin features to the same policy/guard.
- **Rationale**: Customers are out of scope, so this is the only way to test "Agent cannot open
  Admin routes" end to end.

## R8. Logout and where the session lives in the browser — confirmed 2026-09-24 (sessionStorage)

- **Decision**: Logout is client-only: discard the stored session and navigate to `/login`. No
  server endpoint and no revocation (spec assumption). The session
  (`{accessToken, expiresAtUtc, email, role}` as returned by login) is kept in `sessionStorage`,
  wrapped in try/catch, and re-checked for expiry on every navigation.
- **Rationale**: `sessionStorage` disappears when the tab closes, which suits shared machines
  named in the spec; returning the user info in the login response avoids decoding the token in
  the browser. The browser copy is used only for display and routing; the API is the
  enforcement point.
- **Alternatives considered**: `localStorage` (survives restarts; larger exposure on shared
  machines); memory-only (a page refresh would sign the user out); HttpOnly cookies (constitution
  says bearer tokens).

## R9. Angular structure — confirmed 2026-09-24

- **Decision**: Under `core/auth/`: `AuthService` (sign-in, sign-out, session signal),
  `authInterceptor` (functional `HttpInterceptorFn`: adds `Authorization: Bearer` only to
  requests under `environment.apiBaseUrl + '/'`; on a 401 clears the session and routes to
  `/login?reason=expired`, except for the login request itself), `authGuard` and
  `roleGuard('Admin')` (functional `CanActivateFn`s). Features: `features/login/` and
  `features/admin/` plus an `access-denied` page. Routes: `/login` public; the shell and its
  children require `authGuard`; `/admin` also requires `roleGuard('Admin')`. The `angular-ui-
  conventions` skill already requires an auth interceptor and role-aware UI; the functional
  guards and session service are the new shapes.
- **Rationale**: Matches the skill, uses current Angular idioms (functional guards, signals) with
  no NgModules.
- **Alternatives considered**: class-based guards/interceptors (legacy style); NgRx (rejected: a
  state-management library needs its own discussion per `.claude/rules/ui.md`).

## R10. Expired sessions

- **Decision**: Two layers. (1) Client: `authGuard` compares `expiresAtUtc` with the clock on every
  navigation; if past, it clears the session and redirects to `/login?reason=expired&returnUrl=...`.
  It is registered as both `canActivate` and `canActivateChild` on the shell route, because
  `canActivate` alone runs only when the shell first activates, not when moving between its child
  pages (found by `angular-reviewer`; covered by a test in `app.spec.ts`).
  (2) Server: a 401 to a request that carried the person's current token does the same; a 401 to a
  request with no token, or with an older token, is ignored so it cannot sign out the wrong
  session. After re-login the user returns to `returnUrl` (spec US1.6 / US3.4).
- **Rationale**: The client check gives instant feedback; the server check is the authority
  (e.g., clock drift).

## R11. Data and migration

- **Decision**: First real migration `AddStaffUsers` creating `StaffUsers` (see data-model.md).
  Role stored as a string (`Admin`/`Agent`) with a database check constraint limiting it to those
  two values. Unique index on the normalized email. Applied with the existing
  `dotnet ef database update` step from 001.
- **Rationale**: `sql-migrations` skill (add a migration; never edit an applied one; do not drop
  the database). No money columns exist.

## R12. Testing approach

- **Decision**: Application unit tests for the sign-in service (fakes for store, hasher,
  token issuer). Api tests via `WebApplicationFactory` with the user store replaced by an
  in-memory fake (no LocalDB), covering: login success for both roles, wrong password, unknown
  email, empty fields, `/me` with and without a token, Agent refused on `/api/admin/status`
  (403), Admin allowed, tampered token (401), expired token crafted with the test key (401), and
  `/health` still anonymous. Frontend specs: `AuthService`, interceptor, both guards, login
  component, shell role-dependent link. Real-database behaviour (migration, seeder) is verified
  manually per quickstart.
- **Rationale**: Fast, deterministic, no machine dependencies, same approach as 001.

## R13. Time

- **Decision**: Token issuance uses `TimeProvider` (built into .NET 8) so the issued `exp` is
  testable. Expired-token API tests craft a token directly with the test key and a past `exp`.
- **Alternatives considered**: sleeping in tests (slow and flaky).

## R14. Out of scope, deliberately

Lockout/throttling, password change/reset, registration, user management UI, refresh tokens,
server-side revocation, MFA (spec FR-018). Because lockout is absent, the plan records it as a
known gap for `012-security`.

## Verification log

### T036 — US1 against the real LocalDB (2026-09-24)

- Development start-up log: "Development seed accounts are in place." `StaffUsers` held exactly
  `admin@crm.local` (Admin) and `agent1@crm.local` (Agent); stored hashes were 84 characters and
  contained neither dev password.
- `POST /api/auth/login`: Admin 200 role Admin; Agent (email sent as `  AGENT1@crm.local `) 200 role
  Agent; wrong password and unknown email both 401 with the identical `Invalid credentials` body
  and no token; empty fields 400 with `errors.email` and `errors.password`.
- `GET /api/auth/me`: no token 401; Agent token 200 `{"email":"agent1@crm.local","role":"Agent"}`.
- `GET /health` with no token: 200 (still anonymous).

### T057 — roles and expiry against the real API (2026-09-24)

Run with `Jwt__ExpiryMinutes=1`, real LocalDB, seeded accounts:

- Admin token → `GET /api/admin/status` 200; Agent token → 403; no token → 401; garbled token → 401.
- Agent token → `GET /api/auth/me` 200.
- After waiting 62 s, the same Admin and Agent tokens → 401 (zero clock skew: no grace period).

## Review disposition (T063, 2026-09-24)

Four read-only reviews ran: `api-reviewer`, `angular-reviewer`, `spec-auditor`, `security-reviewer`.
None reported a Critical or High finding.

### Fixed

- Expiry re-check between shell child pages (`canActivateChild`); a stale or token-less 401 no longer
  signs out the wrong session; whitespace-only email is treated as empty; API problem text is shown
  for 400/5xx; post-sign-in navigation uses `replaceUrl`; double submit ignored; sign-in response shape
  validated; `<nav>`/`<main>`/heading landmarks and a "Back to home" link.
- Signing algorithm pinned to HS256; `IncludeErrorDetails` off; low-entropy keys rejected (fewer than 10
  distinct characters); dummy password hash built eagerly (no first-request timing gap); email and
  password length caps (256 / 128) returning 400; `UseExceptionHandler` outside Development; CORS
  limited to the needed headers and methods; `AsNoTracking` on the user lookup.
- Seeder: registered only in Development, and the start-up service also refuses any database that is
  not a `(localdb)\` instance.
- Tests: the flaky `Tampered()` token helper (it changed only base64 padding bits about 1 run in 16)
  now alters a mid-signature character; added tests for `JwtTokenService` claims/lifetime,
  `IdentityPasswordHasher`, `JwtOptions` validation, `Result` -> `ProblemDetails` for all kinds,
  malformed login bodies, the Forbidden title, CORS header narrowing, seeder guards, and the
  interceptor/login/guard gaps above.
- Docs: seed passwords no longer repeated in the spec or research; status, operation count and claim
  list corrected.

### Deliberately not done (recorded gaps)

| Item | Why | Where it goes |
|------|-----|---------------|
| Login rate limiting / lockout (security M1) | Spec FR-018 excludes throttling and lockout; rated Low while the API is localhost-only, Medium once reachable elsewhere. The dev accounts have well-known passwords. | **Spec 012 (security)** — use the built-in .NET 8 rate limiter on `POST /api/auth/login` plus lockout |
| HTTPS redirect, HSTS, `AllowedHosts` restricted to `localhost` (L6) | Localhost-only tool today; credentials cross loopback in clear text | Before any deployment spec: HTTPS profile, `UseHttpsRedirection`/`UseHsts`, `AllowedHosts`, https `environment.ts` |
| Content-Security-Policy (L9) | Hardening only; no XSS sinks found; sessionStorage was a confirmed decision | Deployment/security spec |
| Server-side revocation, refresh tokens, stale role until expiry (L10) | Spec FR-018 and the sign-out assumption | Spec 012 / user-management spec |
| Pinned NuGet versions (`8.*`, `7.*`) | Reproducibility, not correctness | `Directory.Packages.props` + `dotnet list package --vulnerable` in CI |
| Focus management after failed submit / sign-out; `role="status"` announcement on first render | Accessibility polish, no functional defect | UI polish pass |
| DevelopmentSeedStartupService positive path and pending-migration skip | Need a real DbContext; verified manually in T036 | Integration-test project if one is introduced |
