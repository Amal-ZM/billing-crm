# Research: Invoice Print

Every decision below was made from the spec, the constitution and the code as it stands after 008. No `NEEDS CLARIFICATION` remains (the
two open questions were answered in the spec's Clarifications: totals only with no payments, and the customer's name only).

## R1. Where the page lives: a top-level route outside the shell

**Decision**: `/invoices/:id/print` is a top-level route in `app.routes.ts`, a sibling of `login` and of the shell route, lazy-loaded and
guarded by `authGuard` (`canActivate`). It is declared before the shell's catch-all children so it is matched first (the shell route
has `path: ''`, and the wildcard is last). The shell (`ShellComponent`: the toolbar with the title, the menus, the user and Sign out) is
therefore never created for this page: there is nothing of the application's chrome to hide.

**Rationale**: the spec's strongest print requirement is SC-003, "0% of the application's own menus, toolbars, links and buttons appear in
the printed output". The most reliable way to meet it is for them not to exist in the page. Hiding them with `@media print` rules aimed
at another component's markup would depend on that component's class names and would silently break if the shell changed. The guard is
the same one every protected screen uses, so a signed-out or expired visitor is sent to `/login` with `returnUrl` and comes back to the
print page afterwards. The API still enforces sign-in on the one call the page makes.

**Alternatives considered**: a child of the shell with global `@media print { .app-toolbar { display: none } }` (couples this feature to the
shell's markup, and the shell's padding and layout would still print); a route data flag that makes the shell hide itself (a new
mechanism inside a shared component for one screen); rendering the document inside a hidden iframe or a new window (a new tab or window
does not share `sessionStorage`, see R5, and it needs a second copy of the data).

## R2. How "Print" is reached: a plain link in the same tab

**Decision**: the details screen gets one more action, **Print**, an `<a mat-button>` with `routerLink` to `/invoices/:id/print`, shown for
every invoice whatever its status and for every signed-in role (`data-testid="print"`). It navigates in the same tab. The print page has
a way back to the invoice (`/invoices/:id`).

**Rationale**: FR-001 says every status and every role. The session is kept in `sessionStorage` (per tab): a link with `target="_blank"`
would open a tab with no session and send the person to sign in. No condition or flag is needed: any invoice the details can show can be
printed, so the link needs no `allowedTransitions` or role check.

**Alternatives considered**: opening a new tab (loses the session); a Print button on the list rows (not asked; the list stays as it is);
showing Print only for Sent and Paid invoices (the spec allows every status, and the printed status word says which it is).

## R3. Print rules: one component stylesheet without encapsulation

**Decision**: `InvoicePrintComponent` declares `encapsulation: ViewEncapsulation.None` and puts all its rules under the single class
`.invoice-print`, including `@media print` rules and an `@page { margin: 15mm; }` rule. Angular removes a component's styles when the
component is destroyed, so the page margin rule exists only while this page is on screen and never affects another screen that is
printed. The print rules are:

- **Chrome**: elements with the class `no-print` (the on-screen bar holding the Print button and the back link) are `display: none`.
- **Colour**: `color: #000; background: #fff` for the document; no rule relies on a background colour or image (browsers drop them by
  default), so the status is a word, never a coloured badge.
- **Width**: the document is a single column that fills the page width (`max-width: 100%` in print, a readable maximum on screen);
  tables use `table-layout: auto` with `overflow-wrap: anywhere` on text cells so a long name or note wraps inside its column and never
  runs off the page; amounts use `white-space: nowrap` so a number is never split.
- **Pagination**: `thead { display: table-header-group }` (the column headings repeat on each page), `tr { break-inside: avoid }`
  (a line is not cut in half), and the totals block and its heading `break-inside: avoid` and `break-before: avoid` after the table
  (so the totals stay with the lines that lead to them).
- **Direction**: names, notes and product names carry `dir="auto"`; amounts, numbers, SKUs and dates carry `dir="ltr"`, as on the details.

**Rationale**: encapsulated component styles (`_ngcontent` attributes) cannot express `@page`, and a global `@page` in `styles.scss` would
change the margin of every screen that anyone prints. A scoped, un-encapsulated stylesheet is the smallest arrangement that keeps the
rules next to the markup they describe and limited to this page. Paper size is deliberately not set (`size` is left to the browser and
the person; A4 portrait is the design target, per the spec).

**Alternatives considered**: putting the print rules in `styles.scss` (global side effects, far from the markup); inline `style`
attributes (cannot express media queries); a separate print stylesheet file (a build-configuration change for one screen); relying on
Angular Material's card (its shadows, padding and typography are screen styles, and would need overriding for print).

## R4. "Totals match detail": same source, same formatting, proved by a parity test

