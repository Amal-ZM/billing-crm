# Feature Specification: Dashboard

**Feature Branch**: `011-dashboard`

**Created**: 2026-09-27

**Status**: Done

**Depends on**: 007-invoice-status and 008-payments (verified running): the stored statuses (Draft, Sent, Paid, Cancelled), Overdue derived
from a Sent invoice's due date, and payments that settle an invoice to Paid; 005-invoice-create (verified running): the invoice list this
feature's cards link to

**Input**: User description: "Feature 011-dashboard. Depends on 008. Cards: Draft count, Unpaid (Sent+Overdue), Overdue, Paid this month,
unpaid amount sum. Click card → filtered invoice list. Given one overdue and one paid this month, When I open home, Then cards show 1 and 1
and unpaid excludes paid."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the invoice picture at a glance (Priority: P1)

Any signed-in staff member opens Home and immediately sees how many invoices need attention, without opening the invoice list and counting
by hand: how many are still Drafts, how many are unpaid (Sent, whether or not they are overdue), how many of those are specifically overdue,
how many became Paid this month, and how much money in total is still owed across the unpaid ones.

**Why this priority**: This is the whole feature. Everything else (the click-through) only matters once the numbers exist and are trusted.

**Independent Test**: Seed a Draft invoice, a Sent invoice not yet due, a Sent invoice past its due date, and an invoice paid in full earlier
this month; open Home; each of the five figures matches what a manual count of those invoices would give.

**Acceptance Scenarios**:

1. **Given** one Draft invoice, one on-time Sent invoice, one overdue Sent invoice and one invoice paid in full this month, **When** I open
   Home, **Then** the Draft card shows 1, the Unpaid card shows 2 (both Sent invoices), the Overdue card shows 1, the Paid this month card
   shows 1, and the unpaid amount sum is the two Sent invoices' remaining balances added together.
2. **Given** one overdue invoice and one invoice paid this month and no other invoices, **When** I open Home, **Then** the Overdue card and
   the Paid this month card each show 1, and the unpaid amount sum is exactly the overdue invoice's remaining balance (the paid invoice is
   excluded from it).
3. **Given** no invoices exist yet, **When** I open Home, **Then** every card shows 0 and the unpaid amount sum shows SAR 0.00.
4. **Given** a Cancelled invoice, **When** I open Home, **Then** it is not counted on any card and is not part of the unpaid amount sum.

---

### User Story 2 - Jump straight to the matching invoices (Priority: P2)

Having seen, for example, that 3 invoices are overdue, the staff member clicks the Overdue card and lands on the invoice list already
narrowed to exactly those 3 invoices, instead of having to find them by scrolling or guessing search terms.

**Why this priority**: Valuable on its own once User Story 1 exists, but the counts are useful even before this is built.

**Independent Test**: With known invoices in each bucket, click each card in turn and confirm the invoice list that opens contains exactly
that bucket's invoices and no others.

**Acceptance Scenarios**:

1. **Given** the Draft card shows a count, **When** I click it, **Then** the invoice list opens showing only Draft invoices.
2. **Given** the Unpaid card or the unpaid amount sum, **When** I click either, **Then** the invoice list opens showing only Sent invoices
   (on-time and overdue alike).
3. **Given** the Overdue card, **When** I click it, **Then** the invoice list opens showing only Sent invoices whose due date has passed.
4. **Given** the Paid this month card, **When** I click it, **Then** the invoice list opens showing only invoices that became Paid within
   the current calendar month.

---

### Edge Cases

- No invoices exist: every card reads 0 and the sum reads SAR 0.00, not an error or a blank space.
- An invoice's due date passes while the dashboard is open: the Overdue count is only as fresh as the last time Home was loaded, exactly
  like every other screen's Overdue flag; nothing on the dashboard updates itself without a reload.
- An invoice is paid across two payments in two different calendar months (a part payment last month, the settling payment this month):
  it counts as Paid this month once, on the date it actually became Paid, not the date of an earlier partial payment.
