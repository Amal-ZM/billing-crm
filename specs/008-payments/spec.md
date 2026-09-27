# Feature Specification: Payments

**Feature Branch**: `008-payments`

**Created**: 2026-09-26

**Status**: Done

**Depends on**: 007-invoice-status (verified running): Draft, Sent (shown as Overdue when late), Paid and Cancelled statuses, the read-only lock and amounts frozen when an invoice is sent; 006-invoice-totals (the grand total); 002-staff-auth (the Admin and Agent roles)

**Input**: User description: "Feature 008-payments. Depends on 007. Payment on Sent/Overdue: date, amount > 0, Cash/Bank/Other, note. Remaining = grand - paid. Remaining 0 → Paid. No overpay. No pay on Draft/Cancelled. Given grand 230, When I pay 100 then 130, Then Paid and remaining 0. Use /invoice-money."

## Clarifications

### Session 2026-09-26

- Q: Once a payment is recorded, can it ever be corrected or voided? → A: No: payments are permanent (no edit, void or delete); a
  mistake is handled outside the system until a later spec adds refunds or credit notes.
- Q: Can an Admin cancel a Sent invoice that already has payments recorded? → A: No: it is refused (409) while any payment exists,
  because there are no refunds; a Sent invoice with no payments, and a Draft, can still be cancelled as in 007.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record a payment against a Sent invoice (Priority: P1)

A staff member receives money from a customer for an invoice that has been sent (including one that is Overdue) and **records the
payment**: the date it was received, the amount in SAR, how it was paid (Cash, Bank or Other) and an optional note. The invoice
then shows what has been **paid** and what is **remaining** (the grand total minus everything paid so far). When a payment brings the
remaining amount to exactly zero, the invoice becomes **Paid**.

**Why this priority**: Until money can be recorded against an invoice, the CRM can say what is owed but never that it has been
settled. This is the whole point of the spec, and every other story builds on it.

**Independent Test**: Create a Draft for ABC with `CNS-001` quantity 2 (grand total 230.00), send it, record a payment of 100.00, and
confirm it shows 100.00 paid and 130.00 remaining and is still Sent; record 130.00 and confirm it is Paid with 0.00 remaining.

**Acceptance Scenarios**:

1. **Given** a Sent invoice with a grand total of 230.00 and nothing paid, **When** I record a payment of 100.00, **Then** the payment is
   saved, the invoice shows 100.00 paid and 130.00 remaining, and its status is still Sent.
2. **Given** that invoice with 100.00 paid, **When** I record a payment of 130.00, **Then** the invoice shows 230.00 paid and 0.00
   remaining, and its status is **Paid**.
3. **Given** a Sent invoice whose due date has passed (shown as Overdue), **When** I record a payment that leaves something remaining,
   **Then** the payment is saved and the invoice is still shown as Overdue; **When** a payment brings the remaining to 0.00, **Then** it is
   Paid and no longer shown as Overdue.
4. **Given** a Sent invoice, **When** I record a payment with a date, an amount, a method of Cash, Bank or Other, and a note, **Then** all
   four are saved exactly as given and shown in the invoice's list of payments.
5. **Given** a Sent invoice, **When** I record a payment without a note, **Then** it is saved (the note is optional).
6. **Given** either an Admin or an Agent is signed in, **When** they record a payment, **Then** it is saved (recording payments is
   day-to-day staff work, like sending).
7. **Given** an invoice that does not exist, **When** I try to record a payment, **Then** I see a clear "invoice not found" outcome.
8. **Given** I am signed out or my session has expired, **When** I try to record a payment, **Then** I am sent to sign in and nothing
   changes.

---

### User Story 2 - Refuse payments that must not be recorded (Priority: P1)

A payment that would be wrong is refused, and nothing is saved: a payment on an invoice that is not open for payment (Draft,
Cancelled or already Paid), an amount that is zero, negative or has more than two decimals, an amount larger than what remains (no
overpaying), a missing or unknown method, a missing or impossible date, or a note that is too long.

