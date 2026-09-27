# Feature Specification: Invoice History

**Feature Branch**: `010-invoice-history`

**Created**: 2026-09-26

**Status**: Done

**Depends on**: 007-invoice-status and 008-payments (verified running): the changes an invoice goes through (sent, cancelled, paid by payments); 005-invoice-create (verified running): creating and editing a Draft; 002-staff-auth (verified running): the signed-in staff member who makes each change

**Input**: User description: "Feature 010-invoice-history. Depends on 007 and 008. Immutable log: created, draft edited, sent, payment, cancelled. time, actor, old, new. UI cannot edit/delete history. Given I send an invoice, When I open History, Then a Sent row exists with my user."

## Clarifications

### Session 2026-09-26

- Q: Invoices created before this feature have no history and no record of who did what. What should their History show? → A: Nothing
  before now: history starts when it is introduced. An older invoice shows only what happens to it afterwards, with a note that earlier
  events were not recorded. No entries are invented and no user is guessed.
- Q: Who may open an invoice's History? → A: Any signed-in staff member (Admin or Agent), like every other invoice screen, so the person
  who sent an invoice can open History and see their own row.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every change to an invoice is recorded (Priority: P1)

Each time something happens to an invoice, the system writes one entry to that invoice's history: who did it, when, what kind of event it
was, and what it changed (the value before and the value after). The kinds of event are exactly five: the invoice was **created**, a
Draft was **edited**, the invoice was **sent**, a **payment** was recorded, and the invoice was **cancelled**. The entry is written in the
same step as the change, so a change that is saved always has its entry and a change that is refused leaves none.

**Why this priority**: This is the whole point of the feature. Without an entry written every time, there is nothing to read, and a
history that can miss a change is not a history.

**Independent Test**: As the Agent, create a Draft, edit it, send it, and record a payment; as the Admin, cancel a second invoice; then
read each invoice's history and confirm one entry per change, each with the right user, time and before/after values.

**Acceptance Scenarios**:

1. **Given** I create an invoice, **When** it is saved, **Then** its history has a **Created** entry with my user and the time, showing
   what it started with (the customer, the due date, the notes, the discount and its lines).
2. **Given** a Draft, **When** I save an edit that changes something, **Then** its history gains one **Draft edited** entry with my user
   and the time, listing each thing that changed with its value before and after (customer, due date, notes, discount, and each line
   added, removed or changed in quantity).
3. **Given** a Draft, **When** I save it without changing anything, **Then** no entry is written.
4. **Given** a Draft, **When** I send it, **Then** its history gains a **Sent** entry with my user and the time, showing the status going
   from Draft to Sent.
5. **Given** a Sent invoice, **When** I record a payment, **Then** its history gains a **Payment** entry with my user and the time,
   showing the payment's date, amount, method and note, and the amount paid and the amount remaining before and after; **and when that
   payment settles the invoice**, the same entry shows the status going from Sent to Paid.
6. **Given** a Draft or a Sent invoice, **When** an Admin cancels it, **Then** its history gains a **Cancelled** entry with the Admin's
   user and the time, showing the status going from Draft or Sent to Cancelled.
7. **Given** a change that is refused (a locked edit, an illegal status change, an overpayment, someone else's change won the race, a
   validation error), **When** it is refused, **Then** no entry is written and the history is exactly as it was.
8. **Given** two people change the same invoice at the same moment, **When** exactly one of the changes is applied and the other is
   refused, **Then** the history has one entry for the change that was applied and none for the refused one.
9. **Given** I am an Admin or an Agent, **When** I make any of these changes, **Then** the entry shows my email address and my role as they
   were at that moment.

---

### User Story 2 - Read an invoice's history (Priority: P1)

A staff member opens **History** from an invoice's details and sees the invoice's entries, newest first: the time, the user (email and
role), what happened, and, for each thing that changed, its value before and after. It is the answer to "who did what to this
invoice, and when?".

**Why this priority**: Recording without a way to read it back has no value, and the request's own example is about reading: "When I
open History, Then a Sent row exists with my user".

