# Feature Specification: Create Draft Invoices

**Feature Branch**: `005-invoice-create`

**Created**: 2026-09-24

**Status**: Done

**Depends on**: 003-customers and 004-products (both verified running); 002-staff-auth for sign-in

**Input**: User description: "Feature 005-invoice-create. Depends on 003 and 004. Create Draft invoice: active customer, due date, notes, lines from active products, qty > 0. Number INV-000123. Line snapshots name, unit price, VAT percent. No invoice for inactive customer/product. Draft lines can be edited. Given ABC customer and CNS-001, When I add qty 2 and save, Then status Draft and one line qty 2. Use /invoice-money later for final totals; this spec must persist lines."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create a Draft invoice with lines (Priority: P1)

A staff member starts an invoice for an active customer, sets a due date and optional notes, adds one or more
lines by choosing active products and entering a quantity, and saves. The invoice is saved as a **Draft** with
the next invoice number (for example `INV-000123`), and each line keeps its own copy of the product's name,
unit price and VAT percentage as they were when the line was added.

**Why this priority**: This is the first time BillingCRM records what a customer is being billed for. Everything
after it (totals, issuing, payments) needs saved invoices with saved lines, so this is the smallest slice that
delivers value.

**Independent Test**: Sign in, create a customer "ABC" and a product `CNS-001` if they do not exist, start a new
invoice for ABC, add `CNS-001` with quantity 2, save, and confirm the invoice is a Draft with a number and exactly
one line of quantity 2; then change the product's price and confirm the saved line still shows the old price.

**Acceptance Scenarios**:

1. **Given** an active customer ABC and an active product `CNS-001`, **When** I create an invoice for ABC, add
   `CNS-001` with quantity 2 and save, **Then** the invoice is saved with status **Draft** and has exactly one
   line with quantity 2.
2. **Given** I saved my first invoice, **When** the invoice is created, **Then** it shows a number in the form
   `INV-` followed by six digits (for example `INV-000123`), and the next invoice I create gets the next number.
3. **Given** a line was added for `CNS-001` at price 100.00 and VAT 15%, **When** an Admin later changes the
   product's name, price or VAT percentage (or deactivates it), **Then** the saved line still shows the name, unit
   price and VAT percentage it had when it was added.
4. **Given** I am creating an invoice, **When** I choose a customer, **Then** only active customers can be chosen;
   an inactive customer is not offered, and if one is submitted anyway the save is refused with a clear message and
   nothing is created.
5. **Given** I am adding a line, **When** I choose a product, **Then** only active products can be chosen; an
   inactive product is not offered, and if one is submitted anyway the save is refused with a clear message and
   nothing is created.
6. **Given** I enter a quantity that is zero, negative, empty, not a number, or has more than two decimal places,
   **When** I save, **Then** I am told the quantity must be a number greater than zero and nothing is saved.
7. **Given** I leave out the customer or the due date, use a due date in the past, or add no lines, **When** I
   save, **Then** I am told which field is wrong and nothing is saved.
8. **Given** a save was refused for any reason, **When** the message appears, **Then** the customer, due date,
   notes and every line I entered are still on the screen so I can correct them.
9. **Given** I am signed out or my session has expired, **When** I try to create an invoice, **Then** I am sent to
   sign in and nothing is created.

---

### User Story 2 - Edit a Draft invoice and its lines (Priority: P2)

While an invoice is still a Draft, a staff member can change its due date and notes, change a line's quantity, add
more lines, and remove lines. Editing never changes the price, VAT percentage or name already saved on an
existing line, and a Draft always keeps at least one line.

**Why this priority**: Drafts exist so that mistakes can be fixed before an invoice is issued. It matters right
after creating drafts, but the invoice is already useful when it can only be created.

**Independent Test**: Open the Draft from story 1, change its quantity to 3, add a second product line, remove the
first line, change the due date and notes, save, and reopen it to confirm every change persisted and the remaining
line kept its original price snapshot.

**Acceptance Scenarios**:

1. **Given** a Draft invoice, **When** I change a line's quantity to a valid value and save, **Then** the new
   quantity is saved and the line's name, unit price and VAT percentage are unchanged.
2. **Given** a Draft invoice, **When** I add a line for an active product, **Then** the line is saved with that
   product's name, unit price and VAT percentage as they are at that moment.
3. **Given** a Draft invoice with two or more lines, **When** I remove a line and save, **Then** the line is gone
   and the other lines are unchanged.