**Why this priority**: A record that accepts a payment on a cancelled invoice, or more than is owed, is worse than no record, and the
rule "no overpay" is part of the request itself. Getting the refusals right is as important as recording.

**Independent Test**: On invoices in each status and with each bad input, try to record a payment and confirm the exact refusal and
that the invoice's paid and remaining amounts and status did not change.

**Acceptance Scenarios**:

1. **Given** a Draft invoice, **When** I try to record a payment, **Then** it is refused as an illegal action (a conflict) with a message
   naming the status ("An invoice that is Draft cannot take a payment."), and nothing changes.
2. **Given** a Cancelled invoice, **When** I try to record a payment, **Then** it is refused in the same way, and nothing changes.
3. **Given** an invoice that is already Paid, **When** I try to record a payment, **Then** it is refused in the same way (nothing remains
   to pay), and nothing changes.
4. **Given** a Sent invoice with 130.00 remaining, **When** I try to record 130.01, **Then** it is refused with a message on the amount
   that says how much remains, and nothing changes; **When** I record exactly 130.00, **Then** it is accepted.
5. **Given** a Sent invoice, **When** I try to record an amount of 0, a negative amount, an amount with a third decimal place (for
   example 10.005), or no amount, **Then** it is refused with a message on the amount and nothing changes. An amount with too many
   decimals is refused, never rounded.
6. **Given** a Sent invoice, **When** I try to record a payment with no method, or a method other than Cash, Bank or Other, **Then** it is
   refused with a message on the method.
7. **Given** a Sent invoice, **When** I try to record a payment with no date, a date that is not a real date, a date in the future
   (after today in Saudi Arabia), or a date before the invoice was sent, **Then** it is refused with a message on the date.
8. **Given** a Sent invoice, **When** I try to record a payment whose note is longer than 1000 characters, **Then** it is refused with a
   message on the note.
9. **Given** a payment with several things wrong, **When** I record it, **Then** all the problems on the fields are reported together, not
   one at a time (as in 005).
10. **Given** an invoice that is not open for payment and a payment that is also invalid, **When** I try to record it, **Then** the
    status refusal (the conflict) is what I get: the invoice's status is judged before the payment's contents. (A body that cannot be
    read at all, such as malformed JSON or text where a number belongs, is refused as a plain 400 before any of this, as for every
    other request in this system; "contents" means the values in a readable body.)
11. **Given** a Sent invoice with a payment of 100.00 recorded, **When** an Admin tries to cancel it, **Then** it is refused as a
    conflict ("An invoice that has payments cannot be cancelled."), nothing changes, and the **Cancel** action is not offered; **Given**
    a Sent invoice with no payments, **Then** an Admin can still cancel it.
12. **Given** any recorded payment, **When** I look for a way to edit or delete it, **Then** there is none (payments are permanent).

---

### User Story 3 - See what has been paid on an invoice (Priority: P2)

On the invoice's details a staff member sees the **amount paid**, the **amount remaining**, and the **list of payments** (date, amount,
method, note) in the order they were made, next to the invoice's other amounts. A Paid invoice shows its payments and 0.00 remaining;
a Sent invoice with no payments shows 0.00 paid and the whole grand total remaining. The invoice list shows the new **Paid** status.

**Why this priority**: Recording is only trustworthy if it can be read back. The list and totals are how a person confirms the payment
they just entered, and how they see what is still owed.

**Independent Test**: Record two payments on an invoice, open it and confirm both appear with their details, that paid and remaining
add up to the grand total, and that the list shows the invoice as Paid once settled.

**Acceptance Scenarios**:

1. **Given** an invoice with payments of 100.00 (Cash) and 130.00 (Bank), **When** I open it, **Then** I see both payments with their
   date, amount, method and note, 230.00 paid and 0.00 remaining.
2. **Given** a Sent invoice with no payments, **When** I open it, **Then** I see 0.00 paid, the whole grand total remaining, and no
   payments listed (with a short "no payments yet" message).