- A Cancelled invoice, at any point in its life, is not counted on any card and never appears in a card's filtered list.
- The unpaid amount sum only ever reflects Sent invoices' remaining balances; a Draft's calculated total (it has no "remaining" concept) and
  a Paid invoice's now-zero remaining balance are never part of it.
- A month with no invoices paid in it: the Paid this month card reads 0, and clicking it opens an empty invoice list, not an error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Home MUST show five figures, to any signed-in Admin or Agent: a Draft count, an Unpaid count, an Overdue count, a Paid this
  month count, and an unpaid amount sum in SAR.
- **FR-002**: The Draft count MUST be the number of invoices whose stored status is Draft.
- **FR-003**: The Unpaid count MUST be the number of invoices whose stored status is Sent, counting an overdue Sent invoice as unpaid too.
- **FR-004**: The Overdue count MUST be the number of invoices whose stored status is Sent and whose due date is before today's Saudi
  calendar date — the same rule the rest of the application already uses to show an invoice as Overdue.
- **FR-005**: The Paid this month count MUST be the number of invoices whose stored status is Paid and whose settling payment (the one
  that brought the remaining balance to zero) has a payment date within the current Saudi calendar month.
- **FR-006**: The unpaid amount sum MUST be the total, in SAR, of the remaining balance (grand total minus what has been paid) of every
  Sent invoice, on-time and overdue alike; it MUST NOT include Draft, Paid or Cancelled invoices.
- **FR-007**: Clicking the Draft card MUST open the invoice list showing only Draft invoices.
- **FR-008**: Clicking the Unpaid card, or the unpaid amount sum, MUST open the invoice list showing only Sent invoices (on-time and
  overdue alike).
- **FR-009**: Clicking the Overdue card MUST open the invoice list showing only Sent invoices whose due date has passed.
- **FR-010**: Clicking the Paid this month card MUST open the invoice list showing only invoices that became Paid within the current
  calendar month.
- **FR-011**: Every figure MUST be worked out fresh from the current invoices and payments each time Home is opened; nothing is cached,
  precomputed or stored.
- **FR-012**: A Cancelled invoice MUST NOT be counted on any card and MUST NOT appear in any card's filtered list.
- **FR-013**: Money on the dashboard MUST be shown in SAR with two decimals, using figures the invoice already calculates; the dashboard
  MUST NOT recalculate or re-derive any amount itself.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user sees all five dashboard figures on Home with no extra navigation, in the same page load that shows the rest
  of Home.
- **SC-002**: Every dashboard count and the amount sum match a manual count and sum of the same invoices, 100% of the time, across Draft,
  Sent (on-time and overdue), Paid and Cancelled invoices in any combination.
- **SC-003**: A user reaches the exact matching filtered invoice list from any one of the five cards in a single click, with no invoice
  missing and none extra.
- **SC-004**: An invoice paid this month is never counted as unpaid, and the unpaid amount sum is never inflated by an invoice that has
  already been paid in full, in any of the combinations SC-002 covers.

## Assumptions

- The dashboard is part of the existing Home page (the request's own wording, "When I open home"), not a new screen or route; it is shown
  to any signed-in Admin or Agent, matching how the invoice list itself is open to both roles.
- "This month" and "today" use the same fixed Saudi Arabia calendar date (UTC+03:00) the rest of the invoice feature already uses for due
  dates and Overdue, so the two never disagree about which calendar day or month it is.
- "Paid this month" is decided by the settling payment's own payment date (the date the money was received, already validated and stored
  on the payment), not by when the payment was typed into the system or by an invoice's original due date.
- Clicking the unpaid amount sum leads to the same filtered list as clicking the Unpaid card (both describe the same set of invoices, one
  as a count and one as a total).
- The invoice list itself gains only the narrow ability to show one of these five sets when reached from a card; it does not gain a general
  search or filter control of its own in this feature (that remains out of scope, as it has been since 005).
- The dashboard shows totals across all invoices; it is not scoped to the invoices a particular staff member created (this application has
  no concept of invoice ownership by staff member).
- No new stored data is introduced: the five figures are computed from the existing `Invoices` and `Payments` tables each time Home loads.
