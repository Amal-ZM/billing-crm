# Feature Specification: Invoice Totals

**Feature Branch**: `006-invoice-totals`

**Created**: 2026-09-24

**Status**: Done

**Depends on**: 005-invoice-create (verified running): Draft invoices with saved lines (quantity, unit price and VAT percentage copied from the product when the line was added)

**Input**: User description: "Feature 006-invoice-totals. Depends on 005. Line net = qty * price. VAT per line. Optional header discount on net. Grand total 2 decimals SAR. Changing product price later must not change saved lines. Given qty 2 price 100 VAT 15% no discount, When saved, Then net 200 VAT 30 grand 230."

## Clarifications

### Session 2026-09-24

- Q: Is the header discount a fixed SAR amount or a percentage of the total net? → A: A fixed amount in SAR.
- Q: How is the discount applied to lines with different VAT percentages? → A: Shared over the lines in proportion to their
  net; each line's VAT is worked out on its own discounted net; the shares add up exactly to the discount.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the amounts on a Draft invoice (Priority: P1)

When a staff member saves a Draft invoice, the system works out what the customer owes and shows it: for every line the
**net** (quantity times the line's saved unit price) and the **VAT** (the line's saved VAT percentage applied to that net),
and for the whole invoice the total net, the total VAT and the **grand total**, all in Saudi riyals with two decimals.
The same amounts are shown whenever the invoice is opened.

**Why this priority**: 005 stores what is being billed but shows no amounts, so an invoice cannot yet be read as a bill.
Correct, exact totals are the point of the whole product and everything after this (issuing, payments, balances) builds on them.

**Independent Test**: Create a Draft for customer ABC with one line of `CNS-001` (unit price 100.00, VAT 15%) and quantity 2,
save it, and confirm the line shows net 200.00 and VAT 30.00 and the invoice shows total net 200.00, total VAT 30.00 and grand
total 230.00 SAR. Reopen the invoice and see the same amounts.

**Acceptance Scenarios**:

1. **Given** a line with quantity 2, saved unit price 100.00 and saved VAT 15%, and no discount, **When** the invoice is saved,
   **Then** the line shows net 200.00 and VAT 30.00 and the invoice shows total net 200.00, total VAT 30.00 and grand total 230.00 SAR.
2. **Given** a Draft with two lines at different VAT percentages (for example 200.00 at 15% and 50.00 at 0%), **When** it is
   saved, **Then** each line's VAT is worked out from its own percentage (30.00 and 0.00), the total VAT is 30.00 and the grand
   total is 280.00.
3. **Given** quantity 1.25, unit price 10.55 and VAT 15%, **When** the invoice is saved, **Then** the line net is 13.19
   (13.1875 rounded to two decimals) and the line VAT is 1.98 (15% of 13.19 is 1.9785, rounded), so the amounts shown are the
   amounts that are added up.
4. **Given** a line whose saved unit price is 0 (a free item), **When** the invoice is saved, **Then** its net and VAT are 0.00
   and it does not disturb the other lines' amounts.
5. **Given** a saved Draft, **When** I open it, **Then** I see every line with its net and VAT, and the invoice's total net,
   total VAT and grand total, each with two decimals and marked as SAR.
6. **Given** I am on the invoice form, **When** I change a quantity or add or remove a line and save, **Then** the amounts
   are recalculated from the lines as saved and shown after the save; the form itself does not do any calculation.
7. **Given** a Draft created in 005, before amounts existed, **When** I open it, **Then** it shows amounts worked out from its
   saved lines (with no discount), without anyone having to edit and save it first.
8. **Given** I am signed out or my session has expired, **When** I try to see or change an invoice's amounts, **Then** I am
   sent to sign in and nothing is shown or changed.

---

### User Story 2 - Give a discount on the whole invoice (Priority: P2)

A staff member can enter an optional discount for the whole invoice. The discount is taken off the **net** before VAT is
worked out, so the VAT the customer pays is on the reduced amount. Leaving the discount empty (or 0) means no discount.

**Why this priority**: Discounts are common in day-to-day billing, but invoices are already useful and correct without
them, so they follow the basic amounts.