3. **Given** a Draft or Cancelled invoice, **When** I open it, **Then** I see no payment action and no payments section that
   invites adding one (a Cancelled invoice with no payments shows none).
4. **Given** a Sent invoice, **When** I open it, **Then** I see the **Record payment** action; **Given** a Paid invoice, **Then** I
   do not.
5. **Given** the payment form, **When** I submit invalid input, **Then** each message appears on the field it belongs to, what I typed
   is kept, and I can correct it and try again; a refusal for the invoice's status shows a message at the top and reloads the
   invoice so the screen matches reality.
6. **Given** the list of invoices, **When** an invoice has become Paid, **Then** the list shows it as **Paid** (and a Sent invoice
   that is Overdue still shows Overdue).
7. **Given** the details are loading, fail to load, or the invoice is not found, **When** I look at the screen, **Then** I see a
   loading, error-with-retry or not-found state (as before).

---

### Edge Cases

- **Exact money**: paid and remaining are exact sums and differences of amounts with two decimals, so they never drift by a halala;
  a payment of the exact remaining amount always settles the invoice, whatever the earlier payments were. No rounding is involved.
- **Two people pay at once**: two payments recorded for the same invoice at the same moment that together would exceed the remaining
  amount end with exactly one saved and the other refused ("changed by someone else, reload and try again", the same conflict
  result as in 007), so an invoice is never overpaid and never has its status decided from stale numbers.
- **A payment races a cancel** (an Admin cancels a Sent invoice while someone records a payment): exactly one of them is applied, and
  an invoice is never left both Cancelled and holding a payment.
- **A Sent invoice with a grand total of 0.00** has nothing to pay: a payment is refused (the amount cannot be more than the 0.00
  that remains) and the invoice stays Sent.
- **The amounts of a Sent invoice never move** (007): a product repriced after sending does not change the grand total, so it cannot
  change what remains. Remaining is always the invoice's own grand total minus its payments.
- **A payment on an Overdue invoice** is accepted exactly as on a Sent one; Overdue is only how a late Sent invoice is shown.
- **A payment dated before the invoice was sent** is refused (a payment cannot precede the bill), and one dated today (Saudi
  Arabia) is accepted.
- **Payments are never deleted or changed** (see the assumptions): the list only grows, and the paid total only goes up.
- **A Paid invoice is final**: it cannot be sent, cancelled, edited or paid again, and it stays Paid for good.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Any signed-in staff member (Admin or Agent) MUST be able to **record a payment** against an invoice that is **Sent**
  (including one shown as Overdue). A payment has a **date**, an **amount** in SAR, a **method** (exactly one of **Cash**, **Bank**,
  **Other**) and an optional **note**, and is saved with the time it was recorded.
- **FR-002**: The **remaining** amount of an invoice MUST be its grand total minus the sum of its payments, and the **paid** amount MUST be
  the sum of its payments. Both MUST be exact, in SAR with two decimals, and MUST follow the project's money rules (the `invoice-money`
  skill): no floating point, no rounding of a payment, no figure calculated anywhere but the server. The grand total used is the
  invoice's own frozen grand total (007), never a fresh calculation from the current product prices.
- **FR-003**: A payment's **amount** MUST be greater than zero, in SAR with at most two decimals (a value with more decimals is refused,
  never rounded), and MUST NOT be more than the remaining amount (no overpaying). An amount of exactly the remaining amount is allowed.
- **FR-004**: A payment MUST be **refused** (with the conflict result, 409, a message naming the current status, and nothing changed) when
  the invoice is **Draft**, **Cancelled** or **Paid**. The invoice's status MUST be judged before the payment's contents (as in 007), and
  an unknown invoice MUST be "not found" before anything else.
- **FR-005**: When a payment brings the remaining amount to exactly **0.00**, the invoice MUST become **Paid** in the same step as saving
  that payment: either both happen or neither. While anything remains the status MUST NOT change. **Paid** is reached only this way:
  there is no action that marks an invoice Paid by hand.
