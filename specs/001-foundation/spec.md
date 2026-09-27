# Feature Specification: Foundation

**Feature Branch**: `001-foundation`

**Created**: 2026-09-24

**Status**: Done (verified running by the developer 2026-09-24)

**Input**: User description: "Feature 001-foundation. Empty BillingCRM that runs: .NET 8 projects Domain, Application, Infrastructure, Api; Angular standalone + Material shell; LocalDB; EF Core; GET /health; CORS; README. Given I run API and Angular, When I open /health and the Angular home, Then both succeed. Out of scope: login, tables for customers."

## Clarifications

### Session 2026-09-24

- Q: Should the health address check that the database is reachable, or only that the API is running? → A: Check the database; success only when the API is up and the database is reachable.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the empty BillingCRM end to end (Priority: P1)

A developer clones the repository on a fresh machine, follows the README, starts the back-end
service and the web front end, and confirms both are alive: the service's health address reports
success, and the web application's home page loads inside a styled application shell. No business
features exist yet; this is the running skeleton every later spec builds on.

**Why this priority**: Every later spec (001–012) assumes a working, verified skeleton. Without
this, nothing else can be verified on the developer's machine (Constitution Principle II).

**Independent Test**: Start the back-end and the front end using only the README instructions,
open the health address and the web home page in a browser, and observe that both succeed.

**Acceptance Scenarios**:

1. **Given** the back-end service and the front end are both running, **When** I open the health
   address in a browser, **Then** I receive a successful response stating the service is healthy.
2. **Given** the back-end service and the front end are both running, **When** I open the web
   application's home page, **Then** it loads without errors and shows the application shell
   (a header with the product name and a main content area).
3. **Given** the front end is running, **When** its home page loads and contacts the back-end
   service from the browser, **Then** the browser does not block the request for cross-origin
   reasons.

---

### User Story 2 - Onboard from the README alone (Priority: P2)

A new developer with no prior knowledge of the project reads the README and reaches the running
state from User Story 1 without asking anyone for help, including supplying the local database
connection settings outside source control.

**Why this priority**: The skeleton is only useful if anyone can reproduce it; secrets must stay
out of git, so the README is the only place the local-setup steps can live.

**Independent Test**: A developer who has never seen the project follows the README verbatim on a
clean checkout and reaches User Story 1's acceptance scenarios.

**Acceptance Scenarios**:

1. **Given** a clean checkout and the prerequisites listed in the README, **When** I follow the
   README steps in order, **Then** I reach a running back-end service and front end.
2. **Given** I have not yet supplied local database settings, **When** I start the back-end
   service, **Then** I get a clear message telling me which setting is missing and where the
   README explains how to provide it.

---

### User Story 3 - Database connectivity is proven, empty (Priority: P3)

The developer can confirm the back-end service is connected to a local database that exists but
holds no business data. The project is set up so later specs can add data structures through
tracked, repeatable changes.

**Why this priority**: Confirms the persistence layer is wired correctly early, but delivers no
user-visible feature beyond P1 and P2.

**Independent Test**: With the local database available, open the health address and confirm it
reports the database as reachable; then make the database unavailable (for example, point the
service at a database that does not exist) and confirm the health address reports the problem
instead of success.

**Acceptance Scenarios**:

1. **Given** the local database is available and configured, **When** I open the health address,
   **Then** it reports the service healthy and the database reachable.
2. **Given** the local database is unavailable, **When** I open the health address, **Then** it
   reports an unhealthy result that names the database as the problem, rather than reporting
   success.

---

### Edge Cases

- The front end is started while the back-end service is not running: the home page still loads
  and the shell renders; it does not crash or show a blank screen.
- The front end is served from an address other than the one allowed for cross-origin requests:
  the back-end service refuses the request rather than allowing any origin.
- The local database connection setting is absent or malformed: the service fails fast at start
  with a message naming the missing setting (never printing a secret value).
