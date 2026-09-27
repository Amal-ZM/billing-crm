# Implementation Plan: Security Hardening

**Branch**: `012-security` | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/012-security/spec.md`

## Summary

The last spec in the roadmap. Most of User Story 1 and 2's guarantees (JWT on every write, Agent refused on three specific actions,
400/404/409 for expected problems) already exist from specs 002-011; this spec makes each one explicit, total (every resource, not just
the ones a prior spec happened to test) and pinned by one consolidated cross-cutting test suite, rather than trusting that per-spec
coverage never had a gap. It then closes five bounded hardening items that were explicitly deferred across specs 004, 007, 008, 009 and
010 (a decimal JSON converter, anti-framing/no-store response headers, a request body size limit, login rate limiting, and database-level
immutability for `Payments`), adds a shared free-text control-character guard, and publishes `SECURITY.md` at the repository root as the
one place all of this — done and consciously still open — is recorded.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript / Angular 21 (frontend) — unchanged from every prior spec

**Primary Dependencies**: ASP.NET Core Minimal API (adding its own built-in `Microsoft.AspNetCore.RateLimiting` middleware, already part of
the shared framework, no new package); EF Core 8 (SQL Server provider); Angular Material — no new external dependency anywhere

**Storage**: SQL Server LocalDB. One new migration: a trigger on the existing `Payments` table (no new column, no new table), mirroring
010's `AddInvoiceHistory` migration exactly

**Testing**: xUnit (`BillingCrm.Application.Tests`, `BillingCrm.Api.Tests`, `BillingCrm.Infrastructure.Tests`); Vitest (Angular, `ng test`)

**Target Platform**: Web (existing .NET 8 API + Angular 21 SPA); still developer-machine only (`http://localhost`), no deployment target,
which is exactly why HTTPS/HSTS stays an open item rather than being closed here

**Performance Goals**: No change; the new checks (a converter reading a JSON token, a control-character scan of already-length-capped
text, a rate limiter keyed by client address) are all O(1)-per-request work, not a new bottleneck

**Constraints**: Every new refusal keeps the existing shape wherever the framework allows it (a 400 naming its field through the existing
`Result`/`ProblemDetails` path); the two exceptions are the request-body-size limit (413) and the rate limiter (429), which are enforced
by ASP.NET Core middleware before a request reaches any endpoint handler and so cannot go through `Result` — both already produce a
structured `ProblemDetails`-shaped body by default in .NET 8, which keeps Principle III's spirit (a predictable, documented shape) even
though the mechanism differs; this is noted, not treated as a violation. Money stays `decimal(18,2)` SAR end to end (Constitution IV); no
secret in any committed file (Constitution III)

**Scale/Scope**: Five closed hardening items, one shared validation helper, one consolidated cross-cutting test suite (Api.Tests, plus
Application.Tests for the new validator checks and Infrastructure.Tests for the new trigger), one new migration, one new root-level
document; no new screen, no new domain entity

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Scope Discipline**: purely defensive hardening and documentation of existing behaviour; no shop, cart, payment gateway, ticket or
  WhatsApp concept anywhere near it. **Pass.**
- **II. Sequential Spec Delivery**: 011 is verified running; this is next and last in sequence, depending on everything before it, all
  verified. **Pass.**
- **III. Explicit Business Errors**: every new check that is a genuine business-rule refusal (the decimal converter's precision/exponent
  rule, the control-character guard) is modelled as a normal field-level 400 through the existing `Result`/`ProblemDetails` path, exactly
  like every check before it. The body-size limit (413) and the rate limiter (429) are not business rules; they are transport-level
  protections enforced by ASP.NET Core middleware ahead of any endpoint, so they cannot be `Result`-shaped, and are called out above as a
  deliberate, narrow exception rather than silently bent. **Pass, with that one noted exception.**
- **IV. Financial Data Integrity**: the decimal converter exists specifically to close a gap in this principle (an over-precise or
  exponent-notation number silently rounding instead of being refused); nothing here introduces a `float`/`double` anywhere, and no secret
  is touched by any change. **Pass.**
- **V. Deliberate API/UI Design**: five genuinely new patterns for this codebase need the developer's confirmation before being built —
  (1) a custom global `decimal` `JsonConverter`, the first custom converter in the API; (2) ASP.NET Core's built-in rate-limiting
  middleware, applied only to login, the first use of that feature; (3) a small cross-cutting header middleware (anti-framing, no-store),
  the first hand-written middleware in `Program.cs` (everything so far has been endpoint policies or `Result`/`ProblemDetails`); (4) a
  shared `FreeTextGuard` validation helper called from four existing validators, the first cross-validator shared abstraction; (5) a
  database trigger on `Payments`, which reuses spec 010's exact trigger shape on a different table, so it is confirmed together with the
  others but needs less explanation than a genuinely new mechanism would. **Pass, with five confirmation gates** (fewer than they sound:
  items 1-4 are each small and item 5 is a repeat of an already-approved pattern).

## Project Structure

### Documentation (this feature)

```text
specs/012-security/
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
│   ├── BillingCrm.Domain/              (no change: no new entity, no new state)
│   ├── BillingCrm.Application/
│   │   ├── Common/                     new FreeTextGuard (the shared control-character check)
│   │   ├── Customers/CustomerValidator.cs      gains the FreeTextGuard check on name, address
│   │   ├── Products/ProductValidator.cs        gains the FreeTextGuard check on name
│   │   └── Invoices/{InvoiceValidator,PaymentValidator}.cs   gain the FreeTextGuard check on notes/note
│   ├── BillingCrm.Infrastructure/
│   │   ├── Persistence/Configurations/PaymentConfiguration.cs   gains HasTrigger, like 010's history config
│   │   ├── Persistence/Migrations/                              new migration: the Payments trigger
│   │   └── Serialization/                                       new DecimalJsonConverter
│   └── BillingCrm.Api/
│       ├── Program.cs                  gains the decimal converter registration, the rate limiter, and the
│       │                                header middleware (anti-framing, no-store)
│       └── (no new endpoint; existing routes gain the login rate limit and the new validation rules)
├── tests/
│   ├── BillingCrm.Application.Tests/       FreeTextGuard tests, one new case per validator that uses it
│   ├── BillingCrm.Api.Tests/                a new consolidated SecurityRegressionTests.cs (every write route's
│   │                                          401/403 in one place), decimal converter tests, header tests,
│   │                                          rate-limit tests, body-size tests
│   └── BillingCrm.Infrastructure.Tests/     the Payments trigger, real SQL

frontend/                (no source change expected: output encoding is already Angular's default interpolation
                          everywhere, with no innerHTML/bypassSecurityTrust anywhere in the app; this spec adds a
                          repo-wide automated check that this stays true, not a code change)

SECURITY.md              new, at the repository root
```

**Structure Decision**: Existing `backend/` (Domain/Application/Infrastructure/Api) + `frontend/` (Angular) split, unchanged. Every change
here is additive to existing files or a small number of new, narrowly-scoped files (one converter, one shared validator helper, one
migration, one root document); nothing is restructured.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|---------------------------------------|
| 413 (body too large) and 429 (rate limited) do not go through `Result`/`ProblemDetails` | Both are enforced by ASP.NET Core middleware before any endpoint handler runs, so there is no `Result` to return yet | Hand-rolling body-size and rate-limit checks inside every endpoint to force a `Result` shape would duplicate framework behaviour that already produces a structured `ProblemDetails` body, for no gain in predictability |