- **FR-006**: A **Paid** invoice MUST be final: it cannot be sent, cancelled, edited or paid again (007's rules already refuse each of
  these; **Sent to Paid** becomes the one new legal change of status, and only a payment causes it), and it MUST NOT be shown as
  Overdue.
- **FR-007**: A payment's **date** MUST be a real calendar date, MUST NOT be later than today's date in Saudi Arabia (UTC+03:00, the fixed
  offset used for due dates in 005 and 007), and MUST NOT be earlier than the date the invoice was sent (also the Saudi date). A **note**,
  when given, MUST be at most 1000 characters (the same limit as an invoice's notes) and is kept as typed apart from surrounding spaces.
- **FR-008**: A request with problems in its fields MUST report **all** of them together as field errors on `date`, `amount`, `method`
  and `note` (as in 005 and 006), and MUST change nothing. A refused request of any kind (not allowed, not found, wrong status, invalid
  fields, overpayment, changed by someone else) MUST be a structured error response, never an unhandled failure, and MUST leave the
  invoice's payments, paid and remaining amounts and status exactly as they were.
- **FR-009**: Two payments (or a payment and a cancel) recorded for the same invoice at the same moment MUST end with the results one
  would get if they had happened one after the other, with the loser told to reload and try again: an invoice MUST never end up with
  more paid than its grand total, or with a payment on an invoice that was cancelled first.
- **FR-010**: Payments are **permanent**: once recorded a payment MUST NOT be edited, voided or deleted by anyone, and there is no way to
  reverse one in this spec. A mistaken entry is handled outside the system until a later spec adds refunds or credit notes.
- **FR-011**: An invoice that has **one or more payments** MUST NOT be cancelled: an Admin's request to cancel it is refused with the
  conflict result (409) and a message that says so ("An invoice that has payments cannot be cancelled."), and nothing changes, because
  this spec has no refunds and a Cancelled invoice would otherwise be left holding money. An Agent's request is still refused as not
  allowed for their role first (007). Cancelling a Draft, or a Sent invoice with no payments, works as in 007. The **Cancel** action
  is not offered for an invoice that has payments.
- **FR-012**: The invoice details response and screen MUST show the **paid** amount, the **remaining** amount and the **list of payments**
  (date, amount, method, note, in the order recorded), and MUST offer **Record payment** only when the invoice is Sent (including Overdue)
  and only when something remains. The screens MUST NOT calculate paid or remaining themselves: they show the figures the API returns.
  The form MUST keep what the person typed when a refusal comes back, show each message on its field, disable the action while a
  request is in progress so nothing is recorded twice, and reload the invoice after a status refusal. The invoice list MUST show the
  **Paid** status like any other.
- **FR-013**: An invoice's **Sent to Paid** change MUST be reflected wherever the status is shown, and the **allowed changes** the API
  reports for an invoice MUST stay accurate (a Paid invoice permits none; a Sent one permits Cancelled only while it has no payments,
  FR-011).
- **FR-014**: A request without a valid sign-in MUST be refused (Constitution Principle III). Recording a payment MUST be open to Admin
  and Agent; every endpoint added MUST declare its authorization.
- **FR-015**: The repository MUST include automated tests covering: recording on Sent and Overdue (either role); the worked example (230.00,
  pay 100.00 then 130.00, Paid and 0.00 remaining); every refusal (Draft, Cancelled, Paid, zero, negative, third decimal, no amount,
  overpay by one halala, exact remaining accepted, method, date in the future, date before sending, note too long, several errors at
  once, status judged before contents, unknown invoice, signed out); that paid and remaining are exact after many small payments; that
  the status changes to Paid only when the remaining is exactly 0.00 and in the same step as the payment; that a repriced product
  does not change the remaining amount; two payments at once (one wins); a payment racing a cancel; cancel refused once
  a payment exists (Admin, and Agent still 403), cancel allowed with none, and no route that edits or deletes a payment; the screens' action offered only when legal, and the form's field messages, kept input and reload after a status refusal.
- **FR-016**: This feature MUST NOT include a **payment gateway** or any way to charge a card or move money (a payment here is a record
  that staff made after receiving money), refunds, credit notes, receipts, PDFs or email, reminders, a payments report, part-payment
  schedules, other currencies, per-user history of who recorded a payment, or search/filter of payments; and no shop, cart, support
  tickets or WhatsApp functionality (Constitution Principle I).

### Key Entities

- **Payment**: a record that money was received for one invoice: **date**, **amount** (SAR, two decimals, greater than zero),
  **method** (Cash, Bank or Other), an optional **note**, and the **time it was recorded**. It belongs to exactly one invoice and is
  kept for good. An invoice has zero or more payments.
- **Invoice** (from 005 to 007): gains its list of payments and two figures worked out from them, **paid** and **remaining**; its
  status can now become **Paid** (the change from Sent, caused only by a payment that settles it).
- **Customer, product, invoice line**: unchanged by this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A staff member can record a payment on a Sent invoice and see the new paid and remaining amounts in under 30 seconds.
- **SC-002**: In the worked example (grand total 230.00, payments of 100.00 then 130.00), the invoice shows 130.00 remaining after the
  first payment, and is Paid with 0.00 remaining after the second, every time.
- **SC-003**: 100% of payments on Draft, Cancelled or Paid invoices are refused and change nothing.
- **SC-004**: 100% of attempts to pay more than what remains, even by 0.01, are refused, and no invoice ever has more paid than its grand
  total.
- **SC-005**: 100% of payments that bring the remaining amount to 0.00 make the invoice Paid, and 0% of others change its status.
- **SC-006**: Paid plus remaining equals the invoice's grand total to the halala for every invoice, however many payments it has.
- **SC-007**: When two people record payments for the same invoice at the same moment, no combination ends with the invoice overpaid.
- **SC-008**: 100% of payment requests without a valid sign-in are refused.
- **SC-009**: All automated tests in the repository pass on the developer's machine, and the developer can, in a browser, send an
  invoice, record two payments that settle it, see it become Paid, and see a payment for more than what remains refused.

## Assumptions

- 002 to 007 are complete and verified. A Sent invoice already has frozen amounts, and Paid already exists as a final status that
  nothing sets; this spec adds the way an invoice becomes Paid (a payment that settles it).
- **Remaining is calculated, not stored as its own figure that could drift**: it is the invoice's frozen grand total minus its
  payments, and status is the one stored consequence (Paid), changed in the same step as the payment that causes it.
- **Who may record**: Admin and Agent, like sending. There is no Admin-only step in this spec (Cancel stays Admin only, as in 007).
- **Method** is exactly Cash, Bank or Other (as requested); no further methods without a later spec.
- **Payment date**: a real date, not after today in Saudi Arabia, not before the date the invoice was sent. A payment can be back-dated
  within that window (money is often received before it is recorded). The time recorded is separate, and only the time.
- **Money**: SAR only; amounts are exact to two decimals, shown as text with two decimals, entered as text and sent as a number, and
  a value with more than two decimals is refused, never rounded (the rule from products and invoices). The largest amount is bounded
  by the invoice's grand total, so the 006 maximum is never exceeded.
- **No history of who**: like 007's send and cancel, a payment records only the time it was recorded, not which staff member.
- **No delete, no edit** of payments in this spec, following the project's "no delete" pattern (customers, products and invoices are
  never deleted). A mistaken payment cannot be undone here; refunds and credit notes are for a later spec (FR-010 records the decision). Because of that, an invoice with payments cannot be
  cancelled (FR-011), which changes 007's Sent-to-Cancelled rule only for an invoice that has payments.
- **Recording a payment is not a payment gateway**: it stores what staff say they received. Charging or moving money is out of scope for
  the whole product (Constitution Principle I).
- The invoice list shows the status (now including Paid) but no paid or remaining columns, and has no filter; that is left to a later
  spec.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole product (Constitution
  Principle I).
