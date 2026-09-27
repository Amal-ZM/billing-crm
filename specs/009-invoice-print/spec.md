# Feature Specification: Invoice Print

**Feature Branch**: `009-invoice-print`

**Created**: 2026-09-26

**Status**: Done

**Depends on**: 006-invoice-totals (verified running): the amounts an invoice shows; 007-invoice-status and 008-payments (verified running): the status an invoice can have (Draft, Sent, Overdue, Paid, Cancelled) and the dates it records; 002-staff-auth (signed-in staff)

**Input**: User description: "Feature 009-invoice-print. Depends on 006. Printable HTML page: title BillingCRM, customer, number, dates, lines, totals, status. Browser print is enough. Given INV-000123, When I open Print, Then totals match detail."

## Clarifications

### Session 2026-09-26

- Q: Should the printed invoice show what has been paid, or only the invoice's own amounts? → A: Only the invoice's own amounts and its
  status: no paid or remaining lines and no list of payments (a Paid invoice simply says Paid).
- Q: How much of the customer should the printed invoice show? → A: The customer's name only, exactly as the invoice's details show it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open a printable invoice and print it (Priority: P1)

A staff member looking at an invoice clicks **Print**. A page opens that shows the invoice as a clean document: the title
**BillingCRM**, the customer, the invoice number, its dates, every line, the totals and the status, with none of the application's
menus or buttons around it. Using the browser's own print command (or Ctrl+P) prints exactly that document on paper, or saves it as a
file through the browser's print-to-file option.

**Why this priority**: This is the whole feature. Staff need something they can hand to a customer or file, and the details screen is
built for working, not for paper.

**Independent Test**: Create an invoice for ABC with `CNS-001` quantity 2 (grand total 230.00), open it, click **Print**, and confirm the
page shows the title, customer, number, dates, the line, the totals and the status, and that the browser's print preview shows only the
document.

**Acceptance Scenarios**:

1. **Given** an invoice such as INV-000123, **When** I click **Print** on its details, **Then** a printable page for that invoice opens.
2. **Given** the printable page, **When** I look at it, **Then** I see the title **BillingCRM**, the customer's name, the invoice number, its
   dates, every line, the totals and the status.
3. **Given** the printable page, **When** I use the browser's print command, **Then** the print preview contains the document only: no
   toolbar, no navigation, no buttons and no back link.
4. **Given** the printable page, **When** I want to print, **Then** there is a clear **Print** button on the screen (which does not appear on
   paper) that opens the browser's print dialog, and a way back to the invoice.
5. **Given** an invoice with a long list of lines, **When** it is printed, **Then** the lines continue onto further pages without a line being
   cut in half, the column headings appear again at the top of each page, and the totals are not left alone at the top of a page apart
   from the lines that lead to them.
6. **Given** I am signed out or my session has expired, **When** I try to open a printable page, **Then** I am sent to sign in and see no
   invoice.
7. **Given** an invoice that does not exist, **When** I open its printable address, **Then** I see a clear "invoice not found" message, and
   the page is loading, shows an error with a way to retry, or shows the invoice, as the other screens do.

---

### User Story 2 - The printout says what the details say (Priority: P1)

Every figure on the printed invoice is the figure the invoice's details screen shows: the same totals, the same line amounts, the same
status. Nothing is worked out again for the printout, so it can never disagree with the screen or with what was sent to the customer.

**Why this priority**: A printed invoice that differs by a halala from the invoice on screen is worse than none. The request states it
directly: "totals match detail".

**Independent Test**: For an invoice with two lines at different VAT percentages and a discount, open the details and the printable page
and compare the total net, the discount, the net after discount, the total VAT and the grand total, every line's net and VAT, and the
status, digit for digit.

**Acceptance Scenarios**:

1. **Given** INV-000123, **When** I open Print, **Then** its totals (total net, discount, net after discount, total VAT, grand total) are
   exactly those on the details.
2. **Given** an invoice with a discount, **When** I print it, **Then** the discount and the net after discount are shown and every line's
   discount share matches the details.
3. **Given** an invoice with no discount, **When** I print it, **Then** no discount rows appear (as on the details).
4. **Given** a Sent invoice that is past its due date, **When** I print it, **Then** its status reads **Overdue**, as on the details; for a
   Draft, Sent, Paid or Cancelled invoice the status reads exactly that.
