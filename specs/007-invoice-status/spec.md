# Feature Specification: Invoice Status

**Feature Branch**: `007-invoice-status`

**Created**: 2026-09-24

**Status**: Done

**Depends on**: 006-invoice-totals (verified running): Draft invoices with saved lines, a discount and calculated amounts; 005-invoice-create (the Draft and its edit rules); 002-staff-auth (the Admin and Agent roles)

**Input**: User description: "Feature 007-invoice-status. Depends on 006. Draft, Sent, Paid, Overdue, Cancelled. Draft→Sent locks lines. Cancel Draft or Sent by Admin only. Illegal transition 409. Overdue = Sent and due date < today Asia/Riyadh. Given Draft, When I send, Then Sent and line edits blocked."

## Clarifications

### Session 2026-09-24

- Q: What can still be edited on an invoice after it is Sent? → A: Nothing: a Sent invoice is read-only (lines, discount, customer,
  due date and notes are all locked); cancelling is the way to correct it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Send a Draft invoice (Priority: P1)

A staff member finishes a Draft and **sends** it. The invoice becomes **Sent**: it is now an invoice the customer has been billed
for, so its lines can no longer be changed. From then on the invoice shows the status Sent, the time it was sent, and the same
amounts it had when it was sent.

**Why this priority**: Until an invoice can leave Draft it is only a working copy. Sending is the moment the numbers become a bill,
and locking the lines is what makes those numbers trustworthy. Everything else in this spec (cancelling, Overdue) builds on it.

**Independent Test**: Create a Draft for ABC with `CNS-001` quantity 2 (grand total 230.00), send it, and confirm the status is
Sent, the time sent is shown, the grand total is still 230.00, and trying to edit its lines is refused.

**Acceptance Scenarios**:

1. **Given** a Draft invoice, **When** I send it, **Then** its status is **Sent**, the time it was sent is shown, and its number and
   amounts are unchanged.
2. **Given** a Sent invoice, **When** I try to change any of its lines (quantity, add a line, remove a line), **Then** the change is
   refused with a clear message that the invoice is no longer a Draft, and the invoice is exactly as it was.
3. **Given** a Sent invoice, **When** I open it, **Then** I see no **Edit** action and no **Send** action, and if I open
   its edit address anyway I see a message that only Drafts can be edited (not a form that fails on save).
4. **Given** a Sent invoice, **When** a product used by its lines is repriced or deactivated, **Then** the invoice's amounts still do not
   change (as in 006), and they are the same amounts the invoice showed when it was sent.
5. **Given** an invoice that is already Sent, Paid or Cancelled, **When** I try to send it, **Then** the request is refused as an
   illegal change of status (a conflict) with a message naming the current status, and nothing changes.
6. **Given** a Draft whose due date is in the past (allowed while it is a Draft), **When** I try to send it, **Then** I am told the due
   date is in the past and must be changed first, and the invoice stays a Draft.
7. **Given** either an Admin or an Agent is signed in, **When** they send a Draft, **Then** it is sent (sending is day-to-day staff work).
8. **Given** an invoice that does not exist, **When** I try to send it, **Then** I see a clear "invoice not found" outcome.
9. **Given** I am signed out or my session has expired, **When** I try to send an invoice, **Then** I am sent to sign in and nothing changes.

---

### User Story 2 - Cancel a Draft or Sent invoice (Priority: P2)

An **Admin** can cancel an invoice that is a Draft or Sent (including one that is Overdue). A cancelled invoice stays on record with its
number, lines and amounts, marked **Cancelled**, and can never be changed, sent or reinstated. An Agent cannot cancel.

**Why this priority**: Mistakes and withdrawn bills are normal, and because invoices are never deleted, cancelling is the only way to
take one out of use. It matters right after sending, but a business can bill without it.

**Independent Test**: As an Admin, cancel a Draft and cancel a Sent invoice and confirm both show Cancelled and stay listed; as an
Agent, try to cancel and confirm it is refused and nothing changes; try to cancel an already Cancelled invoice and confirm the conflict.

**Acceptance Scenarios**:

1. **Given** a Draft invoice, **When** an Admin cancels it, **Then** its status is **Cancelled**, the time it was cancelled is shown, and
   its number, lines and amounts are kept.
