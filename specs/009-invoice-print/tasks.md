---

description: "Task list for 009-invoice-print"
---

# Tasks: Invoice Print

**Input**: Design documents from `/specs/009-invoice-print/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/print-page.md, quickstart.md

**Tests**: Included. FR-014 requires automated tests, and the acceptance checks in contracts/print-page.md map to test tasks below. Write
each phase's tests first and confirm they fail (a test that does not compile yet counts as failing).

**Organization**: Grouped by user story (US1 open the printable invoice and print it, US2 the printout says what the details say). This is
a **front-end-only** spec: no API, database, migration or package changes. The route, the component skeleton with its load states and the
route test are Foundational because both stories need them. The document markup, the on-screen controls and the print rules are **US1**;
because the page is built from the details' own field paths, pipes and test ids, **US2 proves the match** (a parity test that renders the
details and the print page from one invoice, and fixtures whose figures would differ if anything were calculated) and adds the checks for
every status, the discount rows, direction and long content.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`frontend/src/app/` as in plan.md. All commands run from the repository root unless a task says otherwise (`cd
/c/Users/somam/source/repos/billing-crm` first in Bash: the shell's directory drifts); run frontend commands from `frontend/`
(`npx --yes npm@11 exec -- ng test --watch=false`). Stop the API and `ng serve` before `dotnet build` or `dotnet test`; **do not start them
again unless the developer asks**. Edit code files with the Edit and Write tools (shell string replacement and long heredocs have
mangled code and quotes before).

**Rules that apply to every task**: use the `angular-ui-conventions` and `invoice-money` skills; the page shows the API's amounts and
status and **never calculates, rounds, converts or derives** any of them (the only comparison allowed is `totals.discount !== 0`, whether
to show the discount rows, exactly as the details do); money is formatted only with the details' pipes (`number: '1.2-2'`, `'1.0-2'` for a
percent, `date: 'mediumDate'` / `'medium'`); the page issues **one `GET`** (`InvoiceService.get`) and never any write; text is always
interpolated, never inserted as HTML (no `innerHTML`, no `bypassSecurityTrust*`); the page must not render what FR-012 forbids (payments,
paid, remaining, ids, the customer's active flag, contact details, `updatedAtUtc`, `allowedTransitions`, `isEditable`, `acceptsPayment`);
the application's shell (toolbar, menus, Sign out) is never part of the page; no backend, database, migration or package change
(**do not touch `backend/`**); no connection string, key or credential in any committed file; Arabic and English text must round-trip
unchanged; no PDF or file output, email, letterhead, seller details, e-invoicing, template choice, bulk print or print history (FR-015).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the new items in plan.md "Patterns and decisions to confirm": (1) a **top-level route outside the signed-in shell** at `/invoices/:id/print`, guarded by the same `authGuard`, so the toolbar, menus and Sign out are never in the page (the first route without the shell); (2) **print styles in the component with no style encapsulation** (`ViewEncapsulation.None`), scoped under one `.invoice-print` class, holding `@media print`, the page margin and the pagination rules (the first such component); (3) **parity by construction and by test**: the page uses the details' own field paths, pipes and `data-testid`s, no shared helper or component is added, and one spec renders both screens from one invoice and compares them; (4) **Print opens in the same tab and never prints by itself**: a plain router link from the details, and an on-screen Print button that calls the browser's print dialog through the injected `DOCUMENT`; (5) **no backend and no data**: the existing `GET /api/invoices/{id}` is the only call, with no print endpoint, no printed flag and no history. Record the confirmations in research.md under "Confirmations (T001)". Do not proceed until confirmed; if the developer changes any item, update plan.md, research.md, data-model.md and the contract first
- [X] T002 Baseline check before any change: stop the API and `ng serve` if running, then (in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` and `ng build` pass, and `dotnet test` for each of the three backend test projects in turn passes (they must stay unchanged and green); note the time, so T019 can prove that no file under `backend/` changed afterwards

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The route, the component skeleton with its load states and the route test.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 [P] Write the route test first in `frontend/src/app/invoice-print.routes.spec.ts` (in the style of `products.routes.spec.ts` and `app-customers-routes.spec.ts`): the `routes` array has a route with `path: 'invoices/:id/print'`; it is a **top-level** route (a direct element of `routes`, not a child of the route whose component is `ShellComponent`); it is lazy (`loadComponent`); its `canActivate` contains `authGuard`; it appears before the wildcard `**` route; and no route under the shell also matches `invoices/:id/print`
- [X] T004 [P] Write the state tests first in `frontend/src/app/features/invoices/invoice-print.component.spec.ts` (the file that later holds the content tests): a stub `InvoiceService` with only `get`, a stub `ActivatedRoute` with the id, `provideRouter([])`; the page shows `[data-testid="detail-loading"]` while the request is pending; a 404 shows the heading "Invoice not found" and a link back to `/invoices`, and **no** Print button; any other failure shows `[data-testid="detail-error"]` with "The invoice could not be loaded." and the API's `detail` when there is one (otherwise "Try again."), a `[data-testid="retry"]` button that loads again (get called twice, then the invoice shows), and no Print button; `get` is called once with the route id on open
- [X] T005 Add the route and the component skeleton. `frontend/src/app/app.routes.ts`: a top-level route `{ path: 'invoices/:id/print', canActivate: [authGuard], loadComponent: () => import('./features/invoices/invoice-print.component').then((m) => m.InvoicePrintComponent) }` placed **after** `login` and **before** the shell route (a comment says why it is outside the shell: nothing of the application's chrome may be in the page). `frontend/src/app/features/invoices/invoice-print.component.ts`: a standalone `InvoicePrintComponent` (`selector: 'app-invoice-print'`, `encapsulation: ViewEncapsulation.None`, an empty `.invoice-print` wrapper) with the state signal `loading | loaded | notFound | error` exactly like `InvoiceDetailComponent`'s `DetailState`, `load()` calling `InvoiceService.get(id)` (route id from `ActivatedRoute.snapshot.paramMap`, `takeUntilDestroyed`), `problemDetail()` for the error detail, and the three non-loaded states' markup and wording from the details; the loaded state renders nothing yet. Make T003 and T004 pass (depends on T003, T004)
- [X] T006 Run `ng test --watch=false` and `ng build`; both pass and the build shows the new lazy chunk (depends on T003-T005)

**Checkpoint**: the route exists behind the guard and outside the shell; the page loads an invoice or shows its not-found / error states; nothing else changed.

---

## Phase 3: User Story 1 - Open a printable invoice and print it (Priority: P1)

**Goal**: The details have a **Print** link; the page shows the title, customer, number, dates, notes, lines, totals and status as a clean document with an on-screen Print button and a way back that do not print, and print rules that give a proper paper layout.

**Independent Test**: Open an invoice, click **Print**, and confirm the page shows every required item, has no application toolbar, and that the browser's print preview shows only the document.

### Tests for User Story 1 (write first; they fail until T009-T011)

- [X] T007 [P] [US1] Add the content tests to `frontend/src/app/features/invoices/invoice-print.component.spec.ts` (fixtures: a Draft of two lines with a discount and notes, a Sent, a Paid and a Cancelled invoice; the details spec's fixtures are a good model): the page heading is `BillingCRM` (an `h1`); `[data-testid="number"]` shows the number; `[data-testid="customer"]` the customer's name; `created-at` and `due-date` are always present and `sent-at` / `cancelled-at` appear only when the invoice has them (not for a Draft; sent for Sent and Paid; cancelled for Cancelled); `notes` shows the text with its line break and, when `notes` is null, **no** notes element and no "Notes" heading anywhere; every line has its own `[data-testid="line-row"]` showing product name, SKU, unit, unit price (`SAR 100.00`), VAT percent (`15%`), quantity, net and VAT amount, in the order returned, with the discount column only when the totals carry a discount; the totals block shows `total-net`, `total-vat`, `grand-total`, and `discount` and `net-after-discount` only with a discount; the status word is in `status-badge`; an on-screen bar with the class `no-print` contains **exactly** the Print button (`[data-testid="print-button"]`) and the back link (`[data-testid="back"]`, href `/invoices/<id>`) and nothing printed is inside it and nothing of the document is inside it; **opening the page does not call the browser's print** (a fake `DOCUMENT` whose `defaultView.print` is a spy: zero calls), and clicking the Print button calls it exactly once per click; the print stylesheet is present while the page is shown (a `<style>` in `document.head` whose text contains `.invoice-print`, `@media print`, `.no-print`, `@page`, `table-header-group` and `break-inside`)
- [X] T008 [P] [US1] Add the Print link tests to `frontend/src/app/features/invoices/invoice-detail.component.spec.ts`: for each of Draft, Sent, a late Sent (Overdue), Paid and Cancelled invoices and for both an Admin and an Agent (`it.each`), `[data-testid="print"]` exists, reads "Print", has `href` `/invoices/<id>/print` and **no** `target` attribute; the link is not shown while the invoice is loading, not found or in error; the existing Send, Edit, Cancel and Record payment behaviour is unchanged (the current tests still pass)

### Implementation for User Story 1

- [X] T009 [US1] `frontend/src/app/features/invoices/invoice-print.component.ts`: the loaded state's markup per contracts/print-page.md and data-model.md, using **plain elements** (no Material card): an element with class `no-print` holding a `<button type="button" data-testid="print-button" (click)="print()">Print</button>` and `<a data-testid="back" [routerLink]="['/invoices', invoice.id]">Back to invoice</a>`; then the document: `<h1>BillingCRM</h1>`, `<h2 data-testid="number" dir="ltr">`, the status word `<span data-testid="status-badge">{{ invoice.isOverdue ? 'Overdue' : invoice.status }}</span>`, a `dl` with Customer (`dir="auto"`), Created (`date: 'medium'`), Due date (`date: 'mediumDate'`), Sent and Cancelled (`date: 'medium'`, each only when present), the notes (`dir="auto"`, line breaks kept with `white-space: pre-line`) only when present, the lines `<table>` with a `<thead>` (Product, SKU, Unit, Unit price, VAT, Quantity, Net, the Discount column only when `totals.discount !== 0`, VAT amount) and one `data-testid="line-row"` per line using exactly the details' pipes (`number: '1.2-2'` for prices, amounts and quantity, `'1.0-2'` for the percent, `SAR` before each amount, names `dir="auto"`, numbers and SKU `dir="ltr"`), then the totals block with the details' ids and order (`total-net`, `discount` and `net-after-discount` only with a discount, `total-vat`, `grand-total`). Inject `DOCUMENT`; `print()` calls `this.document.defaultView?.print()`. No other read, no calculation, no field FR-012 forbids. Make T007 pass (depends on T005, T007)
- [X] T010 [US1] The print stylesheet in `invoice-print.component.ts` (`styles` with `encapsulation: ViewEncapsulation.None`, every rule under `.invoice-print`) per research R3: on screen a readable single column (`max-width` about 800px, centred) with plain document styling; `@media print { .invoice-print .no-print { display: none } }`; `@page { margin: 15mm }` (no `size`); black text on white (`color: #000; background: #fff`) and no reliance on background colours; the table full width with `overflow-wrap: anywhere` on text cells and `white-space: nowrap` on amount cells; `thead { display: table-header-group }`; `tr { break-inside: avoid }`; the totals block and its heading `break-inside: avoid`, and `break-before: avoid` on the totals block after the table; the status printed as bold text; the notes `white-space: pre-line`. Make the stylesheet assertion in T007 pass (depends on T009)
- [X] T011 [US1] `frontend/src/app/features/invoices/invoice-detail.component.ts`: add to the action row, for every loaded invoice, `<a mat-button data-testid="print" [routerLink]="['/invoices', invoice.id, 'print']">Print</a>` (no `target`; a short comment says it opens in the same tab because the session is per tab), placed before "Back to invoices". Make T008 pass (depends on T008)
- [X] T012 [US1] Run `ng test --watch=false` and `ng build`; both pass (depends on T007-T011)

**Checkpoint**: an invoice can be printed from its details: the page shows every required item, its on-screen controls do not print, and the print rules are in place. The figures are the details' by construction; US2 proves it.

---

## Phase 4: User Story 2 - The printout says what the details say (Priority: P1)

**Goal**: Every amount, date and the status on the printed page equal the details' for the same invoice, for every kind of invoice; nothing is calculated; long and mixed-language content prints correctly.

**Independent Test**: For an invoice with two lines at different VAT percentages and a discount, compare the details and the print page figure for figure, then repeat for each status.

### Tests for User Story 2 (write first; the parity and content checks pin what US1 built, so they should pass, and any that does not is a defect to fix in T015)

- [X] T013 [P] [US2] Write the parity test in `frontend/src/app/features/invoices/invoice-print-parity.spec.ts`: one helper renders `InvoiceDetailComponent` (stubs: `InvoiceService` with `get`, `AuthService` with `role`, `ActivatedRoute`, `provideRouter([])`) and `InvoicePrintComponent` (same stubs) from the **same** `Invoice` object and returns each page's text for the shared ids, with whitespace collapsed; for each of these ids the two texts must be **equal**: `number`, `customer`, `due-date`, `created-at`, `sent-at`, `cancelled-at`, `notes`, `status-badge`, `total-net`, `discount`, `net-after-discount`, `total-vat`, `grand-total`, and for every `line-row` (compared by index) its text; it also asserts that an id present on one page is present on the other (and absent on both when not applicable). Run it (`it.each`) for a Draft, a Sent, a late Sent (`isOverdue: true`), a Paid and a Cancelled invoice, each **with and without a discount**, using figures that would differ if anything were recalculated (a grand total that is not the sum of its parts such as net 111.11, VAT 22.22, grand total 999.99; a line whose VAT is not net times percent; a quantity like 1.5; an amount like 1234567.5 that shows thousands separators). Assert the status words are `Draft`, `Sent`, `Overdue`, `Paid`, `Cancelled` respectively on both pages
- [X] T014 [P] [US2] Add the remaining content checks to `invoice-print.component.spec.ts`: **no arithmetic** (a fixture whose totals are not the sum of the lines and whose line VAT is not net times percent is printed exactly as given, and the text contains none of the figures a client would work out); each line's discount share equals the details' and the Discount column and rows are absent when `totals.discount` is 0; a Cancelled invoice shows its cancelled date and a Draft shows neither sent nor cancelled; an inactive customer prints by name with no "Inactive" text; **nothing FR-012 forbids**: the page text contains no "Paid" or "Remaining" *labels* and no payment row for an invoice whose `payments`, `paid` and `remaining` are set (Paid status excepted: the word Paid appears only as the status), no id (the invoice id, customer id, line ids and product ids never appear in the text or in any attribute other than the back link's `href`), no "Last changed" or `updatedAtUtc` value; text direction: names, notes and product names have `dir="auto"` and amounts, numbers, dates and SKUs have `dir="ltr"`; Arabic and English (`شركة الأمل`, `ساعة استشارة`) render as written; a note and a product name containing `<b>x</b>` and `&` appear as text (no `<b>` element in the document); long content (a 300-character note without spaces and a 200-character product name) is present in full; amount cells carry the no-wrap class; 100 lines render 100 `line-row`s; the only service method called is `get` (the stub has no other method) and it is called once
- [X] T015 [US2] Make any failing test from T013 and T014 pass by fixing the **page** (never the details and never the test's expectations): adjust `invoice-print.component.ts` so its field paths, pipes, wording, order and ids match the details exactly, and its direction, wrapping and no-wrap classes are as specified. If a difference is intended (for example the page has a plain heading where the details have a card), the parity helper compares only the shared ids and the difference is documented in a comment there (depends on T013, T014)
- [X] T016 [US2] Run `ng test --watch=false` and `ng build`; both pass (depends on T013-T015)

**Checkpoint**: every figure and the status on the printable page are proved equal to the details' for each kind of invoice; the page calculates nothing and shows nothing it must not.

---

## Phase 5: Polish and Cross-Cutting Concerns

- [X] T017 [P] Update `README.md`: the Print action on an invoice's details (any signed-in user, any status), what the printable page shows (title BillingCRM, customer name, number, dates, notes, lines, totals, status) and what it deliberately leaves out (payments, paid and remaining, customer contact details, seller details), that it is the details' figures (nothing recalculated), that **Print** opens in the same tab and has an on-screen Print button that opens the browser's print dialog (and Ctrl+P works too), that "Save as PDF" is the browser's feature and the application makes no file, that opening or printing changes nothing, that there is **no migration** for this spec, and a pointer to `specs/009-invoice-print/quickstart.md`
- [X] T018 [P] Update `CLAUDE.md` (touch only what 009 adds): the Project paragraph (Spec 009 added the printable invoice page, front end only), the frontend layout line (`invoice-print`, the top-level route outside the shell), and an "Invoice print (spec 009)" bullet under Run Commands: the route `/invoices/:id/print` outside the shell behind `authGuard`, opened in the same tab from the details' Print link, the one `GET`, no arithmetic and the parity test, the print styles in the component with no encapsulation, no backend or data change
- [X] T019 Prove nothing on the server moved: run `find backend -type f -newer specs/009-invoice-print/plan.md -not -path '*/bin/*' -not -path '*/obj/*'` (or compare timestamps against the time noted in T002) and confirm it lists **nothing**; then `dotnet build backend/BillingCrm.sln` and `dotnet test` for each of the three backend test projects in turn, unchanged and green
- [X] T020 Run the whole front-end suite once more (`ng test --watch=false`, `ng build`); all pass; search the files changed by this spec for `innerHTML`, `bypassSecurityTrust`, `Password`, `Secret`, `SigningKey`, `Server=` and confirm there is no hit; confirm no file under `frontend/` other than `app.routes.ts`, `invoice-detail.component.ts` (+ its spec), the new print files and the new route spec was changed
- [X] T021 [P] Walk through `specs/009-invoice-print/quickstart.md` sections A, B and D against the automated tests and report which rows are covered by a test and which are manual-only (the print preview, the long invoice, the very large amount, Save as PDF and the browser's own dialog are manual); add any missing automated test for a row that can be automated
- [X] T022 Run the full quickstart validation (`specs/009-invoice-print/quickstart.md`) on the developer's machine, including every browser check (the page beside the details with identical figures for a Draft, Sent, Overdue, Paid and Cancelled invoice, no toolbar or menu on the page, the Print button opening the browser's dialog only when pressed, the print preview showing only the document, a 40-line invoice running over pages with the heading row repeated and no line cut, the very large amount on one line, Arabic names, the invoice unchanged after printing, not-found and error-with-retry states, and the sign-in redirect); the developer starts the servers and confirms it is running (Constitution II)
- [X] T023 Run `angular-reviewer`, `spec-auditor`, **`security-reviewer`** and `api-reviewer`; fix or explicitly record every finding in `specs/009-invoice-print/research.md` (a "Review disposition" section like 008's). Give `angular-reviewer`: no arithmetic, rounding or derived status in the page; the parity test really compares the two screens (and would fail if the page recalculated); the route outside the shell and its guard; the print stylesheet (scoped under `.invoice-print`, `@page` only while the page is shown, no colour dependence, repeated header, kept rows and totals, no-wrap amounts, wrapping of long text); accessibility (heading structure `h1` then `h2`, the table's header cells and caption or label, the Print button and back link, focus, the status as text, the not-found and error states as alerts); text direction; no `innerHTML`; and weak or overclaiming tests. Give `security-reviewer`: that the page shows the same data the details show to the same signed-in users and nothing more (no new exposure), the guard on the route and the API's own 401, that a print URL cannot be used without a session, that no invoice data is put in the URL, storage or the document title, that text (notes, names) cannot inject markup or script into the page, and that nothing is written. Give `spec-auditor` the whole spec (US1.1-US1.7, US2.1-US2.7, edge cases, FR-001..FR-015, SC-001..SC-007) with the browser-only rows marked Unknown until T022. Give `api-reviewer`: confirm that no file under `backend/` changed and that the print page's single call is the existing `GET /api/invoices/{id}`
- [X] T024 Re-run `ng test --watch=false`, `ng build` and `dotnet test` for each backend project after any review fixes; all pass
- [X] T025 Mark spec 009 done only after T022 and T023 are complete, and update `specs/009-invoice-print/spec.md` status to Done; do not start spec 010 before then

---

## Dependencies and Execution Order

- **Setup** (T001-T002) first. T001 blocks all code.
- **Foundational** (T003-T006) blocks both stories: T003 and T004 (tests) in parallel; T005 (needs both); T006 last.
- **US1** (T007-T012): T007 and T008 (tests, different files) in parallel; T009 then T010 (same component file); T011 (the details file, needs T008); T012 last. Needs Foundational.
- **US2** (T013-T016): T013 and T014 (tests, different files) in parallel; T015 (fixes, needs both and US1's markup); T016 last. Needs US1.
- **Polish** (T017-T025): T017, T018 and T021 in parallel; T019 and T020 after the code is final; T022 and T023 are the done-ness gate (T022 needs the servers started by the developer); T024 after fixes; T025 last.

### Parallel example (Foundational and US1)

```text
T003 route spec        T004 state specs                    T007 content specs     T008 details Print link specs
        \                    /                                     \                          |
                T005 route + skeleton -> T006          T009 markup -> T010 print styles      T011 details link
```

## Implementation Strategy

- **MVP**: Foundational plus US1 (T001-T012): an invoice can be opened as a printable page and printed from the browser. It already uses the
  details' own paths, pipes and ids, so its figures are the details'; US2 then proves it with the parity test and the wider checks.
- Build test-first inside each phase and run the suite at every checkpoint (T006, T012, T016) before moving on.
- No migration, no backend change: the three backend suites are run unchanged only to prove nothing moved (T002, T019, T024).
- The developer's verification (T022) is the gate for closing 009 and for starting 010.
