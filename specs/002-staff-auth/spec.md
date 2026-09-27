# Feature Specification: Staff Authentication

**Feature Branch**: `002-staff-auth`

**Created**: 2026-09-24

**Status**: Done (verified running by the developer 2026-09-24)

**Depends on**: 001-foundation (verified running)

**Input**: User description: "Feature 002-staff-auth. Depends on 001. JWT login. Roles Admin, Agent. Seed admin@crm.local and agent1@crm.local (passwords as documented in CLAUDE.md). Login + logout. Agent cannot open Admin routes. Bad password = no token. Expired token → login. Given valid admin credentials, When I login, Then I see the app shell. Out of scope: customers."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in and reach the app shell (Priority: P1)

A staff member opens BillingCRM, is asked to sign in, enters their work email and password, and,
if the credentials are correct, lands in the application shell, which shows who they are and
which role they hold. If the credentials are wrong, they stay on the sign-in page with a clear
message and get no access.

**Why this priority**: Nothing else in the product (customers, products, invoices) can be built
or used until staff can identify themselves. This is the smallest slice that delivers value.

**Independent Test**: With the two seeded accounts on a local database, sign in as the Admin and
as the Agent and confirm each reaches the shell; then try a wrong password and an unknown email
and confirm neither gets in.

**Acceptance Scenarios**:

1. **Given** the seeded Admin account, **When** I sign in with its correct email and password,
   **Then** I see the application shell showing my email and the role "Admin".
2. **Given** the seeded Agent account, **When** I sign in with its correct email and password,
   **Then** I see the application shell showing my email and the role "Agent".
3. **Given** a valid email, **When** I sign in with a wrong password, **Then** I stay on the
   sign-in page, see a generic "email or password is incorrect" message, and no session is
   created.
4. **Given** an email that does not exist, **When** I sign in, **Then** I see the same generic
   message as for a wrong password (the message never reveals which part was wrong).
5. **Given** I leave the email or password empty, **When** I submit, **Then** I am told which
   field is required and nothing is sent for verification.
6. **Given** I am not signed in, **When** I open any application page directly by its address,
   **Then** I am taken to the sign-in page, and after signing in I arrive at the page I asked for.

---

### User Story 2 - Roles decide what I can open (Priority: P2)

Admins can open the Admin-only area; Agents cannot. An Agent never sees a way to it and, if they
type its address or call it directly, they are refused. The refusal comes from the back end, not
merely from hiding a link.

**Why this priority**: Roles are the reason authentication exists in this product; every later
spec relies on this rule being correct and enforced on the server.

**Independent Test**: Sign in as the Agent, confirm no Admin link appears, open the Admin address
directly and confirm access is denied; repeat the same request to the back end with the Agent's
credential and confirm it is refused; repeat as Admin and confirm access is granted.

**Acceptance Scenarios**:

1. **Given** I am signed in as an Admin, **When** I open the Admin-only area, **Then** I see it.
2. **Given** I am signed in as an Agent, **When** the shell renders, **Then** it offers no link to
   the Admin-only area.
3. **Given** I am signed in as an Agent, **When** I open the Admin-only area by typing its
   address, **Then** I am shown an "access denied" message, not the area's content.
4. **Given** an Agent's valid credential, **When** it is used directly against an Admin-only back
   end operation, **Then** the back end refuses it as forbidden.
5. **Given** no credential, an invalid credential, or an expired credential, **When** it is used
   against any protected back end operation, **Then** the back end refuses it as unauthenticated.

---

### User Story 3 - Sign out and expired sessions (Priority: P3)

A signed-in person can sign out, which returns them to the sign-in page and removes their access
on that browser. If their session runs out of time, the next thing they do sends them back to the
sign-in page with a message that the session expired.

**Why this priority**: Important for safety on shared machines, but the product is usable in a
demo without it once sign-in and roles work.

**Independent Test**: Sign in, sign out, and confirm protected pages are no longer reachable;
sign in again with a session lifetime set very short, wait for it to run out, and confirm the
next action lands on the sign-in page with the "expired" message.

**Acceptance Scenarios**:

1. **Given** I am signed in, **When** I choose to sign out, **Then** I am taken to the sign-in
   page.
2. **Given** I have just signed out, **When** I use the browser's back button or type a protected
   address, **Then** I am taken to the sign-in page and no protected content is shown.
3. **Given** my session has expired, **When** I navigate to another page or trigger a request,
   **Then** I am taken to the sign-in page with a message that my session expired.
4. **Given** my session has expired, **When** I sign in again, **Then** I return to the page I was
   on.

---

### Edge Cases

- The session runs out while a page is open: the next request is rejected as unauthenticated and
  the person is sent to sign-in with the "expired" message; no stale data remains on screen.
- A modified or forged credential is presented: it is treated exactly like no credential.
- An already signed-in person opens the sign-in page: they are taken to the shell instead.
- The email is entered with different letter casing or stray spaces around it: it still matches
  the account.
- Repeated wrong passwords: this spec applies no lockout or throttling (see Assumptions); every
  attempt gets the same generic response and never creates a session.
- The back end is unreachable at sign-in: the person sees a "cannot reach the service" message,
  distinct from "wrong email or password".
- The health address from 001 remains reachable without signing in.
- The required session-signing secret is missing when the back end starts: it fails to start with
  a message naming the setting, never printing a value.
- Seeding is run twice on the same database: no duplicate accounts and no changed passwords.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Staff MUST be able to sign in with an email address and a password.
- **FR-002**: A successful sign-in MUST give the person a time-limited session credential that
  states who they are and their role; a failed sign-in MUST NOT produce any credential.