**Independent Test**: On the Draft from story 1 (net 200.00, VAT 15%), enter a discount of 20.00, save, and confirm the invoice
shows total net 200.00, discount 20.00, net after discount 180.00, VAT 27.00 and grand total 207.00; then clear the
discount, save, and confirm the amounts return to 230.00.

**Acceptance Scenarios**:

1. **Given** a Draft with total net 200.00 at VAT 15%, **When** I enter a discount of 20.00 and save, **Then** the invoice
   shows total net 200.00, discount 20.00, net after discount 180.00, VAT 27.00 and grand total 207.00 SAR.
2. **Given** a Draft with lines at different VAT percentages, **When** I save a discount, **Then** the discount is shared over
   the lines in proportion to their net, each line's VAT is worked out on its own reduced net, the shares add up to exactly the
   discount, and the amounts on the screen add up to the grand total to the last halala.
3. **Given** a discount equal to the whole net, **When** I save, **Then** the net after discount, VAT and grand total are
   all 0.00 and the save is accepted.
4. **Given** a negative discount, a discount larger than the total net, a discount with more than two decimals, or text that is
   not a number, **When** I save, **Then** I am told the discount is wrong (naming the limit) and nothing is saved.
5. **Given** a discount was saved, **When** I edit the Draft and change quantities or lines, **Then** the discount stays as
   entered and the amounts are recalculated; if the lines are reduced so that the discount is now larger than the net, the
   save is refused with a message naming the discount and the person's input stays on the screen.
6. **Given** I clear the discount box and save, **When** the invoice is reopened, **Then** it shows no discount and the amounts
   without one.
7. **Given** a save is refused for any reason, **When** the message appears, **Then** the discount and every other value I
   entered are still on the screen and the invoice is exactly as it was.

---

### User Story 3 - Amounts stay true when products change (Priority: P3)

Once a line is saved, the amounts on it depend only on what was saved with it: its quantity, its saved unit price and its
saved VAT percentage. A later change to the product's price, VAT percentage, name or active state never changes any amount on
an existing invoice. Editing a Draft recalculates only from its saved lines.

**Why this priority**: 005 already saves the price and VAT with each line; this story proves the amounts really are built
from those copies. It protects trust in issued figures, but it adds no new screen.

**Independent Test**: Save the story 1 Draft (grand total 230.00). As an Admin, change `CNS-001` to price 150.00 and VAT 5%, or
deactivate it. Reopen the invoice: every amount is unchanged. Edit the quantity to 3, save: net is 300.00 (still at 100.00),
VAT 45.00, grand total 345.00.

**Acceptance Scenarios**:

1. **Given** a saved Draft with grand total 230.00, **When** an Admin changes the product's price, VAT percentage or name, or
   deactivates it, **Then** the invoice opened afterwards shows exactly the same line and invoice amounts.
2. **Given** that same Draft, **When** I change the existing line's quantity from 2 to 3 and save, **Then** the amounts use
   the line's saved price and VAT (net 300.00, VAT 45.00, grand total 345.00), not the product's new values.
3. **Given** that same Draft, **When** I add a second line for the repriced product, **Then** the new line's amounts use the
   product's current price and VAT (copied at that moment) while the first line keeps its original amounts.

---

### Edge Cases

- Rounding: a line net (quantity has up to two decimals and so does a price, so a net can have up to four) is rounded to two
  decimals, halves away from zero (0.025 becomes 0.03), and VAT is rounded the same way. Rounding is done once per line, never
  on a running total, so the same invoice (same lines in the same order, same discount) always gives the same amounts.
- A quantity such as 1.5 or a price such as 0.05 works exactly; nothing is ever calculated with approximate numbers.
- A discount share too small to matter (for example a 0.01 discount over three lines) is still shared out exactly: the shares
  add up to the discount, never to a cent more or less.
- The same product on two lines is two lines: each has its own amounts, and the invoice totals add them.
- Very large amounts: with the largest allowed quantity, price and 100 lines the totals would be far beyond any real invoice.
  An amount (a line net or an invoice total) larger than 9,999,999,999,999.99 SAR (about ten trillion riyals, chosen so every
  amount is exact on every screen) is refused with a message that the line or the invoice total is too large, and nothing is
  saved or changed; it never crashes and never shows a wrong total.
