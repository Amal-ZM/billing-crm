# Research: Dashboard

Every decision below was made from the spec, the constitution and the code as it stands after 010. No `NEEDS CLARIFICATION` remains (the
spec itself needed none). The one item the spec leaves to planning — the shape of a new endpoint — is decided here and confirmed with the
developer at task T001, the same gate 006 to 010 each used for their own new pattern.

## R1. Where the summary lives and what it looks like on the wire

**Decision**: the five figures are read by `InvoiceService.GetSummaryAsync(CancellationToken)` (a new method on the existing service, not a
new service), backed by a new `IInvoiceStore` method. The route is `GET /api/invoices/summary`, inside the **existing** `/api/invoices`
group (same `RequireAuthorization()`, no extra policy — any signed-in Admin or Agent, exactly like the invoice list itself). It answers
`200` with `{ draftCount, unpaidCount, overdueCount, paidThisMonthCount, unpaidAmount }` (four integers and one SAR amount). It takes no
query string and has no failure mode of its own (there is nothing to look up by id and nothing a person can get wrong): it is always a
`200`, unauthenticated is the existing `401`.

**Rationale**: the summary is entirely about invoices, so it belongs in the invoice group and the invoice service, not a new resource, a
new store or a new top-level Application folder — matching FR-001 to FR-006 and Constitution V's "reuse an already-approved pattern where
one exists" (the group, the service, the auth). What genuinely has no precedent in this codebase is the **response shape**: every existing
`GET` returns one resource, a page of resources, or (010) a resource's history — never a set of aggregate numbers with no `id` and nothing
to page. That is the one new kind of thing this feature introduces, and it is confirmed with the developer at T001 before being built,
exactly as 006's amounts, 007's status fields, 008's payment shape, 009's print page and 010's history entry each were.

**Alternatives considered**: a new `DashboardService`/`IDashboardStore` pair (an extra layer for one read that touches only invoices and
payments, both already owned by `InvoiceService`); folding the summary into `GET /api/invoices` itself as extra fields on the paged list
(every list read would compute five aggregate figures it does not need); a new top-level `/api/dashboard` route group (a second place that
reads invoices, for no benefit over nesting it under the resource it is entirely about).

## R2. How each figure is worked out

**Decision**: five independent, simple queries against the existing `Invoices` and `Payments` tables, run when `GetSummaryAsync` is called,
never cached:

| Figure | Query |
|--------|-------|
| Draft count | `COUNT(*)` where `Status = Draft` |
| Unpaid count | `COUNT(*)` where `Status = Sent` (on-time and overdue both count; Overdue is not a separate stored status) |
| Overdue count | `COUNT(*)` where `Status = Sent AND DueDate < today` (today = the same fixed Saudi UTC+03:00 date `InvoiceService.Today` already uses) |
| Paid this month count | `COUNT(*)` where `Status = Paid` and the invoice's **settling payment**'s `PaidOn` falls within the current Saudi calendar month |
| Unpaid amount sum | `SUM(FrozenGrandTotal - SUM(Payments.Amount))` where `Status = Sent`, i.e. the sum of each Sent invoice's `Remaining` (`InvoiceMoney.Balance`, already used for the details screen) |

