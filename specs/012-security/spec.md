# Feature Specification: Security Hardening

**Feature Branch**: `012-security`

**Created**: 2026-09-27

**Status**: Draft

**Depends on**: 001-foundation through 011-dashboard (all verified running): every domain, endpoint and screen this spec's rules and checks
apply across

**Input**: User description: "Feature 012-security. Depends on all previous. JWT on all writes. Agent cannot create products, cancel Sent,
or delete paid invoices. 400 validation, 404 missing, 409 unique/illegal status. Anonymous /api/invoices → 401. Notes escaped in UI. Short
SECURITY.md checklist. Given no token, When I POST /api/invoices, Then 401."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every write needs a real, valid session (Priority: P1)

Anyone who is not signed in, or whose session has expired or been tampered with, is refused before a single row changes — for every
mutating request across customers, products, invoices, invoice actions and payments, not just the ones a person happens to test.

**Why this priority**: This is the foundation everything else in the system depends on; if it has a gap, nothing else in this spec matters.

**Independent Test**: Send every documented write request (create, edit, activate/deactivate, send, cancel, record payment) with no token,
with an expired token and with a tampered token; each is refused with 401 and changes nothing.

**Acceptance Scenarios**:

1. **Given** no Authorization header, **When** I `POST /api/invoices`, **Then** the response is 401 and no invoice is created.
2. **Given** no Authorization header, **When** I send any other write request (`POST`/`PUT` under `/api/customers`, `/api/products`,
   `/api/invoices`, including the send/cancel/payment actions), **Then** each is 401 and nothing changes.
3. **Given** a token that has expired, or one whose signature has been altered, **When** I send a write request, **Then** it is 401, the
   same as having no token at all.
4. **Given** a signed-in Agent, **When** they try to create a product, edit a product, activate or deactivate a product, or cancel any
   invoice, **Then** each is refused with 403 before anything is looked up or changed, and an Admin doing the same succeeds.
5. **Given** any invoice, in any status, **When** anyone — Agent or Admin — looks for a way to delete it, **Then** none exists: there is no
   delete operation for an invoice of any status, for either role (this has been true since invoices were introduced and does not change).

---

### User Story 2 - Expected problems always look the same (Priority: P1)