2. **Given** a Sent invoice, **When** an Admin cancels it, **Then** its status is **Cancelled** and its amounts are unchanged.
3. **Given** a Sent invoice whose due date has passed (shown as Overdue), **When** an Admin cancels it, **Then** it is cancelled.
4. **Given** an Agent is signed in, **When** they try to cancel any invoice, **Then** the request is refused as not allowed for their
   role, and nothing changes. The **Cancel** action is not offered to an Agent.
5. **Given** an invoice that is Cancelled or Paid, **When** an Admin tries to cancel it, **Then** the request is refused as an illegal
   change of status (a conflict) with a message naming the current status, and nothing changes.
6. **Given** a Cancelled invoice, **When** anyone tries to edit it or send it, **Then** the request is refused as an illegal change (a
   conflict) and nothing changes; a Cancelled invoice never returns to Draft or Sent.
7. **Given** a Cancelled invoice, **When** I open the invoice list and the invoice, **Then** it is still there, shown as Cancelled, with
   its number and amounts.
8. **Given** an invoice that does not exist, **When** an Admin tries to cancel it, **Then** they see a clear "invoice not found" outcome.
9. **Given** I am signed out or my session has expired, **When** I try to cancel an invoice, **Then** I am sent to sign in and nothing changes.

---

### User Story 3 - See which invoices are Overdue (Priority: P3)

A Sent invoice whose due date is before today (the calendar date in Saudi Arabia) is shown as **Overdue** wherever the status is shown,
without anyone having to change it. When the due date is today or later it is shown as Sent. Overdue is a way of seeing a Sent invoice
that is late, not a separate step anyone performs.

**Why this priority**: It tells staff which bills need chasing, and it needs no new action, but it only matters once invoices can be Sent.

**Independent Test**: Send an invoice due tomorrow and confirm it shows Sent; when the Saudi date passes its due date (in tests: a
Sent invoice whose due date is already past) confirm it shows Overdue on the details and in the list; confirm a Draft or Cancelled invoice
with a past due date never shows Overdue.

**Acceptance Scenarios**:

1. **Given** a Sent invoice with a due date of today, **When** I open it, **Then** it shows **Sent** (not yet late).
2. **Given** a Sent invoice with a due date before today, **When** I open it or see it in the list, **Then** it shows **Overdue**.
3. **Given** a Sent invoice showing Sent, **When** the date in Saudi Arabia passes its due date, **Then** it shows Overdue from the start of
   the next Saudi day, with nobody having edited or saved it.
4. **Given** a Draft, a Cancelled or a Paid invoice with a due date before today, **When** I open it, **Then** it does not show Overdue.
5. **Given** an Overdue invoice, **When** I look at what I can do with it, **Then** it is still a Sent invoice: its lines are locked, an Admin can
   cancel it, and it cannot be sent again.
6. **Given** the list is loading, fails to load, or is empty, **When** I look at the screen, **Then** I see a loading, error-with-retry or
   empty state (as before), and each row shows its status including Overdue.

---

### Edge Cases

- Two people act on the same invoice at once (for example one sends it while another edits its lines, or two Admins cancel it): the
  second action is refused with the conflict result, never applied twice and never half-applied. A Sent invoice never ends up with
  changed lines, even when the edit and the send happened in the same instant.
- A person has a Draft open in the edit form while someone else sends it: saving that form is refused (the invoice is no longer a
  Draft) with a clear message, and nothing is lost from the invoice.
- "Today" is the calendar date in Saudi Arabia (UTC+03:00), the same date used for due dates: an invoice due today is not Overdue, and an
  invoice due yesterday is Overdue for the whole of today.
- A due date cannot be changed on a Sent invoice (it is read-only), so nothing else in this spec moves it out
  of Overdue except cancelling it.
- A Sent invoice for a customer that later becomes inactive, or with a product that becomes inactive: unaffected; the invoice stays Sent
  with the same lines and amounts.
- An invoice is never deleted; Cancelled invoices keep their numbers, so the number sequence has no gaps caused by cancelling.
- The status of a Paid invoice: nothing in this spec makes an invoice Paid (see Assumptions), but if an invoice is Paid it is final: it
  cannot be sent, cancelled or edited.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An invoice MUST have exactly one of these statuses stored: **Draft**, **Sent**, **Paid** or **Cancelled**. A new invoice is
  Draft (as in 005). **Overdue** is not stored: it is how a Sent invoice is shown when its due date is before today (see FR-008).