The **settling payment** of a Paid invoice is simply its last-recorded payment (`Payments.RecordedAtUtc` descending, take one): once an
invoice's remaining balance reaches zero it is Paid and cannot take another payment (spec 008), so the last payment recorded is always the
one that settled it, whatever its own `PaidOn` date is (which is validated at the time, but not against other payments' dates). This
matches the spec's Assumptions exactly ("decided by the settling payment's own payment date").

**Rationale**: FR-002 to FR-006. Five small, independent queries are simpler to write, test and reason about than one composite query, and
match the existing style (`EfInvoiceStore.SearchAsync` already runs a separate count query before its paged query). At the 5,000-invoice
scale the Infrastructure performance tests already exercise, `COUNT`/`SUM` with a `WHERE` on `Status` (and the correlated per-invoice
payment sum, which only runs over Sent or Paid invoices, a fraction of the total) cost nothing new.

**Alternatives considered**: one query returning all five figures via `GROUP BY` (harder to read, and the "paid this month" and "unpaid
amount" figures need different joins than a plain status group-by can express in one pass); loading every invoice with its lines and
payments into memory and computing in .NET (defeats the point of a database; would not scale); storing `PaidAtUtc` on `Invoice` as a new
column (a schema change for a value that is already recoverable from `Payments.RecordedAtUtc`, and 010 already established that a change
is described by its own record, not a new column on the invoice).

## R3. The invoice list's four filtered views

**Decision**: `GET /api/invoices` gains three new, optional query values, read as text like `page`/`pageSize` already are: `status`
(`Draft`, `Sent`, `Paid` or `Cancelled`; an unrecognized value is the existing 400-on-field pattern, naming `status`), `overdue` (`true` —
meaningful only together with `status=Sent`; ignored otherwise, so a client cannot get a different error by pairing it with the wrong
status) and `paidMonth` (`true` — meaningful only together with `status=Paid`; likewise ignored otherwise). Nothing else about the list
changes: paging, ordering (newest first) and every existing response field stay as they are. The four dashboard links become:

| Card | Query string |
|------|--------------|
| Draft | `?status=Draft` |
| Unpaid (and the unpaid amount sum) | `?status=Sent` |
| Overdue | `?status=Sent&overdue=true` |
| Paid this month | `?status=Paid&paidMonth=true` |

**Rationale**: FR-007 to FR-010, SC-003. This reuses the exact shape customers' (`type`, `active`) and products' (`unit`, `status`) list
filters already use: an optional, named, typed query value that narrows the existing paged read, refused on its own field if it cannot be
understood. No general filter control is added to the invoice list **screen** (the spec's own Assumption): these three values exist so a
dashboard card's link can reach the right set, not as a new search UI a person types into.

**Alternatives considered**: a single `view=draft|unpaid|overdue|paidThisMonth` parameter (fewer combinations to reason about, but an
opaque enum that duplicates `status` under another name is a new kind of filter shape, where three separately-named, independently
reusable values match what is already there); a dedicated `/api/invoices/overdue` and `/api/invoices/paid-this-month` route each (more
routes for what is still "the same list, narrowed," and inconsistent with how every other narrowing on this list — paging — is a query
value, not a different route).

## R4. Angular: cards on Home, a service, and a screen that reads the same query values

**Decision**: `core/dashboard/dashboard.models.ts` (`DashboardSummary`) and `core/dashboard/dashboard.service.ts` (`summary(): Observable
<DashboardSummary>`, one `GET /api/invoices/summary`) — a new `core/<domain>` folder and service, exactly like every other resource
(`core/invoices`, `core/customers`, `core/products`), so this needs no separate explanation under Constitution V. `features/home/home.
component.ts` (already existing) gains the five cards below its current content, each an `[routerLink]="['/invoices']"` with
`[queryParams]` set to the matching table from R3 (a plain Angular Material link/card, the same kind of navigation the app already uses
elsewhere, not a new UI pattern). `features/invoices/invoice-list.component.ts` reads `status`, `overdue` and `paidMonth` from its own
route's query parameters (it already keeps its search/filters/page in the query string per 005) and passes them through to
`InvoiceService.list`, which passes them to the API unchanged.

**Rationale**: FR-001, FR-007 to FR-011, US1, US2. Every piece here — a core service per resource, keeping list state in the query string,
a Material card/link — is an already-established pattern in this codebase.

**Alternatives considered**: a `DashboardComponent` as a new route (the spec explicitly says "When I open home", not a new screen); a
signal or service shared directly between Home and the invoice list instead of the URL (the app's own established habit, since 005, is
the address bar, which also makes a card's destination a plain, bookmarkable link).

## R5. Test approach

- **Application (fakes)**: each figure computed correctly from a small set of seeded invoices/payments covering every status and both
  overdue and on-time Sent invoices; a settling payment recorded in a different month from an earlier part payment counts by its own date;
  a Cancelled invoice affects nothing; an empty store gives all zeros; the unpaid amount sum matches `InvoiceMoney.Balance` for each Sent
  invoice added together; the new invoice-list filter values narrow `SearchAsync`'s results to exactly the matching invoices, in
  combination with paging.
- **API**: `GET /api/invoices/summary` for an Admin and an Agent, `401` with no token, the exact field names and types, no query string
  accepted or needed; `GET /api/invoices?status=...` (each of the four value combinations) returns exactly the matching invoices and nothing
  else, an unrecognized `status` is a 400 naming the field, `overdue`/`paidMonth` paired with the "wrong" status changes nothing; the
  route-list pin (`InvoiceAccessTests`) gains exactly one new `GET`.
- **Infrastructure (real SQL)**: the five figures against a real database at a meaningful scale (built on the existing 5,000-invoice
  performance fixture data or a smaller purpose-seeded set), including a Paid invoice whose settling payment predates or postdates its
  earlier part payments' months, and the boundary of a calendar month (a payment on the last instant of one month does not count for the
  next).
- **Angular**: the five cards show the API's own figures with no client-side recalculation; each card's link carries the right query
  parameters; the invoice list applies an incoming `status`/`overdue`/`paidMonth` from the address to its own request and to what it shows
  as the active filter; a person on Home with no invoices sees five zeros, not an error or a blank dashboard.

## R6. Docs

`README.md` gains a description of the dashboard cards and what each links to; `CLAUDE.md` gains a "Dashboard (spec 011)" bullet with the
new route, the invoice-list filter values and where the figures come from. The `invoice-money` skill needs no change (the amount sum only
adds together figures `InvoiceMoney` already produces, it introduces no new calculation). These are listed tasks, as for 007 to 010.

## Confirmations (task T001) — 2026-09-27

The developer confirmed all three items from plan.md's Constitution Check, as recommended:

1. The read shape: `GET /api/invoices/summary` (an aggregate of five figures, no id or paging) nested in the existing `/api/invoices`
   group and `InvoiceService`; three new optional query values (`status`, `overdue`, `paidMonth`) on `GET /api/invoices`, reusing
   customers'/products' typed-filter pattern; no new table, column or migration — everything computed live from `Invoices` and `Payments`.
2. "Paid this month" is decided by the settling payment's own `PaidOn` date (the last-recorded payment of a Paid invoice), never by when
   it was typed in and never by a new stored column.
3. Angular: a new `core/dashboard/` service and model; cards added to the existing `HomeComponent`, not a new screen;
   `invoice-list.component.ts` reads `status`/`overdue`/`paidMonth` from its own address like it already reads `page`/`pageSize`.

## Known gaps

- The dashboard is a snapshot as of the page load, like the rest of the application; it does not update itself while left open.
- No per-user scoping (this application has no concept of invoice ownership by staff member) and no history of past months' figures.
- "This month" is always the current Saudi calendar month; there is no way to see a previous month's figures from the dashboard itself
  (the invoice list can still be searched for older invoices; only the dashboard's own count is scoped to the current month).

## Quickstart coverage (task T030) — 2026-09-27

Every row of `quickstart.md` sections 2 and 3 walked against the automated suite:

| Section | Row | Automated? | Where |
|---------|-----|------------|-------|
| 2 | No invoices: all five cards 0, SAR 0.00 | Yes | `InvoiceServiceSummaryTests.An_empty_store_...`, `InvoiceSummaryEndpointTests.An_empty_store_...` |
| 2 | Create a Draft: Draft +1 | Yes | `InvoiceServiceSummaryTests.Draft_count_is_the_number_of_Draft_invoices` |
| 2 | Send it (future due date): Draft -1, Unpaid +1, Overdue unchanged | Yes | `Unpaid_count_is_every_Sent_invoice_on_time_and_overdue_alike`, `Overdue_count_is_only_...` |
| 2 | A second, overdue Sent invoice: Unpaid +1, Overdue +1, unpaid amount includes it | Yes | `Overdue_count_is_only_...`, `The_unpaid_amount_sum_is_the_Sent_invoices_own_remaining_balances_added_together` |
| 2 | Full payment today: Paid this month +1, unpaid amount drops | Yes | `Paid_this_month_counts_by_the_settling_payments_own_date_...`, `The_unpaid_amount_sum_excludes_Draft_Paid_and_Cancelled_invoices` |
| 2 | The request's own worked example (1 overdue, 1 paid this month, unpaid excludes paid) | Yes | `One_overdue_and_one_paid_this_month_show_1_and_1_...` (Application + real-SQL `Each_figure_counts_only_its_own_bucket`) |
| 2 | Cancel: stops being counted anywhere | Yes | `Cancelled_invoices_affect_no_figure` |
| 2 | Click Draft card -> only Draft invoices | Yes (composed) | `InvoiceSearchFilterEndpointTests.Status_Draft_...` (API) + `home.component.spec.ts` link test + `invoice-list.component.spec.ts` filter-read test |
| 2 | Click Unpaid / unpaid-amount card -> same Sent set | Yes (composed) | `Status_Sent_returns_every_Sent_invoice_...` + the home link test asserting both cards link to `?status=Sent` |
| 2 | Click Overdue card -> only overdue Sent | Yes (composed) | `Status_Sent_with_overdue_true_...` + home link test |
| 2 | Click Paid this month card -> only this month's Paid | Yes (composed) | `Status_Paid_with_paidMonth_true_...` + home link test |
| 2 | Agent sees the same cards, figures and links (not Admin-only) | Yes | `Both_roles_read_the_same_five_figures_and_nothing_else` (Agent + Admin) |
| 3 | `GET /api/invoices/summary`, either token, shape | Yes | `InvoiceSummaryEndpointTests.Both_roles_...` |
| 3 | `GET /api/invoices/summary`, no token: 401 | Yes | `Without_a_token_it_is_401` |
| 3 | `GET /api/invoices?status=Sent&overdue=true` | Yes | `InvoiceSearchFilterEndpointTests.Status_Sent_with_overdue_true_...` |
| 3 | `GET /api/invoices?status=not-a-status`: 400 naming `status` | Yes | `An_unrecognized_status_is_400_naming_status` |

No missing automated test was found for a row that could be automated. The one genuine gap between the automated suite and section 2 is
that no single Angular test literally clicks a card and watches a second, separately-rendered screen update — each half is proven on its
own (Home builds the right link; the invoice list reads the right query values), matching how every earlier spec's own cross-screen
navigation (e.g. 009's Print link, 010's History link) was verified without a browser-level click-through test either; T031's browser
walkthrough is what confirms the two halves genuinely connect.

## Review disposition (task T032) — 2026-09-27

`api-reviewer`, `angular-reviewer` and `spec-auditor` each reviewed the full implementation.

- **api-reviewer**: one real finding, **fixed**: `InvoiceService.SearchAsync`'s `status` parsing used `Enum.TryParse` directly, which
  (documented .NET behaviour) also accepts an enum's own underlying number as a string — so `?status=0`..`?status=3` silently succeeded
  as `Draft`..`Cancelled` instead of being refused, unlike every other typed list filter in this codebase (`ProductValidator.TryParseUnit`
  already guards against exactly this with an explicit name-comparison loop, which the new invoice code hadn't reused). Fixed by adding
  the equivalent `InvoiceService.TryParseStatus` (exact-name comparison over `Enum.GetValues<InvoiceStatus>()`); added numeric- and
  wrong-case regression cases to `InvoiceServiceSearchFilterTests` and `InvoiceSearchFilterEndpointTests`. Everything else — freshness
  (never cached), Cancelled exclusion, the settling-payment definition, the no-regression guarantee, the ignored-not-erroring pairing
  rule, routing precedence and the route-list pin, and the shared-LocalDB test isolation — was checked and confirmed correct with no
  changes needed.
- **angular-reviewer**: no blocker. Confirmed no client-side recalculation of any figure, the quiet-degradation design for the dashboard
  cards (deliberately different from the invoice list's/history's `role="alert"` + Retry, and a sound distinction since the dashboard is
  supplementary context, not the primary task), the exact `queryParams` per card, `queryFrom`'s graceful handling of a hand-edited
  address, accessibility (real `<a>` elements, text never colour-only, visible focus, consistent `role="status"`), and no `innerHTML`.
  Two minor findings, both **fixed**: (1) the paginator "keeps the active filter" test only inspected the mocked `Router.navigate` call's
  arguments and never exercised Angular's real query-param merge or read back a resulting URL, so its name overclaimed what it proved —
  renamed and commented to describe exactly what it checks (that `onPage` itself never strips the filter or drops `queryParamsHandling:
  'merge'`), matching the pre-existing, unchanged `onPage` behaviour and the same convention already used elsewhere in this codebase; (2)
  the "all-zero" edge case (an empty database) had no dedicated Angular test — added one to `home.component.spec.ts`.
- **spec-auditor**: no genuine mismatch between spec.md and the code across both user stories, every functional requirement and success
  criterion; everything not resolvable from source and tests was correctly marked Unknown, deferred to T031 (the literal click-through,
  and the "same page load" human-perception claim). One observation, not a defect: SC-002's "100% of the time, any combination" is
  demonstrated by a well-chosen but finite set of scenarios (including one all-statuses-at-once seed), not an exhaustive combinatorial
  sweep — reasonable given each figure is an independent, simple `WHERE`-clause query, not a stateful calculation; no change made.

All fixes were re-verified: `dotnet test` for the three backend projects and `ng test --watch=false`/`ng build` all still pass (see
T033).
