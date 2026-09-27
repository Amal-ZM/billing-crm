# Implementation Plan: Invoice Print

**Branch**: `009-invoice-print` | **Date**: 2026-09-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-invoice-print/spec.md`

## Summary

Give an invoice a page made for paper. **Nothing changes on the server**: no API, table, migration, package or stored data. The details
screen gains a **Print** link that opens one new screen, `/invoices/:id/print`, which reads the invoice with the existing
`GET /api/invoices/{id}` and lays out exactly what the details already show (title BillingCRM, customer name, number, dates, notes,
lines, totals, status) as a clean document. Three choices carry the design. **First**, the page is a top-level route *outside* the
signed-in shell (still behind the same `authGuard`), so the toolbar and menus are not in the page at all and nothing has to be hidden
for print; its own on-screen controls (a Print button that calls the browser's print dialog, a way back) are marked and hidden by print
styles. **Second**, every figure and the status word are rendered from the same response fields, with the same pipes and the same
`data-testid`s as the details, and a **parity test renders the details and the print page from one invoice and compares them**, so
"totals match detail" is proved rather than assumed and the page can never grow arithmetic. **Third**, the print rules (page margin, a
repeating table header, rows and totals kept whole, black on white) live in the component's own styles with no encapsulation, scoped
under one class, so they exist only while this page is on screen. The link opens in the **same tab** because the session lives in
`sessionStorage`, which a new tab would not share. Decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: TypeScript on Angular 21 (unchanged); no backend change (C# on .NET 8 untouched)

**Primary Dependencies**: no new packages. Plain document markup (the page is not a Material card; its two on-screen controls are a native button and a link, so they look like a document, not an app screen), Angular Material only for the loading spinner, `DecimalPipe`, `DatePipe`, `DOCUMENT`

**Storage**: none. No migration. Nothing is stored or recorded when a page is opened or printed (FR-011)

**Testing**: Vitest for Angular: the print component (each required item, with and without notes and discount, every status including
Overdue, states, no calculation, no writes, print button calls the browser once, marked controls, escaped text), the details screen's
Print link (every status and role), the route configuration (top-level, behind `authGuard`, not under the shell), and the **parity test**.
No backend tests change. The Application, API and Infrastructure suites are run unchanged to prove nothing moved.

**Target Platform**: Developer workstation, localhost; a current desktop browser's print preview (Chrome or Edge) for the manual check

**Project Type**: Web application (back end + front end); this spec touches the front end only

**Performance Goals**: none new. One invoice is read once; 100 lines render as one table

**Constraints**: every amount and the status are the API's values, shown with the details' pipes, never calculated, rounded or derived;
nothing of the application's chrome appears in print; no request other than the one `GET`; the page changes nothing; text is always
interpolated (never inserted as HTML); `decimal` values arrive as JSON numbers and are only formatted for display

**Scale/Scope**: 1 new screen (1 component + its spec), 1 new route, 1 link added to the details screen, 1 docs update; no service,
model, API or database change

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design, still passes.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Scope Discipline | Pass | Invoices are a permitted domain. No PDF or file, email, letterhead, e-invoicing, template, bulk print, shop, cart, payment gateway, tickets or WhatsApp (FR-015). |
| II. Sequential Spec Delivery | Pass | 008 was verified running by the developer on 2026-09-26 and closed. 009 is done only when verified running locally and the reviewers have run. |
| III. Explicit Business Errors | Pass | No new business error exists. Not found, a failed load and a signed-out visitor use the existing responses (`404`, other failures, `401` to sign-in) shown as messages; nothing throws for them. |
| IV. Financial Data Integrity | Pass | The page shows the API's `decimal` amounts (SAR, two decimals) and performs **no** arithmetic, rounding or conversion of money; a parity test proves the figures equal the details'. No secrets. |
| V. Deliberate API/UI Design | Pass | The new shapes below are to be confirmed by the developer before implementation (T001). |

**Workflow gates**: use `angular-ui-conventions` (standalone component, a feature service owns `HttpClient`, states on every screen) and
`invoice-money` (no client money maths). No `minimal-api-conventions` or `sql-migrations` work exists in this spec. Before the spec counts
as done run `angular-reviewer`, `spec-auditor` and `security-reviewer` (a new screen that shows financial data), and `api-reviewer` to
confirm no backend file changed (CLAUDE.md rule 5).

### Patterns and decisions to confirm with the developer before generating (Principle V)

Reused, already-approved patterns (a lazy standalone screen per route, `InvoiceService.get`, loading / not-found / error-with-retry
states, `authGuard`, `DecimalPipe` and `DatePipe` formatting, `data-testid`s, the "screens show what the API returned and calculate
nothing" rule, plain text interpolation) need no re-explanation. New in this spec:

1. **A top-level route outside the signed-in shell** — `/invoices/:id/print` sits beside `login` and the shell, guarded by the same
   `authGuard`, so the toolbar, menus and sign-out button are never in the page (R1). This is the first route without the shell.
2. **Print styles in a component, with no style encapsulation** — one new component, `InvoicePrintComponent`, whose stylesheet is global
   while it is on screen but scoped under a single `.invoice-print` class, holding the print rules and the page margin (R3). This is the
   first component with `ViewEncapsulation.None` and the first print stylesheet.
3. **Parity by construction and by test** — the page uses the same field paths, pipes and `data-testid`s as the details, and a spec
   renders both from one invoice and compares figures, dates and the status word. No shared helper is added; the one-line status
   expression is written the way the details and the list already write it (R4).
4. **Print opens in the same tab and does not print by itself** — a plain router link from the details; the page has a Print button that
   calls the browser's print dialog through the injected `DOCUMENT`; a new tab would lose the `sessionStorage` session (R2, R5).
5. **No backend, no data** — the existing `GET /api/invoices/{id}` is the only call; there is no print API, no printed flag and no history (R6).

## Project Structure

### Documentation (this feature)

```text
specs/009-invoice-print/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── print-page.md
├── checklists/
│   └── requirements.md
└── tasks.md              # created by /speckit-tasks
```

### Source Code (changes to the 008 layout)

```text
frontend/src/app/
├── app.routes.ts                                   # + top-level route invoices/:id/print (authGuard, lazy), before the shell
├── features/invoices/
│   ├── invoice-print.component.ts                  # NEW: the printable page (+ print styles, no encapsulation)
│   ├── invoice-print.component.spec.ts             # NEW: content, states, controls, no writes, print button
│   ├── invoice-print-parity.spec.ts                # NEW: details and print page compared for one invoice
│   ├── invoice-detail.component.ts                 # + Print link (every status, every role)
│   └── invoice-detail.component.spec.ts            # + Print link tests
└── invoice-print.routes.spec.ts                    # NEW: the print route is top-level, lazy and guarded

README.md, CLAUDE.md                                # the Print action and the page; the layout line (listed tasks, as for 007 and 008)
```

**Structure Decision**: Extend the 008 layout in place. One new component and two spec files; one route; one link. No new service, model,
pipe, shared helper, project or package. The backend and database are not touched.

## Known gaps carried forward

- No PDF or stored copy: "Save as PDF" is whatever the browser's print dialog offers; a later spec can add a server-made document.
- No seller identity (company name, address, logo, VAT registration) and no e-invoicing (QR) fields: they need settings the system does
  not have yet. A later spec that adds them will change this page.
- The printout omits payments, paid and remaining (a decision in the spec's Clarifications).
- The browser's own print header and footer (page title, address, date, page numbers) are the browser's setting; the page cannot remove
  them. **The address contains the invoice's id and the host name**, so for a copy that leaves the company the person should untick
  "Headers and footers" in the print dialog (the README and the quickstart say so).
- A line with several very large amounts is wider than a portrait A4 page even at the smaller print size; it relies on the browser scaling it
  to fit (manual check in the quickstart). Realistic invoices fit.