4. **Given** a Draft invoice with exactly one line, **When** I try to remove it, **Then** the change is refused
   with a message that a Draft needs at least one line.
5. **Given** a Draft invoice, **When** I change its due date or notes and save, **Then** the changes are saved and
   the invoice number and status do not change.
6. **Given** a Draft whose product has become inactive since the line was added, **When** I open and edit the
   Draft, **Then** the existing line is still shown and can be kept, have its quantity changed, or be removed,
   but I cannot add a new line for that inactive product.
7. **Given** a Draft whose customer has become inactive since it was created, **When** I edit its due date, notes
   or lines, **Then** the edit is allowed, but choosing a different customer is only possible among active
   customers.
8. **Given** I change the customer of a Draft, **When** I pick an active customer and save, **Then** the customer
   is changed; picking an inactive customer is refused.
9. **Given** an invalid edit (bad quantity, past due date, inactive new product), **When** I save, **Then** it is
   refused with a message naming the problem, and the Draft is unchanged.
10. **Given** an invoice that does not exist, **When** I open or edit it (for example from an old link), **Then** I
    see a clear "invoice not found" message.

---

### User Story 3 - See my invoices and open one (Priority: P3)

A staff member can see a list of invoices, newest first, showing number, customer, due date and status, and open
any invoice to see its header (customer, due date, notes, status, when it was created and last changed) and all of
its lines with the saved name, unit, unit price, VAT percentage and quantity.

**Why this priority**: Staff need to find and re-open a Draft to edit it and to check what was saved, but list
searching and filtering can wait for a later spec.

**Independent Test**: Create three Drafts, open the invoice list and confirm they are listed newest first with
their numbers, customers, due dates and status; open one and confirm every saved line detail is shown.

**Acceptance Scenarios**:

1. **Given** several invoices exist, **When** I open the invoice list, **Then** they are listed newest first with
   number, customer name, due date and status, and split into pages with the total number shown.
2. **Given** no invoices exist, **When** I open the list, **Then** I see a clear "no invoices yet" message with a
   way to create one.
3. **Given** an invoice, **When** I open it, **Then** I see its number, status, customer, due date, notes,
   created and last-changed times, and every line with product name, SKU, unit, unit price in SAR, VAT percentage
   and quantity.
4. **Given** an invoice whose customer or product was later changed, renamed or deactivated, **When** I open it,
   **Then** the lines still show the values saved with them and the customer name is shown as it is now, with an
   "inactive" mark if the customer is inactive.
5. **Given** the list is loading, fails to load, or is empty, **When** I look at the screen, **Then** I see a
   loading, error-with-retry or empty state, never a blank screen.

---

### Edge Cases

- Two staff members create an invoice at the same moment: each gets a different number, and no number is ever
  given to two invoices.
- A save that is refused (validation, inactive customer or product) does not consume an invoice number in a way
  that produces a duplicate; a gap in the number sequence after a refused or failed save is acceptable, a
  duplicate is not.
- A product or customer becomes inactive while a person is filling in the form: the save is refused at that
  moment with the clear message, and the person's input stays on screen.
- A product's price or VAT percentage changes after a line was added: the line keeps its saved values; adding the
  same product again later gets the current values, so one Draft can hold two lines for the same product at
  different prices.
- The same product added twice: allowed as two separate lines, each with its own quantity.
- A quantity such as `1.5` (one and a half hours) is allowed; `0`, `-1`, `0.001` and `abc` are not.
- Very large quantities or too many lines are refused with a message naming the limit (see FR-007 and FR-008).
- Customer or product names written in Arabic, English or both are saved and shown exactly as they were.
- Notes containing line breaks are kept as typed.
- A product with price 0 can be added (a free item); the line is saved with price 0.
- A Draft edit made by two people at the same time: the last save wins (see Assumptions).
- Signed-out visitors, or someone whose session has expired, can see and change nothing (handled by 002).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Any signed-in staff member (Admin or Agent) MUST be able to create a Draft invoice, edit a Draft,
  list invoices and open an invoice. Anyone not signed in MUST be refused everything.
- **FR-002**: An invoice MUST have a customer, a due date, optional notes, a status, an invoice number, the times
  it was created and last changed, and one or more lines. A new invoice always has the status **Draft**. Draft is
  the only status this spec creates or handles.
- **FR-003**: The customer of a new invoice MUST be an **active** customer. Creating an invoice for an inactive
  customer, or for a customer that does not exist, MUST be refused with a clear message and MUST create nothing.
  The customer picker MUST offer active customers only.