- The health address is requested while the database is down: the response is a failure status,
  not a false success.
- The health address is requested without any credentials: it succeeds, since login does not
  exist yet and health checks must work unauthenticated.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain a back-end solution organised into four layers named
  Domain, Application, Infrastructure, and Api, with dependencies pointing inward (Api and
  Infrastructure depend on Application; Application depends on Domain; Domain depends on nothing).
- **FR-002**: The repository MUST contain a web front end consisting of an application shell (a
  header showing the product name and a main content area) and a home page rendered inside it.
- **FR-003**: The back-end service MUST expose a health address that responds successfully and
  states the service is healthy when the service and its database are available.
- **FR-004**: The health address MUST report the database as reachable or unreachable, and MUST
  return a non-success status when the database is unreachable.
- **FR-005**: The health address MUST be reachable without authentication.
- **FR-006**: The back-end service MUST connect to a local database instance, and MUST be set up
  so that later specs can introduce data structures through tracked, repeatable database changes.
- **FR-007**: This feature MUST NOT create any business data structures (no customers, products,
  or invoices); the database is empty of business data after setup.
- **FR-008**: The back-end service MUST allow cross-origin requests from the front end's local
  development address and MUST NOT allow arbitrary origins.
- **FR-009**: The list of allowed front-end origins MUST be configurable outside the code, with
  the local development address as the default for developer machines.
- **FR-010**: The database connection setting MUST be supplied from local configuration outside
  source control; no connection string, key, or credential may be committed, including in sample
  or default settings files (Constitution Principle IV).
- **FR-011**: If a required setting is missing at start-up, the service MUST fail with a clear
  message naming the setting and MUST NOT print secret values.
- **FR-012**: The repository MUST include a README describing prerequisites, how to supply the
  local database setting, how to start the back-end service and the front end, how to open the
  health address and home page, and how to run the automated tests.
- **FR-013**: The repository MUST include at least one automated test proving the health address
  responds successfully, and at least one proving the front end's shell renders.
- **FR-014**: Login, user accounts, roles, and any customer, product, or invoice functionality
  MUST NOT be part of this feature.

### Key Entities

None. This feature deliberately introduces no business entities or tables; only the empty
foundation on which they will be added by later specs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer following only the README reaches a running back-end service and front
  end in under 15 minutes on a machine that already has the listed prerequisites.
- **SC-002**: Opening the health address returns a healthy result in under 2 seconds, 100% of the
  time when the service and database are available.
- **SC-003**: Opening the front end's home page shows the application shell with no errors
  reported in the browser, on the first load.
- **SC-004**: With the database unavailable, 100% of requests to the health address report a
  failure that names the database, and none report success.
- **SC-005**: A search of everything committed to source control finds no connection strings,
  keys, or credentials.
- **SC-006**: All automated tests in the repository pass on the developer's machine.

## Assumptions

- The technology choices (.NET 8 Minimal API, Angular with standalone components and Angular
  Material, SQL Server LocalDB, EF Core, ProblemDetails for errors) are fixed by the project
  constitution and the feature request; they are not being re-decided here.
- The developer's machine already has the required runtimes, the LocalDB instance, and a package
  manager installed; the README lists them but does not install them.
- The health address reports database connectivity in addition to service liveness (confirmed
  in Clarifications, Session 2026-09-24).
- The default cross-origin allowance targets only the front end's standard local development
  address; production origins are out of scope for this spec.
- No login, roles, or seeded users exist yet; the seed users listed in the project guidance
  belong to a later spec.
- No initial database migration is created because there are no entities; the migration tooling
  is configured and ready but the first migration arrives with the first spec that adds a table.
- Currency and money rules (decimal, SAR) do not apply yet, as there are no monetary values.
- Shop, cart, payment gateway, support tickets, and WhatsApp integration remain out of scope for
  the whole product (Constitution Principle I).