**Decision**: the page reads the invoice with `InvoiceService.get(id)` and renders each figure with the same field path and the same pipe
as `InvoiceDetailComponent`: `totals.totalNet`, `totals.discount`, `totals.netAfterDiscount`, `totals.totalVat`, `totals.grandTotal` and
each line's `unitPrice`, `vatPercent`, `quantity`, `net`, `discountShare`, `vat` with `number: '1.2-2'` (`'1.0-2'` for the percent),
the due date with `date: 'mediumDate'` and the created, sent and cancelled times with `date: 'medium'`, the status as
`invoice.isOverdue ? 'Overdue' : invoice.status`. The discount rows appear only when `totals.discount !== 0`, and the discount column
only then, as on the details. The page uses the **same `data-testid`s** as the details for the same things (`number`, `customer`,
`due-date`, `created-at`, `sent-at`, `cancelled-at`, `notes`, `status-badge`, `line-row`, `total-net`, `discount`, `net-after-discount`,
`total-vat`, `grand-total`). A spec, `invoice-print-parity.spec.ts`, builds one invoice whose figures would differ if anything were
recalculated (the grand total is not the sum of the parts; a line's VAT is not net times percent), renders the details component and the
print component from it, and asserts that, for every one of those test ids, the text is equal after collapsing whitespace. It runs for a
Draft, a Sent, a late Sent (Overdue), a Paid and a Cancelled invoice, and with and without a discount.

**Rationale**: SC-002 and FR-005 are about equality with the details, so the test compares the two directly instead of comparing each to a
hand-written expected string; if the details' formatting changes, the print page must change with it or the test fails. No helper is
extracted for the one-line status expression: it is already written that way in the details and the list, and extracting it would be a
new shared abstraction for one expression (Principle V) and would touch two working screens.

**Alternatives considered**: a shared "invoice figures" component used by both screens (a new shared abstraction and a refactor of the
details, whose layout differs on purpose: screen cards versus a paper document); a shared formatting function (as above); comparing to
literals only (proves the page against a fixture, not against the details).

## R5. The Print button and the browser

**Decision**: the on-screen bar has a **Print** button (`data-testid="print-button"`) whose click handler calls
`document.defaultView?.print()`, with the `DOCUMENT` token injected so a test can replace it, and a **Back to invoice** link
(`data-testid="back"`). Both are inside one element with the class `no-print`. Opening the page never calls `print()` by itself (FR-008).
The person can equally use Ctrl+P or the browser menu: the printed output is the same, because it is the same page.

**Rationale**: "browser print is enough". A button gives the discoverable path the spec asks for, and calling `window.print()` is all the
browser needs. Not printing automatically avoids a dialog appearing before the person has looked at the page (and in some browsers a
programmatic print on load is blocked or shows a blank page while the data is still loading).

**Alternatives considered**: calling `print()` automatically when the invoice has loaded (surprising; racing the render); waiting for
`afterprint` events to navigate back (fragile across browsers; the person may want to print twice).

## R6. No backend, no data, no history

**Decision**: the only request is the existing `GET /api/invoices/{id}`. There is no print endpoint, no "printed" flag or time, no
counter and no audit line. The page changes nothing on the invoice, including its `updatedAtUtc` (FR-011). The API, its tests and the
database are not changed; the three backend suites run unchanged as a guard.

**Rationale**: FR-011 and FR-015 (no history). The details response already carries everything FR-002 needs. Reading the customer
record for an address or VAT number was declined in the Clarifications, so no second call is needed and nothing can differ from the details.

**Alternatives considered**: a server-rendered HTML or PDF endpoint (out of scope: no file output; a second implementation of every
figure that could disagree); recording prints (out of scope).

## R7. What is on the page, in order

**Decision** (contract in [contracts/print-page.md](contracts/print-page.md)): an on-screen bar (Print, Back to invoice; not printed); then
the document: a header with the title **BillingCRM** as the page heading and the invoice number as the sub-heading; the status as a
word beside the number; a facts list (Customer, Created, Due date, and Sent / Cancelled when present); the notes when present; the lines
table; the totals block. Plain text, no card, no badge colours. The page's `<title>` is left as the application's (`BillingCRM`).

**Rationale**: FR-002 to FR-004, FR-006, FR-007 and FR-012. Leaving out ids, contact details, the active flag, payments, paid and remaining
follows FR-012 exactly (the fields are simply not rendered).

## R8. States and sign-in

**Decision**: the same four states as the details (`loading`, `loaded`, `notFound`, `error` with a Retry), reusing the details' wording
for the three non-loaded ones: "Loading invoice…", "Invoice not found" and "The invoice could not be loaded." with the API's detail when
there is one and a **Retry**. A malformed or unknown id is a 404 from the API and shows the not-found message. The states show only the
on-screen bar's Back link, never a Print button, since there is nothing to print. A signed-out visitor never reaches the component:
`authGuard` redirects to `/login` (with `returnUrl`), and if the session ends while the page is open, the interceptor's 401 handling
signs the person out as it does everywhere.

**Rationale**: FR-010 and the "as the other screens do" wording. Reusing the wording keeps tests and the person's expectations aligned.

## R9. Test approach

- **Print component spec**: title `BillingCRM` (as the page heading); number; customer name; created, due, sent and cancelled dates for
  each status (sent and cancelled only when present); notes present (line breaks kept) and absent (no heading at all); every line with
  product name, SKU, unit, unit price, VAT percent, quantity, net and VAT amount, and the discount share only with a discount; totals with
  and without a discount; each status word including Overdue; Arabic and English text kept and `dir` set (`auto` for names and notes,
  `ltr` for numbers, dates and SKUs); nothing shown that FR-012 forbids (no payments, paid, remaining, ids, customer active flag, contact
  details); no arithmetic (a fixture whose totals are not the sum of their parts, and a line whose VAT is not net times percent, are
  shown exactly as given); the on-screen bar carries `no-print` and holds the only button and link; the Print button calls the injected
  `window.print` exactly once and opening the page calls it zero times; loading, not found, error and Retry; the only service call is
  `get` (the stub has no other method, so any write would throw); text with markup in it (`<b>` in a note) appears as text, not as HTML.
- **Parity spec**: as R4.
- **Details spec**: the Print link for each status and both roles, pointing at `/invoices/<id>/print`, and no `target`.
- **Route spec**: the print route is a top-level route (not a child of the shell route), lazy, and has `authGuard` in `canActivate`.
- **Guard**: the existing `authGuard` specs already cover signed-out and expired sessions; the route spec proves the print route uses it.
- **Manual (quickstart)**: the browser's print preview shows only the document, repeated headings on a 40-line invoice, totals kept with
  the last lines, nothing cut off with a very large amount.

## R10. Docs and skills

`README.md` gains the Print action and what the page shows and omits; `CLAUDE.md` gains an "Invoice print (spec 009)" bullet and the layout
line (`invoice-print`). The `invoice-money` skill needs no change (no money rule changes; the page calculates nothing). These are listed
tasks, as for 007 and 008.

## Known gaps

- No PDF or stored copy; no seller identity or e-invoicing fields; no payments on the printout (see plan.md).
- The browser's own print header and footer cannot be controlled by the page.
- Paper size and orientation are the browser's; the layout targets portrait A4 and reflows for others.

## Confirmations (task T001, Constitution V) — 2026-09-26

The developer confirmed all four groups of new patterns from plan.md, as recommended:

1. The print page is a top-level route outside the signed-in shell (`/invoices/:id/print`), behind the same `authGuard`.
2. The print styles live in the new component with no style encapsulation, every rule scoped under `.invoice-print`.
3. "Totals match detail" is guaranteed by the details' own field paths, pipes and test ids, no shared helper or component, and one parity spec comparing both screens.
4. Print opens in the same tab (a plain link), never prints by itself, and has an on-screen Print button; no backend, endpoint, printed flag or history.

## Review disposition (2026-09-26)

Four read-only reviews ran after implementation (`angular-reviewer`, `security-reviewer`, `spec-auditor`, `api-reviewer`). None found a Blocker
or a Critical defect. One High (CSS) and several Medium items were real and are fixed; everything else is fixed, or recorded here with the
reason. The browser print preview (T022) is the developer's run and is still to be done.

### angular-reviewer (no blockers; one High)

| Finding | Disposition |
|---------|-------------|
| H1: the amount columns were left-aligned (the cell rule `table.lines th, td { text-align: start }` was more specific than `.amount { text-align: end }`) | **Fixed**: the amount rule now uses the table's own selector (`table.lines th.amount, table.lines td.amount`); a test pins the selector and forbids a bare `.amount` rule. (jsdom does not apply CSS specificity, so a computed-style test would have hidden this; the check reads the stylesheet instead) |
| M1: the status was drawn in capitals (`text-transform`), so paper did not say what the details say | **Fixed**: the transform is removed; a test forbids `text-transform` |
| M2: fixtures were too consistent to catch recalculation (net after discount equal to net minus discount, shares equal to the discount, top-level `discount` equal to `totals.discount`) | **Fixed**: the discount fixtures are deliberately inconsistent (77.79, shares 20.01 + 13.30, top-level 40.00 against 33.33); tests assert those exact figures and that 40.00 and 77.78 never appear |
| M3: parity passed silently if an id was missing from both screens | **Fixed**: the parity spec asserts that eight core ids exist on both screens; it also compares text direction of the figures and of every line cell, and the notes with their line breaks |
| M4: the print CSS tests were string searches | **Fixed**: the print block is extracted by brace matching; the bar is hidden inside `@media print` only (and with `!important`, so a flex row cannot win), the pagination rules are checked on their elements, colour and width rules inside the print block, every rule scoped under `.invoice-print`, no `text-transform`; and a test proves the stylesheet and `@page` are **removed when the page is destroyed** |
| M5: very large amounts likely overflow A4 (many `nowrap` columns) | **Mitigated, recorded**: a smaller, tighter table in print, and a scroll wrapper on screen. A line with several ~24-character amounts is still wider than the page, so it relies on the browser scaling it to fit; this is a known gap in plan.md and a manual step in the quickstart. Realistic invoices fit |
| L1: the Print button test spied on `window.print`, was not exception-safe, and could not see an auto-print after a slow load | **Fixed**: `try/finally`, and a test that emits the invoice late and still expects no print |
| L2: vacuous tests (own stub, an arbitrary storage key, a trivially true title check) | **Fixed**: removed or replaced by real ones (nothing written to storage, the title unchanged) |
| L3: Retry and the links of the not-found and error states could print | **Fixed**: they sit in `.no-print`; a test checks every control of both states |
| L4: dates had no `dir="ltr"` although FR-013 says they should | **Fixed** on the printout (the details keep theirs, so dates are not compared for direction in parity); test |
| L5: the status had no label; focus after Retry; plain controls | **Partly fixed**: a "Status" label (visible and for screen readers). Focus management and Material-styled buttons are **recorded, not done** (the controls are deliberately plain: the page is a document) |
| L6, L7 | **Recorded**: the title stays `BillingCRM` by design (R7); the totals border gap and `html, body` height are cosmetic and covered by the manual preview |

### security-reviewer (no Critical, High or Medium)

| Finding | Disposition |
|---------|-------------|
| L1: the browser's print footer prints the internal address (invoice id and host name) | **Documented**: README, quickstart (a step) and plan.md say to untick "Headers and footers" for a copy that leaves the company; the page cannot remove the browser's header and footer |
| L2: no anti-framing header anywhere (app-wide) | **Deferred to 012** (`frame-ancestors 'none'` / `X-Frame-Options` on the host that serves the app) |
| L3: no Sign out on the print page | **Accepted**: deliberate, for a clean printout; Back to invoice leads to the shell |
| L4: no `Cache-Control: no-store` on authenticated responses or the document (bfcache) | **Deferred to 012** |
| L5: the page read the id once, so a link between two print pages would show the old invoice | **Fixed**: the page follows the address (`paramMap`), numbers its requests so a slow older answer cannot replace a newer one; test |
| L6: bidi and control characters in printed free text | **Deferred to 012** (already listed; it matters more on paper) |

### spec-auditor (no Critical; one High that was a runnable-step defect)

| Finding | Disposition |
|---------|-------------|
| High: the quickstart's very-large-amount step could not be created (with the default 15% VAT the total exceeds the system limit and the save is refused) | **Fixed**: quantity 8000, with the reason stated |
| M1 dates without `dir`; M2 capitals; M3 nowrap overflow; M4 weak CSS tests; M5 tautological tests | **Fixed or recorded** as above |
| Low: plan.md said Material buttons and named `app.routes.spec.ts`; a stray "(US: ...)" in the quickstart | **Fixed** |
| No automated check that "last changed" is unchanged (FR-011 / SC-006) | **Recorded**: the page can only read (its stub has no other method), and the API's `GET` is read-only; the quickstart checks it in a browser |
| A malformed id has no component-level test | **Recorded**: the API turns it into a 404, which the page tests |

### api-reviewer (no backend change)

| Finding | Disposition |
|---------|-------------|
| No file under `backend/` relates to 009; the only call is the existing `GET`; every field the page reads is in the invoice response | **Confirmed** (also by the timestamp check in T019 and by the unchanged backend suites) |
| Medium (design): created, sent and cancelled times are shown in the viewer's browser time zone, while the system elsewhere uses the fixed Saudi UTC+03:00 date, so a printout made in another time zone or near midnight can show a different day | **For the developer**: both screens do it (parity holds and FR-007 says "same form as the details"); changing it means adding a time-zone argument to the details screen you have already verified as well. Not changed in this spec |
| Low: amounts arrive as JSON numbers and are formatted by the pipe; the response carries fields the page ignores | **Recorded**, no action |

### Verification (T024)

`ng test --watch=false`: 755 pass (was 637 before this spec). `ng build`: clean. The three backend suites are unchanged and pass:
Application 709, API 471, Infrastructure 222.

The developer ran specs/009-invoice-print/quickstart.md (T022) on 2026-09-26, including the browser print-preview checks, and confirmed 009 is running and verified.