- A product's price or VAT percentage changes while a person is editing the form: the existing lines keep their saved values;
  only a line added afterwards gets the new ones.
- Two people editing the same Draft at once: the same rule as 005 applies (a clash on the same rows is refused with "changed by
  someone else, reload"; otherwise the later save wins), and the amounts are always the ones worked out from the lines and
  discount that were actually saved.
- Arabic or English text in customer and product names or notes is unaffected by this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Any signed-in staff member (Admin or Agent) MUST be able to see the amounts of any invoice and enter a discount on
  a Draft. Anyone not signed in MUST be refused everything.
- **FR-002**: For every line the system MUST calculate the line **net** as quantity times the line's **saved** unit price,
  rounded to two decimals (halves away from zero).
- **FR-003**: For every line the system MUST calculate the line **VAT** as the line's **saved** VAT percentage applied to the
  line's net (after that line's share of any discount, see FR-006), divided by 100 and rounded to two decimals (halves away
  from zero). VAT is worked out line by line; totals are sums of the lines, never recalculated from a total.
- **FR-004**: A Draft MUST allow one optional header discount, entered as a **fixed amount in SAR**
  (not a percentage). An empty discount means no discount, and a discount of zero is the same as none.
- **FR-005**: A discount MUST NOT be negative, MUST NOT be more than the invoice's total net, and MUST have at most two decimal
  places. Anything else MUST be refused with a message that names the discount and its limit, and nothing MUST be saved. A
  discount equal to the total net is allowed.
- **FR-006**: The discount MUST reduce the net **before** VAT is worked out, so VAT is charged on the discounted net.
  The discount is **shared over the lines in proportion to their net**; each line's VAT is worked out on its own discounted
  net, and the shares add up exactly to the discount (any left-over halala from rounding goes to the lines with the largest
  remainders, ties to the earlier line, so the same invoice always gives the same amounts).
- **FR-007**: The API MUST return these amounts, each with exactly two decimals in SAR: the **total net** (sum of line nets),
  the **discount** (0.00 when none), the **net after discount**, the **total VAT** (sum of line VATs) and the **grand total**
  (net after discount plus total VAT), and for every line its net, its share of the discount and its VAT. The invoice details
  MUST show the total net, total VAT and grand total and every line's net and VAT; the discount, the net after discount and
  each line's discount share are shown when the invoice has a discount (they would all read 0.00 otherwise).
- **FR-008**: The amounts MUST be available straight after a Draft is saved and whenever any invoice is opened, and MUST always
  equal what these rules produce from the invoice's saved lines and its saved discount. An invoice made in 005, before this
  feature, MUST show its amounts (with no discount) without needing to be edited.
- **FR-009**: The amounts MUST be built only from each line's saved quantity, saved unit price and saved VAT percentage and from
  the discount. The product's current price, VAT percentage, name or active state MUST NOT influence any amount of an existing
  line. Editing a line's quantity MUST NOT refresh its saved price or VAT. (This restates 005's snapshot rule for amounts.)
- **FR-010**: Every amount MUST be an exact decimal in Saudi riyals with two decimals; no amount may be held or calculated as an
  approximate number at any layer (Constitution Principle IV). The screens MUST display the amounts they are given and MUST NOT
  calculate any amount themselves.
- **FR-011**: If any line net or any invoice amount would be larger than 9,999,999,999,999.99 SAR, the save MUST be refused with a
  clear message and MUST save and change nothing.
- **FR-012**: A save that is refused for any reason (including a bad discount) MUST leave the invoice exactly as it was, MUST
  keep every value the person entered on the screen, and MUST name the field at fault, as in 005 (a value of the wrong JSON
  type, such as a discount sent as text, cannot be read at all and is a 400 without field names, as for a quantity; the screen
  never sends one). Refusals are structured error
  responses, never unhandled failures (Constitution Principle III).
- **FR-013**: The invoice details and the Draft form MUST let the person see and change the discount (create and edit), show
  the amounts on the details, and show loading and error states, and the save action MUST stay disabled while a save is in
  progress.