- **FR-004**: Each line MUST refer to an **active** product and have a quantity. Adding a line for an inactive
  product, or for a product that does not exist, MUST be refused with a clear message and MUST save nothing. The
  product picker MUST offer active products only (as chosen by the price list in spec 004).
- **FR-005**: When a line is added, the system MUST save on the line its own copy of the product's name, SKU, unit,
  unit price (SAR) and VAT percentage as they are at that moment, together with a reference to the product. Later
  changes to the product (name, SKU, unit, price, VAT percentage or deactivation) MUST NOT change the saved line.
  Editing a line's quantity MUST NOT refresh its saved copy.
- **FR-006**: The invoice number MUST have the form `INV-` followed by exactly six digits, starting at
  `INV-000001` and going up by one for each invoice created (for example `INV-000123`). It MUST be assigned when
  the invoice is created, MUST be unique across all invoices, MUST never change and MUST never be reused. Two
  invoices created at the same moment MUST get different numbers.
- **FR-007**: A line's quantity MUST be a number greater than zero with at most two decimal places and at most
  999,999.99. Anything else (zero, negative, empty, not a number, more than two decimals, too large) MUST be
  refused with a message that names the quantity, and nothing MUST be saved or changed.
- **FR-008**: An invoice MUST have at least one and at most 100 lines. Saving with no lines, adding a 101st line,
  or removing the last remaining line of a Draft MUST be refused with a clear message.
- **FR-009**: The due date MUST be a valid calendar date that is not before the day the invoice is created
  (today). A missing, invalid or past due date MUST be refused with a message naming the due date. When editing a
  Draft, a due date that is left unchanged is always accepted, even if it has since passed.
- **FR-010**: Notes are optional, kept as typed (including line breaks and Arabic or English text) and limited to
  1,000 characters. Leading and trailing spaces are removed; empty notes mean "no notes".
- **FR-011**: While an invoice is a Draft, a staff member MUST be able to change its due date and notes, change its
  customer to another active customer, change a line's quantity, add lines for active products, and remove lines
  (never the last one). An existing line whose product has since become inactive MUST still be shown, kept, have
  its quantity changed or be removed, but a new line for that product MUST be refused. An edit that leaves the
  customer unchanged MUST be allowed even if that customer has since become inactive.
- **FR-012**: A save that is refused for any reason MUST leave the invoice exactly as it was and MUST keep every
  value the person entered on the screen. Refusals MUST name the field or line at fault.
- **FR-013**: The system MUST NOT calculate, show or store line totals, VAT amounts, discounts, invoice totals or
  balances in this spec. It MUST persist everything that later totals need: for every line the quantity, the unit
  price and the VAT percentage, all as exact decimals in Saudi riyals and never as approximate numbers
  (Constitution Principle IV). Totals are the job of a later spec that follows the `invoice-money` rules.
- **FR-014**: The invoice list MUST show invoices newest first (by creation, then number), with number, customer
  name, due date and status, split into pages of 20 by default (at most 100), reporting the total number of
  invoices and the current page. A page number beyond the last MUST give an empty page with the real total, not an
  error. Searching and filtering the list is out of scope.
- **FR-015**: Opening an invoice MUST show its header and every line with the saved product name, SKU, unit, unit
  price, VAT percentage and quantity, in the order the lines were added. The customer is shown by its current name
  with an inactive mark when applicable.
- **FR-016**: Opening or editing an invoice that does not exist MUST produce a clear "not found" outcome, not a
  crash or a blank screen.
- **FR-017**: Refusals (validation, inactive or missing customer or product, not found, not signed in) MUST be
  returned as structured error responses, not as unhandled failures (Constitution Principle III).
- **FR-018**: Customer and product names, and notes, in Arabic and English MUST be saved and shown exactly as
  entered.
- **FR-019**: The screens MUST show loading, empty and error states for the invoice list and the invoice details,
  and MUST disable the save action while a save is in progress so nothing is saved twice.
- **FR-020**: The repository MUST include automated tests covering: the acceptance scenario (customer ABC and
  `CNS-001` with quantity 2 saved as a Draft with one line of quantity 2); invoice numbers (format, first number,
  increasing, unique under simultaneous creation); the price/VAT/name snapshot surviving later product changes and
  deactivation; refusal for an inactive or missing customer and for an inactive or missing product, on create and
  on edit; every quantity rule; the due-date rules; the 1 to 100 line limits; notes limits; each Draft edit
  (quantity, add, remove, due date, notes, customer); the last-line rule; a Draft with a now-inactive product or
  customer; the list (order, paging, empty) and the details; not found; and refusal of signed-out requests.