**Independent Test**: Send an invoice, open its History, and confirm a **Sent** row with your email, your role and the time; open the
history of an invoice made before this feature and confirm the empty state and its note.

**Acceptance Scenarios**:

1. **Given** I send an invoice, **When** I open its History, **Then** a **Sent** row exists showing my user (my email and role), the time,
   and the status going from Draft to Sent.
2. **Given** an invoice with several entries, **When** I open its History, **Then** I see them newest first, each with its time, user, kind
   of event and its before/after values, and the events I know happened are all there and none more.
3. **Given** the invoice's details, **When** I look at the actions, **Then** there is a **History** action for every invoice, whatever its
   status, for either role, and a way back from the History to the invoice.
4. **Given** an invoice made before this feature, **When** I open its History, **Then** if it has no entries I see a clear "no history has
   been recorded" message, and if it has entries (it was changed after the feature was introduced) I see them **with a note** that events
   before recording began are not shown; no entry is invented for the earlier events.
5. **Given** an invoice that does not exist, **When** I open its History, **Then** I see a clear "invoice not found" message; while it loads
   I see a loading state; if it fails to load I see an error with a way to retry.
6. **Given** I am signed out or my session has expired, **When** I try to open a History, **Then** I am sent to sign in and see nothing.
7. **Given** long text (a long note, a long product name, Arabic or English or a mix), **When** it appears in an entry, **Then** it is shown
   as written, wraps inside its place and reads in the right direction; amounts and dates stay left-to-right.
