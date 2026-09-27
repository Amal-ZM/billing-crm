# Research: Invoice Status

**Feature**: 007-invoice-status | Decisions taken while planning. No `NEEDS CLARIFICATION` remains: the one open question was
answered on 2026-09-24 (a Sent invoice is read-only, nothing at all can be edited).

## R1. Two action routes, not a status body

- **Decision**: `POST /api/invoices/{id}/send` and `POST /api/invoices/{id}/cancel`, no request body, each answering `200` with the
  invoice. They sit in the existing `/api/invoices` group (any signed-in user); **cancel adds `AuthorizationPolicies.AdminOnly`**
  (the mixed-role pattern products already use). There is no `PUT /status`.
- **Rationale**: the two actions have different permissions (send: any staff; cancel: Admin only). Endpoint-level policy is the
  established way to make an Agent get 403 **before the id is even looked up**, which is what the spec asks ("the role is checked
  first"). With one `PUT /status {status}` the permission would depend on the body, so it would have to be checked in code after the
  body is read and the invoice loaded. Two routes also read as the business verbs, and an unknown status value cannot be sent.
- **Alternatives**: `PUT /api/invoices/{id}/status` with `{ "status": "Sent" }` (permission-by-body, see above); making Send and Cancel
  Admin-only or Agent-allowed the same (not what was asked).
- A new endpoint shape (an action on a resource), so it is listed for the developer's confirmation (Principle V, T001).

## R2. The state machine lives in the Domain

- **Decision**: `InvoiceStatus` becomes `Draft, Sent, Paid, Cancelled`. A static `InvoiceStatusRules` holds the legal changes as a small
  table: `Draft to Sent`, `Draft to Cancelled`, `Sent to Cancelled`; it offers `CanChange(from, to)` and `NextStatuses(from)`. The
  aggregate gets `Send(nowUtc)` and `Cancel(nowUtc)`, each returning `false` (and changing nothing) when the change is illegal. Every
  edit member (`UpdateDetails`, `AddLine`, `RemoveLine`, `SetLineQuantity`) **throws** `InvalidOperationException` when the invoice is
  not a Draft.
- **Rationale**: the rules exist once, are testable without a database, and a future spec (payments: `Sent to Paid`) adds one row to the
  table. The throw is a safety net for a programming error: the service always checks the status first and answers a `409`, so no user
  input can reach it (Constitution III: expected failures are `Result`s; an edit reaching a locked aggregate would be a bug).
- **Alternatives**: the rules in `InvoiceService` only (the aggregate could then be edited from any other caller); a generic state-machine
  library (a new dependency for a table of three rows).

## R3. Overdue is derived, not stored

- **Decision**: only `Draft`, `Sent`, `Paid`, `Cancelled` are stored. `Invoice.IsOverdue(today)` is `Status == Sent && DueDate < today`,
  with `today` the fixed UTC+03:00 date already used for due dates (`InvoiceService.Today`). The API returns the stored `status` **and**
  `isOverdue`; the screens show "Overdue" when `isOverdue` is true and the stored status otherwise. Overdue is still a Sent invoice for
  every rule (`allowedTransitions` is the same).
- **Rationale**: exactly the request's definition. Nothing to schedule, nothing that can be stale, no migration of statuses at midnight,
  and the transition rules keep one status value to reason about. Returning the stored status separately means a client (or a test) can
  never confuse "Overdue" with a status that has its own rules.
- **Alternatives**: a stored `Overdue` status flipped by a background job (a job, a race at midnight, a wrong answer until it runs);
  returning "Overdue" in `status` (a client checking `status == "Sent"` to offer Cancel would miss late invoices).

## R4. Amounts are frozen when an invoice leaves Draft

- **Decision**: when an invoice is sent, or cancelled from Draft, the amounts are written down: `Invoices.FrozenTotalNet`,
  `FrozenNetAfterDiscount`, `FrozenTotalVat`, `FrozenGrandTotal` and, per line, `InvoiceLines.FrozenNet`, `FrozenDiscountShare`,
  `FrozenVat` (all `decimal(18,2)`, nullable). They come from the same `InvoiceMoney` calculation at that moment. `Invoice.Amounts` for a
  non-Draft invoice returns these stored numbers (the discount is already stored and is locked); for a Draft it calculates as in 006.
  A check constraint requires the four invoice columns to be all null for a Draft and all present otherwise.
- **Rationale**: 006 (R2) left this to the spec that issues invoices. Spec FR-007 says the amounts of a Sent invoice never change for any
  reason, including a later change of the calculation rules (a new rounding rule, a VAT rule). Calculating on read cannot promise that;
  stored numbers can. Reading stored numbers is also cheaper. Existing invoices are all Drafts, so nothing needs a backfill.
- **Alternatives**: keep calculating (breaks FR-007 the first time a rule changes); store one JSON snapshot column (not queryable, not
  checkable, a second format to keep); a copy table (a join for every read).
- If a non-Draft row somehow lacks a line's frozen values (the invoice-level check cannot see other rows), reading falls back to
  calculating rather than failing (reading must never fail, as decided for 006).

## R5. `RowVersion` on `Invoices` (optimistic concurrency)

- **Decision**: `Invoices.RowVersion` (`rowversion`, an EF concurrency token, `byte[]` in the Domain). Every change to an invoice or its
  lines already moves `UpdatedAtUtc`, so every save writes the invoice row and is checked against the version. `EfInvoiceStore.SaveChangesAsync`
  already turns `DbUpdateConcurrencyException` into `false`, and the service already answers `false` with the `409 invoice_changed`
  ("changed by someone else, reload and try again"). Send and Cancel use the same path.
- **Rationale**: the spec needs "exactly one of two simultaneous actions wins" (SC-007) and "a Sent invoice never ends up with changed
  lines, even if the edit and the send happened in the same instant" (FR-005, edge case). Without a token, an edit that loaded the invoice
  as a Draft could save its line changes after a send committed, or two sends could both succeed. A `WHERE Status = 'Draft'` guarded
  update would fix the two-sends case only.
- **Consequences to confirm**: (1) two people saving the same Draft in the same instant now get a `409` instead of "last save wins"
  (005's assumption); (2) it closes the 006 review gap (a discount left above the net by two saves of different rows); (3) the loser
  of a simultaneous send or cancel sees "changed by someone else" (a `409`), which the spec allows.
- **Alternatives**: a conditional `UPDATE ... WHERE Id = @id AND Status = @from` for transitions only (leaves the edit-versus-send window);
  a transaction with `UPDLOCK` (serialises reads, more code, easy to get wrong); doing nothing (violates FR-005 in a rare window).

## R6. Response shape

- **Decision**: the invoice response gains `isOverdue` (bool), `sentAtUtc` and `cancelledAtUtc` (ISO time or `null`),
  `allowedTransitions` (the statuses the invoice's **stored status** permits: Draft gives `["Sent","Cancelled"]`, Sent gives
  `["Cancelled"]`, Paid and Cancelled give `[]`) and `isEditable` (true only for a Draft). The list rows gain `isOverdue`. `status` stays
  the stored value. `RowVersion` is never sent.
- **Rationale**: the screens decide what to offer without repeating the transition rules in TypeScript; the only thing left for the client
  is the role (Cancel only for an Admin), which it already knows (`auth.role()`), and the API still enforces both.
- **Alternatives**: the client re-derives the rules from `status` (two places for a rule); role-specific flags in the response (the
  service would need the caller's role, and the response would differ per caller).

## R7. Errors and the order of checks

| Request | Order of checks | Result |
|---------|-----------------|--------|
| `send` | not signed in, invoice exists, status is Draft, due date not in the past, save | `401`, `404`, `409 invoice_status_change_not_allowed`, `400 dueDate`, `409 invoice_changed` |
| `cancel` | not signed in, **role is Admin (policy)**, invoice exists, status is Draft or Sent, save | `401`, `403`, `404`, `409 invoice_status_change_not_allowed`, `409 invoice_changed` |
| `PUT` (edit) | not signed in, invoice exists, **status is Draft**, then the body is judged as in 005/006, save | `401`, `404`, `409 invoice_not_draft`, `400`, `409 invoice_changed` |

- The illegal-change message names the current status and the requested change: "An invoice that is Sent cannot be sent." /
  "An invoice that is Cancelled cannot be cancelled." (the current status shown as **Overdue** for a late Sent invoice).
- The locked-edit message: "Only a Draft invoice can be edited. This invoice is Sent."
- The past-due send is a field error on `dueDate` ("Due date is in the past. Change the due date before sending."), like other refusals of a
  value, because the person fixes it by editing the Draft.
- A locked edit is judged **before** the body, so a locked invoice is always a `409` whatever the body holds.
- An id that is not a GUID is `404`, as for the other operations (an Agent still gets `403` first on `cancel`).

## R8. Angular

- **Decision**: the details page shows the status badge as "Overdue" when `isOverdue`, and the sent and cancelled times when present.
  **Send** is offered when `allowedTransitions` contains `Sent`; **Cancel** when it contains `Cancelled` **and** the role is Admin;
  **Edit** when `isEditable`. Send and Cancel use an **inline two-step confirmation** (a message with "Yes, send" / "Yes, cancel" and
  "Keep it"), because both are permanent; no dialog component. While an action runs its buttons are disabled. A refusal (`409`, `400`
  on `dueDate`, `404`, network) is shown as a message on the page and the invoice is reloaded so the actions match reality. The edit
  form, when the loaded invoice is not editable, shows "Only a Draft can be edited" with a link back, not the form. The list shows the
  status with Overdue.
- **Rationale**: no new component or pattern, no calculation, and the rules stay in the API.
- **Alternatives**: a Material dialog (a new shared piece for one use); no confirmation (a click permanently locks or cancels an invoice).

## R9. Test approach

- **Domain (Application.Tests)**: the whole transition table (4 statuses by 3 requested changes; only the three legal ones succeed);
  `IsOverdue` before, on and after the due date and for every status, at the Saudi day boundary (21:00 UTC is midnight in Riyadh);
  `Send`/`Cancel` set the time and freeze the amounts; the frozen amounts do not move when a line value or the discount setter is
  changed afterwards (by reflection); every edit member throws on a non-Draft invoice.
- **Service**: send (either role's path is the same service), cancel from Draft, Sent and Overdue, illegal changes naming the current
  status, past due date on send, locked edits (add, remove, quantity, discount, customer, due date, notes) all `409` and unchanged,
  `false` from the store (a lost race) is a `409 invoice_changed`, not found.
- **API**: `401` without a token; Agent `403` on cancel including for an unknown id (the policy comes first) and for an illegal status;
  Admin cancel; both roles send; `404` for unknown and non-GUID ids; `409` bodies name the status; `400` on `dueDate`; the response
  additions on create/get/list; a locked `PUT` is `409` whatever the body; no route accepts `DELETE`; the access test lists the two
  new routes with the right policies.
- **Real SQL**: migration smoke (columns, widened check, the two new checks, `RowVersion`); the checks reject a Sent row without a
  time, a Draft row with frozen amounts, a status `Overdue`; frozen amounts round-trip exactly; two contexts sending the same Draft:
  one `true`, one `false`; an edit racing a send: exactly one applies and the invoice never has changed lines and status Sent.
- **Angular**: the badge shows Overdue; Send/Cancel/Edit gating by status and role; the two-step confirmation; refusals shown and the
  invoice reloaded; the locked edit page; the list shows Overdue.

## R10. Migration

- **Decision**: one migration `AddInvoiceStatuses`: adds `SentAtUtc`, `CancelledAtUtc`, the four frozen columns and `RowVersion` to
  `Invoices` and the three frozen columns to `InvoiceLines`; drops and re-adds `CK_Invoices_Status` with the four values; adds
  `CK_Invoices_StatusTimes` and `CK_Invoices_FrozenAmounts`. Existing rows are all `Draft`, so every new column is `NULL`
  (a `rowversion` column fills itself). Applied with `dotnet ef database update`; never edited afterwards.
- The existing test that asserts "the status check rejects anything but Draft" is updated to the new list (`Overdue` and any
  other text are still rejected).

## R11. Known gaps

- Payments and Paid, credit notes, reopening or un-sending, cancellation reason and history, delivery (PDF/email), status filters and
  search on the list, reminders: later specs.
- The 012-security items (decimal-literal converter, `StaffOnly` policy, Development header leak) still apply.

## Confirmations (task T001, Constitution V) — 2026-09-24

The developer confirmed every new pattern in plan.md before any code was generated (all four questions answered "Confirm"):

1. **Routes and rules (R1, R2)**: `POST /api/invoices/{id}/send` and `/cancel` with no body, cancel behind `AdminOnly`; the legal changes in one
   Domain table (`InvoiceStatusRules`); an aggregate that throws when a non-Draft invoice is edited.
2. **Overdue, frozen amounts, response shape (R3, R4, R6)**: Overdue derived and returned as `isOverdue`; amounts frozen into new columns when an
   invoice leaves Draft; `isOverdue`, `sentAtUtc`, `cancelledAtUtc`, `allowedTransitions`, `isEditable` added.
3. **Row version (R5)**: `Invoices.RowVersion` as the concurrency token. This **replaces 005's "last save wins" for Drafts** (two changes in the same
   instant: one winner, one 409) and closes the 006 review gap.
4. **Errors and UI (R7, R8)**: 409 for an illegal change or locked edit naming the current status, 400 on `dueDate` for a past-due send; an inline
   two-step confirmation, role-gated Cancel and a locked-edit message in Angular.

## Review disposition (2026-09-26)

Four read-only reviews ran after implementation (`api-reviewer`, `security-reviewer`, `angular-reviewer`, `spec-auditor`). None found a
Critical or High defect. Every finding is fixed, or recorded here with the reason.

### api-reviewer

| Finding | Disposition |
|---------|-------------|
| A `PUT` on a locked invoice with an unreadable body is the framework's 400, not the 409 | **Accepted, worded**: the 409 is for any *readable* body (the locked check runs before the body is judged, not before it is parsed). Test added for the unreadable case |
| Frozen amounts could disagree with `Discount` for a legacy discount above the net | **Fixed**: `Calculate()` clamps the discount to `[0, totalNet]` and `FreezeAmounts()` freezes the clamped discount first |
| `RowVersion` only moves if the invoice row is written (a lines-only edit did not) | **Fixed**: `EfInvoiceStore.SaveChangesAsync` marks the invoice's `UpdatedAtUtc` modified when its lines changed. Real-SQL tests: line-only edit loses to a committed send; two edits of one Draft have one winner |
| `CancelAsync` has no role check of its own | **Accepted**: the `AdminOnly` policy on the route is the single gate (pinned by `InvoiceAccessTests`), as for products |
| The `bool` returned by `Send`/`Cancel` was ignored by the service | **Fixed**: a `false` becomes the same 409 |
| Overclaiming or missing tests | **Fixed**: renamed to what they prove; added multi-line freeze with remainder, partly-frozen fallback, Overdue raw row, SQL-level "frozen read even if line values change" |
| `Down` re-adds a Draft-only status check | **Recorded**: the migration is applied, so it is not edited; a rollback with Sent rows present would fail, which is the safe direction |

### security-reviewer

| Finding | Disposition |
|---------|-------------|
| Low: the JWT's role is not re-checked against the database during its lifetime | **Deferred to spec 012** (already in the deferred list: stale roles inside a token's lifetime) |
| Low: only `DbUpdateConcurrencyException` is handled; a deadlock victim is a 500 | **Deferred to spec 012**; recorded in the deferred-items memory |
| Low: the lock is enforced in the application, not by a database trigger | **Accepted**: every write path goes through the aggregate; the `Frozen*` and time checks stop inconsistent rows |
| Suggested: a test that `status` / `rowVersion` in a request body are ignored | **Fixed**: test added |

### angular-reviewer

| Finding | Disposition |
|---------|-------------|
| Medium: the confirmation was not announced and focus was not managed | **Fixed**: alertdialog with focus moved to it, Escape keeps the invoice, focus returns to the button, result announced |
| Medium: no way out of a 409 on the edit form | **Fixed**: "Discard my changes and reload the invoice" after a 409 (a Sent invoice then shows the locked page); tests both ways, and none for a 400 |
| Medium: no `cancel()` service test; weak assertions | **Fixed**: `cancel()` POST/encoding tests; the `?? true` check now asserts the Send button is disabled; the badge test renamed to what it checks |
| Low: a 403 left Cancel offered | **Fixed**: Cancel is hidden after a 403, and the test reloads the same Sent invoice |
| Low: a failed reload left stale actions; Edit/Back during a request | **Fixed**: stale actions are hidden; the request is not cancelled by leaving |
| Low: the locked page had no heading or alert role | **Fixed**: heading "Invoice cannot be edited", `role="alert"`; an Overdue case added |
| Low: fixtures too consistent, list error-state and sent+cancelled-both-shown tests | **Recorded, not done**: no behaviour is unproven by them; revisit if the fixtures are reshaped |

### spec-auditor

No Fail on any Given/When/Then; all are Unknown until the developer runs the quickstart (T039).

| Finding | Disposition |
|---------|-------------|
| Medium: `.claude/skills/invoice-money/SKILL.md` still says amounts are "never stored" | **For the developer**: it is an instruction file; needs "calculated while Draft, frozen when leaving Draft" |
| Medium: three tests named for freezing would pass without it | **Fixed**: renamed; the freeze is proven by the Domain, round-trip and SQL "change the line values" tests |
| Medium: no `cancel()` service test | **Fixed** (above) |
| Medium: research R5's two consequences untested (edit-vs-edit) | **Fixed**: real-SQL edit-vs-edit test; the misleading "two concurrent edits" names now say "older version" |
| Low: quickstart SQL/curl/wording (database name, `Number` digits, time zone, `curl.exe`, Draft/Cancelled past-due) | **Fixed** in `quickstart.md` |
| Low: stale code comments in `InvoiceService`/`InvoiceModels` | **Fixed** |
| Low: `CLAUDE.md` 005 invoices bullet says "lost a race with another edit of the same rows" | **For the developer**: instruction file; since 007 any change to the same invoice is a 409 (the 007 bullet already says so) |
| Low: no manual two-window check for FR-011 | **Recorded**: proven by the real-SQL race tests |
| Low: tests whose names claim more than they prove; a few routes/layers without a test (Paid `PUT` over HTTP, ProblemDetails body on a cancel 403, boundary for the list's `isOverdue`, `DELETE` 405) | **Recorded, not done**: each is covered at another layer |

### Verification (T041)

`dotnet test`: Application 593, API 431, Infrastructure 199, all pass (each project run on its own). `ng test --watch=false`: 541 pass. `ng build`: clean.
