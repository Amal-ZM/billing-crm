<!--
Sync Impact Report
- Version change: [TEMPLATE] → 1.0.0 (initial ratification)
- Modified principles: n/a (first concrete adoption of the constitution)
- Added sections:
  - Core Principles: I. Scope Discipline, II. Sequential Spec Delivery,
    III. Explicit Business Errors, IV. Financial Data Integrity,
    V. Deliberate API/UI Design
  - Technology & Security Constraints (Section 2)
  - Development Workflow & Quality Gates (Section 3)
  - Governance
- Removed sections: none (template placeholders replaced with concrete content)
- Templates requiring follow-up: none found referencing outdated principle
  names; `.specify/templates/*` are resolved at command time and read this
  file directly, no manual sync required.
- Deferred TODOs: none
-->

# BillingCRM Constitution

## Core Principles

### I. Scope Discipline (Internal CRM Only)
BillingCRM is an internal staff tool covering exactly three domains: customers, products, and
invoices. Features MUST NOT introduce a shop, cart, payment gateway, support tickets, or WhatsApp
integration, regardless of how naturally they might seem to fit a "billing" or "CRM" product.
Any request that implies one of these excluded domains MUST be flagged and scoped back before
implementation begins.

**Rationale**: The excluded domains are the most common way a "billing CRM" scope-creeps into an
e-commerce or support platform. Naming them explicitly prevents silent expansion of the product.

### II. Sequential Spec Delivery
Work proceeds as specs numbered 001–012, strictly in order. The next spec in the sequence MUST
NOT be started until the current spec has been verified running on the developer's own machine.
Partial or unverified specs MUST NOT be treated as a foundation for subsequent work.

**Rationale**: Sequential, verified delivery keeps each spec's assumptions grounded in a working
system instead of compounding unverified changes across multiple specs.

### III. Explicit Business Errors (Result/ProblemDetails, Not Exceptions)
Expected business-rule failures (validation failures, not-found, conflict, authorization denial,
etc.) MUST be modeled as a `Result`-style return value and surfaced to clients as
`ProblemDetails` responses. Exceptions are reserved for truly unexpected failures (bugs,
infrastructure faults), never for anticipated business outcomes.

**Rationale**: Using exceptions for control flow on expected outcomes hides the API's real
contract and makes error handling unpredictable for both the Angular client and future
maintainers.

### IV. Financial Data Integrity
All monetary values MUST be stored and computed as `decimal(18,2)` in SAR. No monetary value may
be represented as `float`/`double` at any layer (database, API, or client). No secrets
(connection strings, API keys, JWT signing keys, credentials) may be committed to git in any
form, including sample/appsettings files; they MUST be supplied via configuration outside source
control.

**Rationale**: Billing correctness depends on exact decimal arithmetic, and this is a financial
system where credential leakage or rounding errors have direct real-world cost.

### V. Deliberate API/UI Design
Before generating a new kind of file (a new Minimal API endpoint pattern, a new Angular
architectural pattern, a new shared abstraction, etc.), the idea MUST be explained to the
developer first and confirmed, rather than generated speculatively. Established, already-approved
patterns in the codebase do not require re-explanation.

**Rationale**: This is a learning-and-review checkpoint: new architectural shapes have outsized
long-term cost if adopted without discussion, while repeating an already-agreed pattern does not.

## Technology & Security Constraints

- Backend: .NET 8 Minimal API.
- Frontend: Angular, standalone components, Angular Material.
- Database: SQL Server LocalDB.
- AuthN/AuthZ: JWT bearer tokens; roles are exactly `Admin` and `Agent`. No additional roles may
  be introduced without a constitution amendment.
- No secrets of any kind are committed to git (see Principle IV).

## Development Workflow & Quality Gates

- When editing API or UI code, the relevant project skill(s) for that area MUST be used rather
  than ad hoc edits.
- The `api-reviewer` skill MUST be run after completing each spec, before that spec is considered
  done and before the next spec (Principle II) begins.

## Governance

This constitution supersedes ad hoc conventions for all work in this repository. All specs,
plans, and reviews MUST verify compliance with the principles above before being marked complete.

**Amendment procedure**: Amendments are made by editing this file, updating the Sync Impact
Report at the top, and bumping the version per the policy below. Any change that removes or
redefines scope (e.g., Principle I's exclusions) requires explicit confirmation from the project
owner before being ratified.

**Versioning policy** (semantic versioning for governance):
- MAJOR: Backward-incompatible governance changes (e.g., removing a principle, changing the
  excluded-domains list, changing roles).
- MINOR: A new principle or materially expanded guidance is added.
- PATCH: Wording clarifications and non-semantic fixes.

**Compliance review**: Each spec's implementation is checked against this constitution as part of
the `api-reviewer` pass required in the Development Workflow section above.

**Version**: 1.0.0 | **Ratified**: 2026-09-22 | **Last Amended**: 2026-09-22