- **FR-002**: Any signed-in staff member (Admin or Agent) MUST be able to **send** a Draft invoice. Sending changes its status from Draft
  to Sent, records the time it was sent, and changes nothing else about the invoice (number, customer, due date, notes, lines, discount,
  amounts).
- **FR-003**: Only an **Admin** MUST be able to **cancel** an invoice, and only while it is a Draft or Sent (including Overdue). Cancelling
  changes its status to Cancelled and records the time it was cancelled. An Agent who tries MUST be refused as not allowed for their
  role, before anything is examined or changed, and MUST NOT be offered the action.
- **FR-004**: The legal changes of status are exactly: **Draft to Sent** and **Draft to Cancelled** and **Sent to Cancelled**. Every
  other request to change status (for example Sent to Sent, Sent to Draft, Cancelled to anything, Paid to anything, Draft to Paid) MUST
  be refused as an **illegal transition** with the HTTP conflict result (409), a message that names the current status and the
  requested change, and MUST change nothing.
- **FR-005**: Once an invoice is no longer a Draft its **lines are locked**: adding, removing or changing the quantity of a line MUST be
  refused with the conflict result and a message that only a Draft can be edited, and the invoice MUST be exactly as it was. Sending
  MUST NOT change the lines' saved product copies or amounts.
- **FR-006**: A Sent invoice is **read-only**: after sending, none of its parts can be edited (its lines, discount, customer, due date
  and notes). Every request to edit a Sent, Paid or Cancelled invoice MUST be refused with the conflict result and a message that only
  a Draft can be edited. Cancelling is the way to correct a Sent invoice. (Cancelling is a change of status, not an edit.)