8. **Given** an entry, **When** I read it, **Then** I see the values as they were **at that time** (for example the customer's name and a
   product's name as they were then), not as they are now.

---

### User Story 3 - History cannot be edited or deleted (Priority: P1)

Once an entry is written it is permanent: nothing in the application lets anyone edit it, delete it, hide it, reorder it or add an entry
by hand, whatever their role. Changes to the invoice never rewrite past entries; they only add new ones.

**Why this priority**: An "immutable log" that could be altered would give false comfort. The request states it as a requirement: the
user interface cannot edit or delete history.

**Independent Test**: Look through every screen for a way to change a history entry (there is none); try to reach an edit or delete
operation for one directly (there is none); change and cancel invoices and confirm the earlier entries are exactly as they were.

**Acceptance Scenarios**:

1. **Given** the History screen, **When** I look at it, **Then** there is no edit, delete, hide, reorder or add control on it or on any entry.
2. **Given** any signed-in Admin or Agent, **When** they try to change or remove a history entry by any means the application offers,
   **Then** there is no such operation: the request is answered as not found or not allowed, and nothing changes.
3. **Given** an entry, **When** the invoice is later edited, sent, paid or cancelled, **Then** the entry is exactly as it was written; only
   new entries are added after it.
4. **Given** an entry, **When** the user account that made it is later changed or deactivated, **Then** the entry still shows the email
   and role it was written with.
5. **Given** a product or a customer that an entry mentions is later renamed, repriced or deactivated, **When** I read the entry, **Then** it
   still shows what it showed when written.
6. **Given** I ask for a History with a page number, filter, search or sort that the screen does not offer, **When** the request is made,
   **Then** it is refused or ignored and never changes anything: reading history never writes.

---

### Edge Cases

- **Overdue is not an event**: it is a way of showing a Sent invoice past its due date (007), nothing happens to the invoice, so no entry
  is written when the date passes. Neither is opening, printing or viewing an invoice or its history.
- **A payment that settles the invoice** is one entry (the payment) that also shows the change of status from Sent to Paid; there is no
  separate "Paid" entry, because the request lists exactly five kinds.
- **A Draft edit that changes several things** is **one** entry listing every change, not one entry per field; a Draft edit that changes
  nothing writes no entry (as 005 already writes nothing for it).
- **A Draft edit that removes and adds lines** lists each removed line and each added line with its product, quantity and, for an added
  line, the price and VAT percent it was given; a changed quantity shows its old and new value.
- **A very large edit** (up to the system's 100 lines) still produces one entry; the entry lists what changed, not the whole invoice.
- **Payments never edited**: a recorded payment is permanent (008), so its entry never has a later correction; a mistaken payment stays in
  the history for good.
- **Notes**: notes and payment notes appear in history as written (both are already visible on the invoice for the same users); a note
  that is cleared shows its old text and an empty new value.
- **Two entries at the same instant** (two changes to different invoices, or an edit and a send in quick succession on one) keep a
  definite, repeatable order: the order in which they were applied.
- **History is per invoice**: this feature has no list of every entry across invoices, no search and no filter.
- **Older invoices** (Clarifications): no entry is invented; the History says earlier events were not recorded.
- **Seeded and test data**: entries are written only by real changes made through the application; nothing writes an entry any other way.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST keep, for each invoice, a history made of **entries**. Each entry has: its **kind** (exactly one of
  **Created**, **Draft edited**, **Sent**, **Payment**, **Cancelled**), the **time** it was written, the **user** who made the change (their
  email address and their role as they were then), and the **changes** it made, each with its **value before** and its **value after**
  (a Created entry has no "before").
- **FR-002**: An entry MUST be written **in the same step** as the change it describes: if the change is saved, its entry exists; if the
  change is refused or fails, no entry exists. There MUST be no way for a saved change to lack its entry, or for an entry to exist for
  a change that was not saved.
- **FR-003**: A **Created** entry MUST show what the invoice started with: the customer, the due date, the notes (if any), the discount (if
  any) and each line (product, quantity), and MUST be written when the invoice is first saved.
- **FR-004**: A **Draft edited** entry MUST be written when, and only when, a save of a Draft changes something, and MUST list every change
  in that save with its value before and after: customer, due date, notes, discount, and each line added, removed or changed in quantity.
  One save is one entry.
- **FR-005**: A **Sent** entry MUST show the status going from Draft to Sent.
- **FR-006**: A **Payment** entry MUST show the payment's date, amount, method and note, the amount paid before and after and the amount
  remaining before and after, and, when the payment settles the invoice, the status going from Sent to Paid.
- **FR-007**: A **Cancelled** entry MUST show the status going from Draft or Sent to Cancelled.
- **FR-008**: Values in an entry MUST be recorded **as they were at the time** (names as text, amounts as the exact two-decimal SAR values
  the invoice showed), so that later changes to a customer, a product or a user never change what an entry says. Money is exact SAR
  with two decimals and is never rounded or recalculated when an entry is read.
- **FR-009**: The user of an entry MUST be the signed-in staff member who made the request (their email address and role as they were
  then); a request cannot claim to be from another user, and every entry MUST have a user.
- **FR-010**: Any signed-in Admin or Agent MUST be able to open the History of any invoice, whatever its status. A request without a
  valid sign-in MUST be refused (Constitution Principle III) and show no history.
- **FR-011**: The invoice's details MUST offer a **History** action for every invoice, and the History screen MUST show the entries **newest
  first** with, for each: the time, the user's email and role, the kind of event, and each change with its value before and after. It MUST
  have a clear loading state, an "invoice not found" message, an error with a **Retry**, and a message when there are no entries.
- **FR-012**: An invoice with no **Created** entry (one made before this feature) MUST show a note on its History that events before
  recording began are not shown; the feature MUST NOT create entries for events that happened before it was introduced.
- **FR-013**: History MUST be **immutable**: no entry can be edited, deleted, hidden, reordered or added by hand through any screen or any
  request the application accepts, for any role. The application MUST offer no such operation; a request to try one MUST fail and change
  nothing. Reading history MUST never write anything.
- **FR-014**: Changing, sending, paying or cancelling an invoice MUST NOT alter any earlier entry; it only adds entries.
- **FR-015**: Entries MUST be shown as written, in the right direction for Arabic and English text, with amounts and dates left-to-right,
  long text wrapping inside its place, and the same date and money formats as the rest of the invoice screens.
- **FR-016**: The repository MUST include automated tests covering: one entry of the right kind, user, time and before/after values for each
  of the five events (including a Payment that settles the invoice and a Draft edit that changes several things, adds and removes lines);
  no entry for a save that changes nothing and for every refused change (locked edit, illegal change, overpayment, validation error,
  lost race); the entry and the change being saved together (a failed save leaves neither); the user taken from the sign-in (Admin and
  Agent), never from the request; values as at the time (rename a customer or product afterwards); newest-first order and a definite order
  for entries at the same instant; the History screen's states and content for each kind, the note for an older invoice, and text
  direction; that no operation, screen or control exists to change or remove an entry (the exact list of operations is pinned); that
  earlier entries are untouched by later changes; and that reading history writes nothing.
- **FR-017**: This feature MUST NOT include: entries for events other than the five (viewing, printing, the Overdue date passing, login);
  history for customers, products or staff; a history list across invoices, search, filter, sort or export; undo or restore from
  history; comments or a reason field on a change; deleting, archiving or purging old entries; notifications; backfilled or
  reconstructed entries for events before it existed; and no shop, cart, payment gateway, support tickets or WhatsApp functionality
  (Constitution Principle I).

### Key Entities

- **History entry**: one permanent record of one change to one invoice: the invoice it belongs to, its kind, when it was written, who made the
  change (email and role), and its list of changes, each a description of what changed with its value before and after. It is written once
  and never changed.
- **Change (in an entry)**: a named thing that changed (for example "Due date", "Status", "Amount paid", "Line: Consulting hour quantity"),
  its value before (none for a Created entry or an added line) and its value after (none for a removed line).
- **Invoice, customer, product, payment, staff user**: unchanged by this feature. The history refers to an invoice and copies, as text,
  what it needs of the others at the time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of saved changes of the five kinds have exactly one entry with the right kind, user and before/after values, and 0% of
  refused or unsaved changes leave an entry.
- **SC-002**: 100% of entries carry the email and role of the signed-in user who made the change, and 0% can be made to name someone else.
- **SC-003**: A staff member can go from an invoice's details to its History in one action and read who sent it and when, in under 10 seconds.
- **SC-004**: There is no screen, control or operation by which an entry can be edited, deleted, hidden, reordered or added, for any role,
  and 100% of attempts to change or remove one are refused with nothing changed.
- **SC-005**: For 100% of entries, what is read now equals what was written, including after the customer, the product and the user it
  mentions have been renamed, repriced, changed or deactivated.
- **SC-006**: 100% of requests to open a History without a valid sign-in are refused and show no history.
- **SC-007**: All automated tests in the repository pass on the developer's machine, and the developer can, in a browser, send an invoice, open
  its History and see a **Sent** row with their own email, then confirm that no way to change that row exists.

## Assumptions

- 002 to 009 are complete and verified. Every change this feature records already goes through one place per kind of event (create, edit,
  send, record a payment, cancel), each a single all-or-nothing save; the entry is written in that same save.
- **The five kinds are exactly the five in the request.** A payment that settles the invoice is one **Payment** entry that also shows the
  change to Paid (there is no sixth "Paid" kind).
- **The user** is the signed-in staff member's email and role at the time (the system has no other name for a person). Earlier entries keep
  the email and role they were written with even if the account is later changed or deactivated.
- **Values are stored as text or exact money as at the time**, not as links to the customer, product or user, so a later rename cannot change
  an entry (FR-008).
- **Immutability is enforced by the application**: it offers no way to change or remove an entry. Whether the database is also made to
  refuse changes to the history table (defence in depth against someone with direct database access) is decided in planning, because it
  is a new kind of database object (Constitution Principle V).
- **Older invoices** (Clarifications): no backfill, a note on their History; the history of an invoice starts at its first recorded event.
- **Any signed-in user may read any invoice's history** (Clarifications), matching who may already read the invoice, its payments and its
  notes; the history adds no data a signed-in user could not already see, except *who* made each change and the earlier values.
- **Order**: newest first, then, for the same instant, the order in which the changes were applied. All entries of an invoice are shown (no
  paging), since an invoice has a bounded life; a later spec can add paging if it is ever needed.
- **Times** are shown in the same form as the invoice's other times (created, sent, cancelled), with the same time-zone behaviour as the
  details.
- **The History is a screen of the signed-in application**, reached from the invoice's details, with the same sign-in and session handling
  as the other screens.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole product (Constitution
  Principle I).