5. **Given** an invoice whose product was later repriced or deactivated, or whose customer was later renamed, **When** I print it, **Then**
   the lines and amounts are still those saved on the invoice (as on the details) and the customer's name is the one the details show.
6. **Given** a Cancelled invoice, **When** I print it, **Then** it says Cancelled and shows the date it was cancelled, so it cannot be mistaken
   for a live invoice; a Draft says Draft, so it cannot be mistaken for an issued one.
7. **Given** names, notes and product names in Arabic, English or a mix, **When** I print, **Then** they appear as written and read in the
   right direction, and amounts and numbers stay left-to-right.

---

### Edge Cases

- **Any status can be printed** (Draft, Sent, Overdue, Paid, Cancelled): the status printed on the page is what tells the reader which it
  is. There is no watermark and no restriction by status in this spec.
- **A very long invoice** (up to the system's limit of 100 lines) prints over several pages as described in User Story 1; a short one fits
  on one page.
- **Very large or very small amounts** (up to 9,999,999,999,999.99 and as small as 0.01) print in full on the page width without being cut
  or wrapped in the middle of a number.
- **A long note or a long product name** wraps inside its column; it never runs off the page or over another column.
- **No notes**: the notes area does not appear at all (no empty heading).
- **The invoice changes while the page is open** (another person sends or cancels it): the page shows what was loaded; opening it again
  shows the current invoice. Nothing on the printable page can change an invoice.
- **A customer that has become inactive** is still printed by name (as on the details); no "Inactive" mark is printed.
- **Paid and remaining**: the printout does not show what has been paid or what remains, and does not list payments (see the
  Clarifications); a Paid invoice says Paid and still shows its full totals.
- **Printing is not recorded**: opening or printing the page changes nothing, keeps no history and does not change the invoice's status
  or "last changed" time.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The invoice's details MUST offer a **Print** action for every invoice, whatever its status, to any signed-in staff member
  (Admin or Agent). It opens a printable page for that invoice.
- **FR-002**: The printable page MUST show, and nothing else of substance: the title **BillingCRM**; the **customer's name**; the invoice
  **number**; the invoice's **dates** (created, due, and, when they exist, sent and cancelled); the invoice's **notes** when it has any;
  **every line**; the **totals**; and the **status**.
- **FR-003**: Each line MUST show the product name, SKU, unit, unit price in SAR, VAT percent, quantity, net amount and VAT amount, and
  the discount share when the invoice has a discount, exactly as saved on the invoice and in the order of the details.
- **FR-004**: The totals MUST show total net, total VAT and grand total in SAR with two decimals, and, only when the invoice has a discount,
  the discount and the net after discount, in the same order and with the same wording as the details.
- **FR-005**: Every amount, and the status, on the printable page MUST be exactly the value the details screen shows for the same invoice,
  taken from the same source. The page MUST NOT calculate, round or re-derive any amount, VAT or total, or any status (Constitution IV;
  the `invoice-money` rules).
- **FR-006**: The status MUST be shown as one of **Draft**, **Sent**, **Overdue**, **Paid** or **Cancelled**, with Overdue shown for a
  Sent invoice whose due date has passed, exactly as on the details.
- **FR-007**: Dates MUST be shown as calendar dates in the same form as the details show them.
- **FR-008**: The printable page MUST, on screen, show a **Print** button that opens the browser's print dialog, and a way back to the
  invoice. These two, and everything else of the application (its toolbar, menus, links and buttons), MUST NOT appear in the printed
  output. Opening the page MUST NOT open the print dialog by itself.
- **FR-009**: When printed, the document MUST fit the width of a standard portrait A4 page, use black text on a white background (no
  reliance on background colours or images), keep a line together on one page, repeat the column headings on each page, and keep the
  totals together with the last lines.
- **FR-010**: The page MUST have a clear loading state, a not-found message for an invoice that does not exist (or an address that is not
  even an invoice), and an error message with a **Retry** for any other failure, as the other invoice screens do. A request without a
  valid sign-in MUST be refused and the person sent to sign in (Constitution Principle III); no invoice data is shown to anyone who is
  not signed in.
- **FR-011**: Opening or printing the page MUST NOT change anything: no status change, no "last changed" time, no stored record of
  printing.
- **FR-012**: The printable page MUST NOT show payments, the amount paid or the amount remaining, the customer's contact details, address
  or VAT number, internal identifiers (invoice or customer ids), the customer's active or inactive state, or who created or changed
  the invoice.
- **FR-013**: Names and notes in Arabic, English or a mix MUST be shown as written and read in the correct direction; amounts, numbers,
  SKUs and dates MUST stay left-to-right.
- **FR-014**: The repository MUST include automated tests covering: the Print action on the details for each status and role; the
  printable page showing each required item (title, customer, number, dates, notes when present and absent, every line with all its
  fields, totals with and without a discount, status including Overdue); **that every amount and the status match the details for the
  same invoice** (using figures that would differ if anything were recalculated); that nothing is calculated in the page; that the
  on-screen-only controls are marked so they do not print and the application's own navigation does not print; loading, not-found,
  error-with-retry and signed-out states; and that opening the page makes no request that changes anything.
- **FR-015**: This feature MUST NOT include producing or storing a PDF or any other file, emailing or sending the invoice, a company
  letterhead, logo, seller name, address or VAT registration, tax-invoice or e-invoicing (QR code) compliance, translating or choosing
  a language, choosing a template or paper size, printing several invoices at once, a printable invoice list or report, payments on
  the printout, a print history, or a watermark; and no shop, cart, payment gateway, support tickets or WhatsApp functionality
  (Constitution Principle I).

### Key Entities

- **Printable invoice**: a view of one existing invoice laid out for paper. It owns no data of its own and stores nothing: everything on it
  is the invoice as the details show it (number, customer name, dates, notes, lines, totals, status).
- **Invoice, customer, product, payment**: unchanged by this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A staff member can go from an invoice's details to the browser's print preview in two actions (**Print**, then the
  browser's print command or the page's **Print** button), in under 10 seconds.
- **SC-002**: For 100% of invoices, every amount and the status on the printable page equal the details' figures digit for digit, including
  invoices with a discount, lines at different VAT percentages, a late Sent invoice and a Cancelled invoice.
- **SC-003**: 0% of the application's own menus, toolbars, links and buttons appear in the printed output.
- **SC-004**: An invoice with 100 lines prints across pages with no line split in two and the column headings repeated on every page.
- **SC-005**: 100% of requests for the printable page without a valid sign-in are refused, and nothing about an invoice is shown.
- **SC-006**: Opening or printing an invoice never changes it (its status and "last changed" time are the same before and after).
- **SC-007**: All automated tests in the repository pass on the developer's machine, and the developer can, in a browser, open Print for
  INV-000123, compare its totals with the details, and see the browser's print preview show only the document.

## Assumptions

- 002 to 008 are complete and verified. The invoice's details already return everything the printout needs (customer name, dates, notes,
  lines, totals, status), so this spec adds no new stored data and no new way to read an invoice.