- **FR-007**: The amounts of a Sent invoice MUST be exactly the amounts it had when it was sent, and MUST NOT change afterwards for any
  reason (a product's price, VAT or state changing, or any later change of the calculation rules). The same holds for Paid and Cancelled
  invoices.
- **FR-008**: An invoice MUST be shown as **Overdue** when it is Sent and its due date is before today's date in Saudi Arabia
  (UTC+03:00). It MUST NOT be shown as Overdue on the due date itself, and MUST NOT be shown as Overdue when it is Draft, Paid or
  Cancelled. The change to Overdue MUST need no action by anyone. An Overdue invoice is still a Sent invoice for every rule in this
  spec (its lines are locked, an Admin can cancel it, it cannot be sent again).
- **FR-009**: A Draft whose due date is before today MUST NOT be sent: the request is refused with a message naming the due date, and the
  invoice stays a Draft (the person changes the due date first, as a Draft allows).
- **FR-010**: Any request about an invoice that does not exist MUST produce the "not found" outcome (as in 005). A request without a valid
  sign-in MUST be refused (Constitution Principle III).
- **FR-011**: A refused request of any kind (not allowed, not found, illegal transition, locked, past due date) MUST leave the invoice
  exactly as it was and MUST be a structured error response, never an unhandled failure. Two people acting on the same invoice at once
  MUST end with exactly one action applied and the other refused with the conflict result (either as an illegal transition, or, when
  both acted in the same instant, as "changed by someone else, reload and try again").
- **FR-012**: The invoice details and the invoice list MUST show the status (Draft, Sent, Overdue, Paid or Cancelled), the details MUST show
  when it was sent and when it was cancelled if it was, and the **Send** and **Cancel** actions MUST be offered only when they are legal
  and allowed for the signed-in role (Send for a Draft; Cancel for an Admin on a Draft or Sent invoice). The **Edit** action MUST be
  offered only for a Draft. The screens MUST disable an action while it is in progress so nothing is done twice, and MUST show a clear
  message for a refusal.
- **FR-013**: Opening the edit address of an invoice that is not a Draft MUST show a clear message that only Drafts can be edited, not a form.
- **FR-014**: The repository MUST include automated tests covering: send (status, time, nothing else changed, either role); every
  legal and every illegal transition (each illegal one a conflict naming the current status); cancel by an Admin from Draft, Sent and
  Overdue; cancel refused for an Agent (before the invoice is examined); locked lines on Sent, Paid and Cancelled (add, remove, quantity);
  that nothing at all (lines, discount, customer, due date, notes) can be edited after sending (FR-006); Overdue derived correctly (before, on and after the due date, at the Saudi day
  boundary, and never for Draft, Paid or Cancelled); amounts unchanged after sending and after a product change; a past due date
  blocking send; not found; refusal of signed-out requests; two actions at once; and the screens' actions offered only when legal and
  allowed.
- **FR-015**: This feature MUST NOT include payments or any way to make an invoice Paid, credit notes, reopening or un-sending an
  invoice, a cancellation reason or history of who did what, invoice PDFs or email (sending here changes status, it does not deliver
  anything), status filters or search on the list, reminders, or bulk actions, and no shop, cart, payment gateway, support tickets or
  WhatsApp functionality (Constitution Principle I).

### Key Entities

- **Invoice** (from 005 and 006): gains a stored **status** (Draft, Sent, Paid, Cancelled), the **time it was sent** (once sent) and the
  **time it was cancelled** (once cancelled). Its **shown status** is Overdue when it is Sent and past its due date.
- **Status change (transition)**: a request to move an invoice between statuses. Only the three legal changes in FR-004 succeed; the
  rest are conflicts. Not stored as a history.
- **Invoice line, customer, product**: unchanged by this feature; a Sent invoice's lines are locked.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A staff member can send a finished Draft in one action and see it as Sent with the time it was sent in under 10 seconds.
- **SC-002**: 100% of attempts to change the lines of an invoice that is Sent, Paid or Cancelled are refused and change nothing.
- **SC-003**: 100% of illegal status changes are refused as conflicts that name the current status, and 100% of the three legal changes
  succeed for the roles allowed to make them.
- **SC-004**: 100% of attempts by an Agent to cancel are refused, and no invoice is ever cancelled by anyone but an Admin.
- **SC-005**: 100% of Sent invoices with a due date before today (Saudi Arabia) are shown as Overdue, and 0% of invoices due today or later,
  or that are not Sent, are.
- **SC-006**: 100% of Sent invoices show exactly the amounts they had when they were sent, including after their products change.
- **SC-007**: When two people act on the same invoice at the same moment, exactly one action succeeds every time.
- **SC-008**: 100% of status operations attempted without a valid sign-in are refused.
- **SC-009**: All automated tests in the repository pass on the developer's machine, and the developer can, in a browser, send a Draft, see
  its lines locked, cancel a Sent invoice as an Admin, and see an invoice with a past due date shown as Overdue.

## Assumptions

- 002 to 006 are complete and verified. Invoices have the single status Draft today and a check that allows only Draft; this spec
  widens the allowed statuses.
- **Paid** is part of the status vocabulary and of the transition rules (it is final, and cannot be sent, cancelled or edited), but
  **nothing in this spec makes an invoice Paid**: paying an invoice needs payments, which come with a later spec. "Draft to Paid" and
  "Sent to Paid" are therefore illegal here. Tests set a Paid invoice up directly.
- **Overdue is derived, not stored**, exactly as the request defines it ("Sent and due date before today"), so it needs no scheduled job
  and can never be wrong or out of date. It is shown wherever the status is shown.
- "Today" is the calendar date in Saudi Arabia (Asia/Riyadh, UTC+03:00 all year), the same fixed-offset date already used for due dates
  in 005.
- **Who may do what:** sending is day-to-day staff work, so Admin and Agent may both send. Cancelling is Admin only, as requested. A role
  that is not allowed gets the "not allowed" result (403); a request that is allowed but illegal for the invoice's status gets the
  conflict result (409); the role is checked first.
- Editing a Sent (or Paid or Cancelled) invoice's lines is a **conflict (409)** with a message that only Drafts can be edited, because the
  invoice's status, not the person, is what forbids it.
- Sending does not re-check whether the customer or the products are still active (they were checked when the lines were added), and a
  Sent invoice is unaffected by later changes to them.
- Sending records only the **time** it happened, and cancelling only the time it happened: there is no history of who did it, no
  reason for cancelling, and nothing is deleted.
- "Send" changes the invoice's status; it does not email, print or deliver anything to the customer (out of scope for the whole
  product until a later spec says otherwise).
- The invoice number sequence is unaffected: cancelled invoices keep their numbers.
- The invoice list shows the status (including Overdue) but has no status filter or search; that is left to a later spec.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole product (Constitution
  Principle I).