A person (or a future client of the API) can rely on one consistent shape for every kind of expected problem: a validation mistake is
always 400 with the field named, a reference to something that does not exist is always 404, and a conflict (a duplicate value, an illegal
status change, someone else's change winning a race) is always 409 — never a raw server error for something the system already expects.

**Why this priority**: Consistency here is what makes every other screen and every future integration predictable; it is as foundational
as User Story 1.

**Independent Test**: Trigger one example of each kind (a bad field, an unknown id, a duplicate email/SKU, an illegal status change) against
several different resources and confirm each answers with the same status code and shape.

**Acceptance Scenarios**:

1. **Given** a request with an invalid field (for example a negative quantity, a malformed date, an unrecognized status filter),
   **When** it is sent, **Then** the answer is 400 naming the field, and nothing is changed.
2. **Given** a request that references an id that does not exist, **When** it is sent, **Then** the answer is 404, and nothing is changed.
3. **Given** a request that would duplicate a unique value (an email, a SKU) or make an illegal status change, **When** it is sent,
   **Then** the answer is 409, and nothing is changed.
4. **Given** two people changing the same invoice at the same moment, **When** both save, **Then** one succeeds and the other gets 409,
   never an unhandled server error, even under a database-level conflict (for example a deadlock).

---

### User Story 3 - Typed text can never become a script (Priority: P2)

Whatever a staff member types into a note, a name, or any other free-text field — including deliberately hostile input such as HTML tags
or script content — always appears on screen exactly as typed, as plain text, and never runs or alters the page.

**Why this priority**: The system already stores and shows a lot of staff-entered free text (customer and product names, invoice and
payment notes, history entries); this closes the loop on all of it at once rather than checking screen by screen.

**Independent Test**: Enter text containing HTML tags, a script tag and bidirectional/control characters into every free-text field the
system has, and confirm it is stored and always shown back as literal text, with hostile control characters refused outright.

**Acceptance Scenarios**:

1. **Given** a name or a note containing `<script>` or other HTML, **When** it is shown anywhere in the application, **Then** it appears
   as literal text, never as an element and never executed.
2. **Given** text containing a right-to-left override or other bidirectional/control character, **When** it is submitted, **Then** it is
   refused with a 400 naming the field, not silently stored.

---

### User Story 4 - One document answers "is this protected?" (Priority: P2)

A developer, an auditor, or a new team member can read one short document at the root of the repository and find, without reading source
code, what the application protects against, exactly how, and which known gaps were consciously accepted and why.

**Why this priority**: Twelve specs' worth of security decisions are currently scattered across each spec's own `research.md`; this is the
one deliverable that makes them find able without archaeology.

**Independent Test**: Hand `SECURITY.md` to someone unfamiliar with the codebase and confirm they can answer "does this check X?" for
authentication, authorization, input validation, output encoding, secrets handling and the known gaps, using only that document.

**Acceptance Scenarios**:

1. **Given** `SECURITY.md` at the repository root, **When** it is opened, **Then** it names, in a short checklist, the authentication and
   session model, the authorization model (including a table of which role each write needs), input validation, output encoding, secrets
   handling, and a list of accepted gaps with a one-line reason for each.
2. **Given** any hardening item this spec closes, **When** `SECURITY.md` is read, **Then** it is listed as done, with a pointer to where it
   is enforced or tested; **given** any item consciously left open, **Then** it is listed as an accepted gap, never silently missing.

---

### User Story 5 - Nothing deferred is silently forgotten (Priority: P3)

Every hardening item the team explicitly chose to defer while building specs 004 through 010 is revisited now that this is the last spec:
each one is either closed, or it is still open on purpose, visibly, with a reason — never quietly dropped because the numbered specs ran out.

**Why this priority**: Least urgent to a new user of the running system, but the whole reason this spec exists in the roadmap.

**Independent Test**: Walk the list of previously deferred items (recorded across specs 004, 007, 008, 009 and 010) one by one and confirm
each has a disposition (closed, with a test; or still open, with a reason) recorded in this spec and in `SECURITY.md`.

**Acceptance Scenarios**:

1. **Given** the full list of previously deferred items, **When** this spec is done, **Then** every one of them has an explicit
   disposition — none is absent from `SECURITY.md` without explanation.

---

### Edge Cases

- A well-formed token that has simply expired is treated exactly like no token at all: 401, not a confusing or different error.
- A request body larger than the configured limit is refused cleanly, not a crash or a hang.
- A burst of login attempts against one account or from one source is throttled after a small number of failures, rather than accepted
  without limit.
- A SQL Server deadlock victim on a concurrent invoice save is reported the same way as any other lost race (409), never a raw 500.
- A note or name containing a large amount of hostile-looking but plain text (very long, mixed Arabic/English, emoji) is still accepted
  and still shown as plain text; only actual control/bidi characters are refused, not merely unusual text.
- An Admin's own role changes (for example is demoted) while they still hold a valid, unexpired token: the token keeps working with its
  original role for the rest of its lifetime (documented as an accepted, time-boxed gap, not fixed by this spec).

## Requirements *(mandatory)*

### Functional Requirements

**Authentication and authorization (User Story 1)**

- **FR-001**: Every request that changes data (every `POST`/`PUT` under `/api/customers`, `/api/products`, `/api/invoices`, including the
  send, cancel and payment actions) MUST require a valid, signed-in session; a missing, expired or tampered token MUST be refused with 401
  before any data is read or changed.
- **FR-002**: An Agent MUST be refused with 403, before the request body is read, for: creating a product, editing a product, activating
  or deactivating a product, and cancelling an invoice of any status. An Admin performing the same actions MUST succeed (subject to the
  usual validation and conflict rules).
- **FR-003**: No role MUST be able to delete a customer, a product, or an invoice of any status; no such operation MUST exist in the API
  for any of the three.

**Consistent expected-error shape (User Story 2)**

- **FR-004**: A request that fails validation MUST be answered 400, naming the specific field or line at fault, and MUST change nothing.
- **FR-005**: A request that references an id that does not exist MUST be answered 404, and MUST change nothing.
- **FR-006**: A request that would duplicate a value required to be unique, or that asks for a status change the current status does not
  allow, MUST be answered 409, and MUST change nothing.
- **FR-007**: A request that loses a race with a concurrent change to the same record (including a database-level conflict such as a
  deadlock) MUST be answered 409, never an unhandled server error.

**Output safety (User Story 3)**

- **FR-008**: Free text entered anywhere in the application (customer and product names and addresses, invoice notes, payment notes,
  history entries) MUST be rendered everywhere it is shown as literal text; it MUST NOT be interpreted as markup or executed as script.
- **FR-009**: Free text containing a bidirectional override or other non-printable control character MUST be refused with a 400 naming the
  field at the time it is submitted.

**Hardening this spec closes**

- **FR-010**: A JSON number bound to a monetary or quantity field MUST refuse exponent notation and more than 28 significant digits,
  rather than silently rounding or truncating it, for every such field in the application (product price and VAT, invoice quantity and
  discount, payment amount).
- **FR-011**: Every API response MUST carry headers that refuse to let the page be framed by another site, and every authenticated API
  response MUST tell the browser not to cache it.
- **FR-012**: A request body above a fixed, generous size limit MUST be refused cleanly rather than accepted or left to fail unpredictably.
- **FR-013**: Repeated sign-in attempts against `POST /api/auth/login` MUST be throttled after a small number of failures in a short
  window, per source, without needing new external infrastructure.
- **FR-014**: The `Payments` table MUST refuse any `UPDATE` or `DELETE` at the database level, the same way `InvoiceHistoryEntries`
  already does, since payments have been described as permanent since spec 008 but were never enforced this strictly.

**Documentation (User Story 4) and closing the backlog (User Story 5)**

- **FR-015**: A `SECURITY.md` file MUST exist at the repository root, covering: the authentication and session model; a table of every
  write operation and the role it needs; input validation; output encoding; secrets handling; and a list of accepted, open gaps, each with
  a one-line reason.
- **FR-016**: Every item previously deferred to this spec (recorded across specs 004, 007, 008, 009 and 010) MUST appear in `SECURITY.md`
  with an explicit disposition: closed (with a pointer to what enforces or tests it) or still open (with a one-line reason); none MUST be
  silently absent.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every mutating request without a valid session is refused 100% of the time, across every resource, with no exception found.
- **SC-002**: An Agent attempting any of the three restricted actions is refused 100% of the time, before any data changes.
- **SC-003**: Every expected-problem scenario tested (a bad field, an unknown id, a duplicate value, an illegal status change, a lost race)
  answers with the documented status code 100% of the time, never a raw, undocumented server error.
- **SC-004**: Hostile-looking free text (HTML, script tags) is shown as literal text 100% of the time it is tested, across every free-text
  field in the application.
- **SC-005**: A person unfamiliar with the codebase can answer, using only `SECURITY.md`, whether the application protects against each of
  the OWASP-style categories this spec covers, without opening any source file.
- **SC-006**: Every previously deferred item has a recorded disposition; a count of "closed" vs. "still open, with a reason" adds up to
  the full list, with none missing.

## Assumptions

- **Scope of the deferred backlog**: this spec closes the items that are bounded, in-repo changes needing no new external infrastructure
  or unresolved design decision (FR-010 to FR-014, above). Three items remain consciously open, recorded in `SECURITY.md` as accepted gaps
  rather than closed here, because each needs something this spec cannot decide alone: HTTPS/HSTS enforcement (needs a real deployment
  target; this application currently only runs on `http://localhost`); a payment idempotency-key guard for a retried `POST` (needs a new
  field and a design decision about what "the same payment" means); and a redaction or erasure path for free text once it is inside the
  permanent invoice history (an explicitly unresolved question from spec 010, not decided here). A stale role inside a still-valid token
  is also left open, mitigated only by the existing token lifetime (default 60 minutes), since full revocation needs session
  infrastructure this project does not have.
- **"Delete paid invoices"**: read as reaffirming the existing, unchanged invariant that no invoice, of any status, can be deleted by any
  role (true since invoices were introduced) — not a request for a new, role-gated delete feature, which would contradict that invariant.
- **Bidi/control-character policy**: refused outright (400) rather than accepted and visually isolated, since refusing fits the project's
  existing "expected problems are 400s" pattern and needs no per-screen UI change.
- **Rate limiting**: implemented with the runtime's own built-in in-memory limiter, adequate for this single-instance application; no new
  external dependency.
- **`SECURITY.md` audience**: written for a developer or auditor already familiar with staff software, not a security specialist; it is a
  checklist and a map to the detail, not a full threat model.
- **Three items from the 004 deferred-items memory, found missing from this spec's own scoping while cross-checking `SECURITY.md` against
  it (T027)**, none left silently absent: a database check-constraint upper bound on `Products.UnitPrice` (the application layer already
  enforced `ProductValidator.MaxUnitPrice`, but the database itself accepted any non-negative value; closed the same bounded, in-repo way
  as FR-010 to FR-014, migration `AddProductUnitPriceUpperBound`); a dedicated `StaffOnly` (`RequireRole("Admin","Agent")`) policy for
  `/api/customers` and `/api/products` reads (left as a documented no-op rather than added: the fallback policy already requires an
  authenticated user for every endpoint, and roles are exactly `Admin`/`Agent` with no third role possible without a constitution
  amendment, so a dedicated policy could not refuse anything the fallback policy does not already refuse today; revisit if a third role is
  ever introduced); and `Development`'s exception page still echoing request headers (including the bearer token) for a genuinely
  unexpected exception, not just a malformed body (`ThrowOnBadRequest = false` from spec 002 only covers the latter) — left as a sixth
  Known Gap rather than closed unilaterally, since removing `Development`'s richer diagnostics everywhere is a real trade-off this spec
  should not decide alone.