- **FR-021**: This feature MUST NOT include issuing or sending invoices, any status other than Draft, cancelling or
  deleting an invoice, totals, VAT amounts, discounts, payments, balances, invoice PDFs, email, search or filters
  on the invoice list, invoice templates or credit notes, and no shop, cart, payment gateway, support tickets or
  WhatsApp functionality (Constitution Principle I).

### Key Entities

- **Invoice**: A bill being prepared for one customer. Has a unique number in the form `INV-` plus six digits, a
  status (only Draft in this spec), a customer, a due date, optional notes, the times it was created and last
  changed, and its lines. It is created as a Draft and can be edited while it is a Draft.
- **Invoice line**: One row of an invoice. Has a reference to the product it was made from, a saved copy of that
  product's name, SKU, unit, unit price (SAR) and VAT percentage taken when the line was added, and a quantity. The
  saved copy is what the invoice will always show, whatever later happens to the product.
- **Customer** (from spec 003) and **Product** (from spec 004): only their active state and their current values
  at the moment a line or invoice is created are used here; this spec changes neither.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A staff member can create a Draft invoice with one line in under 2 minutes from opening the invoice
  list, and it appears in the list with its number straight away.
- **SC-002**: 100% of attempts to create or edit an invoice for an inactive or missing customer, or with a line for
  an inactive or missing product, are refused with a clear message and change nothing.
- **SC-003**: 100% of saved lines show the name, unit price and VAT percentage they had when they were added, even
  after the product was renamed, repriced or deactivated.
- **SC-004**: 100% of invoice numbers are unique and in the form `INV-` plus six digits, including when several
  invoices are created at the same moment.
- **SC-005**: 100% of quantities that are zero, negative, empty, non-numeric, have more than two decimals or exceed
  the maximum are refused, and 100% of valid quantities (for example 2 and 1.5) are saved exactly as entered.
- **SC-006**: With 5,000 invoices stored, the first page of the invoice list appears in under 1 second.
- **SC-007**: 100% of invoice operations attempted without a valid sign-in are refused.
- **SC-008**: All automated tests in the repository pass on the developer's machine, and the developer can, in a
  browser, create a Draft for customer ABC with `CNS-001` quantity 2, edit it, and find it again in the list.

## Assumptions

- 002, 003 and 004 are complete and verified: staff sign in as Admin or Agent, customers can be active or
  inactive, and products have a name, SKU, unit, unit price, VAT percentage and an active state.
- **Who can do what:** invoices are day-to-day staff work, so both Admin and Agent may create and edit Drafts and
  view invoices. Nothing here is Admin-only. (If invoices should be restricted by role later, that is a change to
  this specification.)
- **"Draft lines can be edited"** is read as: change a line's quantity, add lines, remove lines, and also change
  the due date, notes and customer, all only while the invoice is a Draft. A Draft must keep at least one line.
- A line's saved copy also includes the product's SKU and unit (beside the name, unit price and VAT percentage
  named in the request), because staff recognise products by them; this adds no new rule.
- The invoice number counts up by one for every invoice ever created, starting at `INV-000001`, and gaps are
  acceptable (a failed save may use up a number) while duplicates are not. Six digits allow up to 999,999 invoices;
  what happens after that is outside this spec.
- The due date has no time of day, and "today" means the current calendar date in Saudi Arabia (Asia/Riyadh).
- Quantities may be fractional with two decimals (for example 1.5 hours), from 0.01 to 999,999.99. Unit prices
  and VAT percentages come from the product exactly as stored (decimal, SAR, VAT 0 to 100 with two decimals).
- The same product may appear on more than one line of the same invoice; lines are kept in the order they were
  added.
- When two people edit the same Draft at once, the last save wins, as with customers and products.
- The invoice list is a simple newest-first list; searching, filtering and status views come with later invoice
  specs. Customer names in the list and details are current names, not saved copies.
- Money is Saudi riyals only, with no currency field. Final totals, VAT amounts and rounding are calculated by a
  later spec following the `invoice-money` rules; this spec only guarantees the quantity, unit price and VAT
  percentage of every line are saved exactly.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole product
  (Constitution Principle I).