- **FR-003**: A failed sign-in (wrong password or unknown email) MUST produce the same generic
  message and response, revealing neither which part was wrong nor whether the email exists.
- **FR-004**: There MUST be exactly two roles, Admin and Agent, and every staff user MUST hold
  exactly one of them. No other role may exist.
- **FR-005**: On a local development database the system MUST provide two seeded accounts:
  `admin@crm.local` with role Admin and `agent1@crm.local` with role Agent, with the passwords
  given in the project guidance (`CLAUDE.md`). Seeding MUST be repeatable without creating
  duplicates or overwriting existing accounts, and MUST NOT run outside local development.
- **FR-006**: Passwords MUST be stored only as one-way hashes and MUST NOT appear in logs, error
  messages, or any response.
- **FR-007**: After sign-in the person MUST see the application shell, which shows their email
  and role and a sign-out control.
- **FR-008**: A person who is not signed in MUST NOT see the shell or any protected page; they
  MUST be sent to the sign-in page, and after signing in MUST arrive at the page they originally
  requested.
- **FR-009**: Signing out MUST remove the person's access in that browser and return them to the
  sign-in page; protected pages MUST NOT be reachable afterwards without signing in again.
- **FR-010**: When a session expires, the person's next navigation or request MUST send them to
  the sign-in page with a message that the session expired.
- **FR-011**: The product MUST include an Admin-only area (a placeholder page with no business
  content) that only Admins can open. Agents MUST NOT be offered a link to it, and opening it by
  address MUST show an access-denied message.
- **FR-012**: The back end MUST enforce roles itself: a request with no, invalid, or expired
  credential MUST be refused as unauthenticated, and an Agent's request to an Admin-only
  operation MUST be refused as forbidden. Hiding controls in the front end MUST NOT be the only
  protection.
- **FR-013**: Every back-end operation MUST declare its access requirement explicitly. Only the
  health address (from 001) and sign-in MAY be open to anonymous callers.
- **FR-014**: Sign-in failures and access denials MUST be returned as structured error responses
  with the appropriate status (invalid input, unauthenticated, forbidden), not as unhandled
  failures (Constitution Principle III).
- **FR-015**: The session lifetime MUST be configurable, with a default of 60 minutes.
- **FR-016**: The secret used to sign session credentials MUST be supplied from local
  configuration outside source control, never committed, and its absence at start-up MUST stop
  the service with a message naming the setting (Constitution Principle IV; same pattern as 001).
- **FR-017**: The repository MUST include automated tests covering: successful sign-in for both
  roles, wrong password, unknown email, Agent refused on an Admin-only operation, unauthenticated
  request refused, and expired session refused.
- **FR-018**: This feature MUST NOT include customers, products, or invoices, user
  registration, user management, password change or reset, account lockout, multi-factor
  sign-in, or session renewal.

### Key Entities

- **Staff User**: A person who works in BillingCRM. Has a unique email address (compared without
  regard to letter case), a password held only as a one-way hash, and exactly one role.
- **Role**: One of exactly two values, Admin or Agent, deciding which areas and operations the
  user may reach.
- **Session**: The proof of a successful sign-in held by the person's browser. States who the
  person is, their role, and when it expires. It is issued at sign-in and is not stored as a
  business record.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person with valid credentials reaches the application shell in under 3 seconds
  from pressing "Sign in".
- **SC-002**: 100% of sign-in attempts with a wrong password or unknown email end with no
  session and the identical generic message.
- **SC-003**: 100% of attempts by an Agent to open or call the Admin-only area are refused, both
  through the screens and directly against the back end; 100% of the same attempts by an Admin
  succeed.
- **SC-004**: After signing out, or once the session has expired, 0 protected screens or data
  can be reached without signing in again.
- **SC-005**: 100% of back-end operations other than the health address and sign-in refuse
  callers who are not signed in.
- **SC-006**: A search of everything committed to source control finds no signing secret and no
  connection string; the only credentials present are the local-development seed accounts
  already documented in the project guidance.
- **SC-007**: All automated tests in the repository pass on the developer's machine, and the
  developer can sign in as both seeded users and confirm the role behaviour in a browser.

## Assumptions

- 001-foundation is complete and verified: the empty shell, health address, LocalDB connection,
  and the pattern for failing fast on missing settings already exist and are reused.
- The technology choices (signed bearer tokens for sessions, two roles, standard error
  responses, hashed passwords) are fixed by the project constitution and the feature request.
- "Admin routes" have no business content yet, because customers are out of scope. A minimal
  placeholder Admin-only page and one matching Admin-only back-end check exist purely so the rule
  can be built and tested; later specs attach real Admin-only features to the same rule.
- Sign-out works by the browser discarding the session credential. The credential is not revoked
  on the server, so a copy taken before sign-out would remain valid until it expires. Server-side
  revocation is out of scope.
- The session lifetime is 60 minutes by default and is not extended while the person is active;
  when it ends they sign in again.
- No lockout, throttling, password rules, password change, or "forgot password" exist in this
  spec. Staff accounts are created only by local seeding until a later spec adds user
  management.
- The two seed accounts exist only for local development on a developer's own database, as the
  project guidance already states; they must never be created on any shared or production
  database. The seed passwords are not repeated in this specification.
- The signing secret for local development is generated by the developer and kept in local
  configuration, following the same approach as the database connection string in 001.
- After sign-in the person is returned to the page they originally requested, or to the shell's
  home page if they came straight to sign-in.
- Shop, cart, payment gateway, support tickets, and WhatsApp integration remain out of scope for
  the whole product (Constitution Principle I).