- **FR-014**: The repository MUST include automated tests covering: the acceptance example (2 x 100.00 at 15%: 200.00, 30.00,
  230.00); several lines with different VAT percentages; every rounding case in the Edge Cases (including 0.025 and 13.1875);
  zero-price lines; the discount rules (amount limits, negative, above net, equal to net, more than two decimals, cleared);
  discount sharing that adds up exactly; VAT on the discounted net; amounts unchanged after the product's price, VAT, name or
  active state changes; a quantity edit using the saved price; a new line using the current price; a Draft made in 005 showing
  amounts; the too-large refusal; and refusal of signed-out requests. The calculation MUST be proven with exact decimals, not by
  comparing approximate numbers.
- **FR-015**: This feature MUST NOT include issuing or sending invoices, any status other than Draft, payments, balances or
  remaining amounts, per-line discounts, several currencies, price or VAT changes on an existing line, invoice PDFs, email,
  credit notes, list columns, searching or filtering, and no shop, cart, payment gateway, support tickets or WhatsApp
  functionality (Constitution Principle I).

### Key Entities

- **Invoice** (from 005): now also carries an optional **discount** and its **amounts**: total net, discount, net after
  discount, total VAT and grand total.
- **Invoice line** (from 005): unchanged data (saved product name, SKU, unit, unit price, VAT percentage, quantity); it now also
  has calculated amounts: its net, its share of the discount and its VAT. The saved copy of price and VAT is the only input.
- **Discount**: an optional reduction of the whole invoice's net, entered by staff on a Draft, applied before VAT.
- **Product** (from 004) and **Customer** (from 003): not changed; only what was copied onto the lines is used.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the example (quantity 2, price 100.00, VAT 15%, no discount), 100% of saves show net 200.00, VAT 30.00 and
  grand total 230.00 SAR.
- **SC-002**: 100% of amounts shown on an invoice are exact to two decimals and add up: total net equals the sum of the line
  nets, total VAT equals the sum of the line VATs, and the grand total equals net after discount plus total VAT, to the last
  halala, for every combination of lines and discount in the tests.
- **SC-003**: 100% of invoices keep exactly the same amounts after the product's price, VAT percentage, name or active state is
  changed.
- **SC-004**: 100% of discounts that are negative, above the total net or have more than two decimals are refused with a
  message naming the discount, and 100% of valid discounts (including zero and equal to the net) are accepted.
- **SC-005**: A staff member can add a discount to an existing Draft and see the new grand total in under 1 minute.
- **SC-006**: Every invoice made before this feature shows correct amounts the first time it is opened, with no manual step.
- **SC-007**: 100% of amount operations attempted without a valid sign-in are refused.
- **SC-008**: All automated tests in the repository pass on the developer's machine, and the developer can, in a browser,
  create the ABC / `CNS-001` quantity 2 Draft and see 200.00, 30.00 and 230.00, add a discount, and see the amounts change.

## Assumptions

- 002 to 005 are complete and verified. Lines already carry a saved unit price (`decimal(18,2)`), VAT percentage and a
  quantity with at most two decimals (005).
- Amounts are Saudi riyals only, with no currency field. Rounding is to two decimals, halves away from zero (the common
  commercial rule), once per line for net and once per line for VAT.
- The rules are the project's `invoice-money` rules: line net = quantity x unit price, line VAT = Round(net x VAT percent / 100,
  2), the header discount comes off the net first and VAT is charged on the discounted net, grand total to two decimals in SAR.
  Because the net used for VAT is each line's rounded net, what is shown on a line is what is added up.
- The amounts are worked out by the system when an invoice is saved or opened and are always consistent with the saved lines and
  discount; whether they are also stored is a planning decision that must not change any result. Drafts from 005 need no
  manual step.
- The Draft form shows no live amounts while typing (the screens do not calculate); the amounts appear on the details after the
  save. A live preview is a later improvement.
- Both Admin and Agent may enter discounts, as they may already edit Drafts (005).
- The invoice list is unchanged (no total column); showing the grand total in the list comes with the list work of a later spec.
- Amounts and percentages follow the same input style as products (text-checked, refused rather than rounded when a person
  types more than two decimals).
- Payments, balances, issuing and any status other than Draft come with later specs; this spec only makes the Draft's amounts
  right.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole product
  (Constitution Principle I).