- **"Browser print is enough"**: the page is made to print well with the browser's own print command; there is no PDF, no download and no
  print button that does more than open the browser's print dialog. Saving as a file is whatever the browser offers in its print dialog.
- **Who may print**: any signed-in Admin or Agent, for any invoice in any status, as they may already read every invoice. There is no
  extra permission and no restriction by status.
- **The title is the application's name**, BillingCRM, as requested. There is no seller identity on the page (no company name, address,
  logo or VAT registration number): that would need settings the system does not have, and is left to a later spec.
- **Customer**: the name only, as the details show it (Clarifications).
- **Dates shown**: the invoice's created date and due date always; the sent date once it has been sent; the cancelled date once it has
  been cancelled. The same forms of date the details use. The payment dates are not shown (Clarifications).
- **Status wording**: the same five words as the details, with Overdue derived exactly as the details derive it (the API's answer, never
  worked out by the page).
- **Notes**: printed when the invoice has any, as written, keeping its line breaks.
- **Page**: portrait A4 is the design target; other paper sizes are whatever the browser scales to. The page does not choose the
  paper size or the print settings.
- **Language**: the labels are in English, as the rest of the application; names and notes in any language are shown as written.
- **Amounts** are SAR with two decimals, as elsewhere; the printable page uses the same values and formatting as the details and
  performs no arithmetic.
- The printable page is one more screen of the signed-in application: it is reached from the details, uses the same sign-in, and
  handles an expired session as the other screens do.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole product (Constitution
  Principle I).
