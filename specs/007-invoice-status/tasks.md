---

description: "Task list for 007-invoice-status"
---

# Tasks: Invoice Status

**Input**: Design documents from `/specs/007-invoice-status/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/invoice-status-api.md, quickstart.md

**Tests**: Included. FR-014 requires automated tests, and the acceptance checks in contracts/invoice-status-api.md map to test tasks
below. Write each phase's tests first and confirm they fail (a test that does not compile yet counts as failing).

**Organization**: Grouped by user story (US1 send a Draft and lock it, US2 cancel a Draft or Sent invoice (Admin only), US3 see which
invoices are Overdue). The status rules, the frozen amounts, the migration, the row version and the response additions are
Foundational because all three stories need them. **Overdue is worked out by the Foundational response code** (`isOverdue`); US3 proves
it at every layer and adds the screens that show it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run from the repository root unless a task
says otherwise; run frontend commands from PowerShell (`npx --yes npm@11 exec -- ng test --watch=false`). Stop the API and `ng serve`
before `dotnet build`, `dotnet test` or `dotnet ef`; **do not start them again unless the developer asks**. Edit code files with the
Edit and Write tools (shell string replacement has mangled backticks and quotes before).

**Rules that apply to every task**: use the `minimal-api-conventions`, `angular-ui-conventions`, `sql-migrations` and `invoice-money`
skills for matching edits; expected failures (illegal change, locked edit, not found, lost race) are `Result` -> `ProblemDetails`,
never exceptions (only an edit reaching a non-Draft aggregate throws, and the service refuses first); the role is checked **before**
the invoice is looked up (`AuthorizationPolicies.AdminOnly` on cancel); **Overdue is never stored** (no `Overdue` value in the enum,
the database or a request); the legal changes are exactly Draft to Sent, Draft to Cancelled and Sent to Cancelled and exist only in
`InvoiceStatusRules`; **amounts of a non-Draft invoice come only from the frozen columns**; money is `decimal` (`decimal(18,2)`),
never `float`/`double`; the Angular code never re-derives the transition rules (it uses `allowedTransitions`, `isEditable`,
`isOverdue`) and never calculates money; no connection string, key or credential in any committed file; every endpoint declares its
access explicitly; **there is no delete, un-send or reopen operation anywhere**; do not change customer or product behaviour;
Arabic and English text must round-trip unchanged.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the new items in plan.md "Patterns and decisions to confirm": (1) the two action routes `POST /api/invoices/{id}/send` and `POST /api/invoices/{id}/cancel` with no body, cancel behind `AdminOnly` (so an Agent gets 403 before the id is looked up), (2) the state machine in the Domain (`InvoiceStatusRules`, `Invoice.Send`, `Invoice.Cancel`, an aggregate that throws on any edit when not a Draft), (3) Overdue derived and returned as `isOverdue`, never stored, (4) amounts frozen into new columns when an invoice leaves Draft, (5) a `RowVersion` concurrency token on `Invoices`, which turns two changes to the same invoice in the same instant into one winner and one 409 and **replaces 005's "last save wins" for Drafts** (it also closes the gap found in the 006 review), (6) the response additions `isOverdue`, `sentAtUtc`, `cancelledAtUtc`, `allowedTransitions`, `isEditable` (list rows: `isOverdue`), (7) the error contract (409 illegal change and locked edit naming the current status, 400 on `dueDate` for a past-due send), (8) the Angular pieces: an inline two-step confirmation (no dialog), role-gated Cancel, a locked-edit message instead of the form. If (1) or (5) is rejected, stop and re-plan before any code; if (4) is rejected, FR-007 must be relaxed in the spec first. Record the answers in `specs/007-invoice-status/research.md` (a "Confirmations" section like 006's)
- [X] T002 Baseline check before any change: stop the API and `ng serve` if running, then `dotnet test backend/BillingCrm.sln` and (from PowerShell, in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` pass, `sqllocaldb i` lists `MSSQLLocalDB`, and `dotnet ef migrations list --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api` shows `AddInvoiceDiscount` as the last applied migration

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The status rules, the aggregate, the migration, the response additions and the client models.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 [P] Write the Domain tests first. `backend/tests/BillingCrm.Application.Tests/InvoiceStatusRulesTests.cs`: the whole table (each of Draft, Sent, Paid, Cancelled against each requested change Sent, Cancelled, Paid, Draft: only Draft to Sent, Draft to Cancelled and Sent to Cancelled are legal); `NextStatuses` (Draft gives Sent and Cancelled, Sent gives Cancelled, Paid and Cancelled give nothing); the enum has exactly Draft, Sent, Paid, Cancelled (no Overdue). `backend/tests/BillingCrm.Application.Tests/InvoiceStatusTests.cs`: a new invoice is Draft with no `SentAtUtc` or `CancelledAtUtc`; `Send` on a Draft returns true, sets `SentAtUtc` and moves `UpdatedAtUtc`, changes nothing else, and freezes the amounts; `Send` on Sent, Paid or Cancelled returns false and changes nothing; `Cancel` from Draft (no `SentAtUtc`), from Sent (keeps `SentAtUtc`) sets `CancelledAtUtc`; `Cancel` on Paid or Cancelled returns false; `IsOverdue(today)` for Sent before, on and after the due date and for Draft, Paid and Cancelled with a past due date (never overdue); freezing: after `Send` the `Amounts` are the frozen numbers, and changing a line's stored values by reflection or changing the product afterwards does not move them (and a Draft still calculates live); a Cancelled-from-Draft invoice has frozen amounts too; `UpdateDetails`, `AddLine`, `RemoveLine` and `SetLineQuantity` each throw `InvalidOperationException` when the invoice is Sent, Paid or Cancelled and work as before on a Draft; a non-Draft invoice whose lines lack frozen values still reads (falls back to calculating). They fail until T004 and T005
- [X] T004 Widen the status and add the rules: `backend/src/BillingCrm.Domain/InvoiceStatus.cs` becomes `Draft, Sent, Paid, Cancelled` (update its comment: Overdue is not a value); add `backend/src/BillingCrm.Domain/InvoiceStatusRules.cs` per data-model.md and research R2: a static class holding the three legal changes as data, `CanChange(InvoiceStatus from, InvoiceStatus to)` and `NextStatuses(InvoiceStatus from)`. No other file may spell out a legal change (depends on T003)
- [X] T005 Extend the aggregate per data-model.md and research R2, R3, R4, R5. `backend/src/BillingCrm.Domain/InvoiceLine.cs`: `FrozenNet`, `FrozenDiscountShare`, `FrozenVat` (`decimal?`, private setters) and an `internal void Freeze(LineAmounts amounts)`. `backend/src/BillingCrm.Domain/Invoice.cs`: `SentAtUtc`, `CancelledAtUtc` (`DateTime?`, private setters, written as UTC like the other times), `FrozenTotalNet`, `FrozenNetAfterDiscount`, `FrozenTotalVat`, `FrozenGrandTotal` (`decimal?`), `RowVersion` (`byte[]`, private setter, defaults to empty, the EF concurrency token, never used by callers), `IsEditable` (Draft), `IsOverdue(DateOnly today)` (`Status == Sent && DueDate < today`), `Send(DateTime nowUtc)` and `Cancel(DateTime nowUtc)` (use `InvoiceStatusRules`; return false and change nothing when illegal; on success set the status and the time, move `UpdatedAtUtc`, and, when leaving Draft, freeze the amounts from `InvoiceMoney` into the invoice and every line), a private guard that throws `InvalidOperationException("Only a Draft invoice can be edited.")` at the top of `UpdateDetails`, `AddLine`, `RemoveLine` and `SetLineQuantity`, and `Amounts` returning the frozen numbers (totals from the frozen columns, lines from their frozen values, discount from `Discount`) for a non-Draft invoice and the 006 calculation with its never-throws clamp for a Draft; if any frozen value is missing on a non-Draft invoice, fall back to the calculation. Update every existing caller that needs it so the solution builds (depends on T004)
- [X] T006 Add the storage and the migration. `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs`: widen `CK_Invoices_Status` to `IN ('Draft','Sent','Paid','Cancelled')`, map `SentAtUtc` and `CancelledAtUtc` as nullable `datetime2` re-tagged as UTC like `CreatedAtUtc`, the four frozen columns as nullable `decimal(18,2)`, `RowVersion` with `.IsRowVersion()`, and add `CK_Invoices_StatusTimes` and `CK_Invoices_FrozenAmounts` exactly as in data-model.md. `InvoiceLineConfiguration.cs`: the three frozen columns as nullable `decimal(18,2)`. Then `dotnet ef migrations add AddInvoiceStatuses --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api --output-dir Persistence/Migrations`; read the generated file and confirm it drops and re-adds only `CK_Invoices_Status`, adds the columns as nullable (RowVersion `rowversion`), adds the two checks and touches nothing else; never edit it after it is applied. Update `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreCreateTests.cs` (the test that says the status check rejects anything but Draft): `Sent`, `Paid`, `Cancelled` are now accepted with consistent times, and `Overdue`, `Archived` and an empty text are rejected. Extend `MigrationSmokeTests.cs`: the new columns and types, `RowVersion` is `timestamp`, the widened check, both new checks exist. Apply it to the developer's database with `dotnet ef database update` (API stopped) (depends on T005)
- [X] T007 [P] Add the response additions per research R6 and data-model.md. `backend/src/BillingCrm.Application/Invoices/InvoiceModels.cs`: `InvoiceDto` gains `IsOverdue`, `SentAtUtc`, `CancelledAtUtc`, `AllowedTransitions` (status names from `InvoiceStatusRules.NextStatuses(invoice.Status)`) and `IsEditable`; `InvoiceDto.From(invoice, customer, today)` takes today's date; `InvoiceSummary` gains `IsOverdue`; `InvoiceListRow.ToSummary(today)`. `InvoiceService.cs`: `CreateAsync`, `UpdateAsync`, `GetAsync` and `SearchAsync` pass `Today(time)`. Update every existing test that builds or compares these types so the solution builds. `status` stays the stored value (depends on T005)
- [X] T008 [P] Extend `frontend/src/app/core/invoices/invoice.models.ts`: `InvoiceStatus` becomes `'Draft' | 'Sent' | 'Paid' | 'Cancelled'`; `Invoice` gains `isOverdue`, `sentAtUtc`, `cancelledAtUtc` (`string | null`), `allowedTransitions` (`InvoiceStatus[]`) and `isEditable`; `InvoiceListItem` gains `isOverdue`. Update every spec fixture that builds an `Invoice` or `InvoiceListItem` (`invoice.service.spec.ts`, `invoice.service.update.spec.ts`, `invoice-detail.component.spec.ts`, `invoice-form.component.spec.ts`, `invoice-form-edit.component.spec.ts`, `invoice-list.component.spec.ts`) with a Draft's values (`isOverdue: false`, both times `null`, `allowedTransitions: ['Sent', 'Cancelled']`, `isEditable: true`) so `ng test` still compiles
- [X] T009 Run `dotnet build backend/BillingCrm.sln`, `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; everything compiles and passes, including the new Domain tests (depends on T003-T008)

**Checkpoint**: the rules and the aggregate are proven, the migration is applied to the developer's database, and no existing behaviour changed (every invoice is still a Draft).

---

## Phase 3: User Story 1 - Send a Draft invoice (Priority: P1)

**Goal**: A Draft can be sent by any signed-in staff member; it becomes Sent with the time recorded and its amounts frozen; nothing on it can then be edited; illegal changes and locked edits are refused.

**Independent Test**: Create a Draft for ABC with `CNS-001` quantity 2 (230.00), send it: Sent, time shown, 230.00, no Edit or Send action, an edit is refused (`409`).

### Tests for User Story 1 (write first; they fail until the implementation)

- [X] T010 [P] [US1] Add `backend/tests/BillingCrm.Application.Tests/InvoiceServiceStatusTests.cs` (fakes, a `FixedTimeProvider`): `SendAsync` on a Draft returns Sent with `SentAtUtc` at the fixed time, the same number and amounts (230.00 for the acceptance invoice), `isEditable` false, `allowedTransitions` `["Cancelled"]`, one save; `SendAsync` on Sent, Paid and Cancelled is a `Conflict` with code `invoice_status_change_not_allowed` whose detail names the current status ("An invoice that is Sent cannot be sent.") and nothing is saved; on a Sent invoice past its due date the detail says "Overdue"; a Draft whose due date is before today (the Saudi date, at the day boundary too) is a `Validation` error on `dueDate` ("Due date is in the past. Change the due date before sending.") and stays a Draft; a Draft due today is sent; an unknown id is `NotFound`; a store that reports a lost race (`SaveLosesRace`) makes it a `Conflict` `invoice_changed`; `UpdateAsync` on a Sent, Paid or Cancelled invoice is a `Conflict` `invoice_not_draft` ("Only a Draft invoice can be edited. This invoice is Sent.") for every kind of body (changed quantity, added line, removed line, new discount, new customer, new due date, new notes, and even an invalid body) and the aggregate is unchanged; a Sent invoice's amounts stay the same after the product is repriced or deactivated in the fake store
- [X] T011 [P] [US1] Add `backend/tests/BillingCrm.Api.Tests/InvoiceStatusEndpointTests.cs` (and extend `InMemoryInvoiceStore` only if needed): `POST /api/invoices/{id}/send` gives `200` with `"status":"Sent"`, `sentAtUtc`, `isEditable` false, `allowedTransitions` `["Cancelled"]`, the acceptance amounts, for **both** an Admin and an Agent; a second send is `409` with title "Illegal status change" and the status in the detail; a Draft with a past due date (seed it) is `400` with `errors.dueDate`; an unknown and a non-GUID id are `404`; no token is `401`; `PUT` on a Sent invoice with any body is `409` "Invoice is locked" and a `GET` afterwards is identical; a lost race (`InvoiceStore.SaveLosesRace`) is `409` "Invoice changed"; `POST` create, `GET` and the list carry the new fields (`isOverdue`, `sentAtUtc: null`, `cancelledAtUtc: null`, `allowedTransitions` `["Sent","Cancelled"]`, `isEditable` true for a Draft); update `InvoiceAccessTests.cs` so the routes under `/api/invoices` now include `send` and `cancel`, each requiring a signed-in user, and there is still no `DELETE`
- [X] T012 [P] [US1] Add `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStatusTests.cs` (its own `LocalDbFixture`), real SQL: a Draft sent through `InvoiceService.SendAsync` reads back in a new context as Sent with `SentAtUtc`, frozen totals and frozen line amounts equal to the calculated ones; after the product is repriced by SQL the Sent invoice's amounts are unchanged; the checks reject a `Sent` row with no `SentAtUtc`, a `Draft` row with a frozen total, a `Draft` row with a `SentAtUtc`, and a status text `Overdue` (error 547 naming the constraint); **two contexts load the same Draft and both send**: exactly one `SaveChangesAsync` returns true and the other false, and the row is Sent once; **an edit racing a send**: one context loads the Draft and changes a line quantity while another sends it: exactly one saves, and the stored invoice never has status Sent together with a changed quantity
- [X] T013 [P] [US1] Extend the Angular specs. `core/invoices/invoice.service.spec.ts`: `send(id)` POSTs to `/api/invoices/{id}/send` with no body and returns the invoice, the id is encoded. `features/invoices/invoice-detail.component.spec.ts`: a Draft offers Send and Edit and no Cancel for an Agent (Cancel is covered in US2); Send shows the inline question first and calls nothing until **Yes, send**, and **Keep it** closes it; while sending its buttons are disabled and a second click sends nothing; success shows the invoice as Sent with the sent time and no Edit or Send; a `409` shows the API's detail and reloads the invoice; a `400` on `dueDate` shows its message with a link to Edit; a `404` shows "Invoice not found"; a Sent invoice shows no Edit and no Send. `features/invoices/invoice-form-edit.component.spec.ts`: when the loaded invoice has `isEditable` false the form is not shown and "Only a Draft can be edited" appears with a link back to the invoice; a `409` on save (the invoice was sent meanwhile) shows the message at the top and keeps the typed values

### Implementation for User Story 1

- [X] T014 [US1] In `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs` add `SendAsync(Guid id, CancellationToken)`: find (with lines, tracked) -> `NotFound`; not Draft -> `Conflict` `invoice_status_change_not_allowed` (a public factory or constants for the message: "An invoice that is {shown} cannot be sent.", `shown` is Overdue for a late Sent invoice); due date before `Today(time)` -> `Validation` `{ "dueDate": ["Due date is in the past. Change the due date before sending."] }`; `invoice.Send(now)`; `SaveChangesAsync` false -> `ChangedByAnotherPerson`; then `Result.Success(InvoiceDto.From(invoice, customer, Today(time)))`. In `UpdateAsync`, right after the invoice is found and **before** `InspectForEdit`, add: not `IsEditable` -> `Conflict` `invoice_not_draft` ("Only a Draft invoice can be edited. This invoice is {shown}.", title "Invoice is locked"). Expose the messages as public constants next to the existing ones (depends on T005, T007)
- [X] T015 [US1] In `backend/src/BillingCrm.Api/Endpoints/InvoiceEndpoints.cs` map `group.MapPost("/{id}/send", SendAsync)` (inherits the group's `RequireAuthorization()`): parse the id (`404` when it is not a GUID), call the service, `Results.Ok(value)` or `ToProblem()`; update the comment at the top of the file (there is still no delete; status changes are the two action routes). No other endpoint code changes (depends on T014)
- [X] T016 [P] [US1] Add `send(id: string): Observable<Invoice>` to `frontend/src/app/core/invoices/invoice.service.ts` (`POST` to `${base}/${encodeURIComponent(id)}/send` with an empty body)
- [X] T017 [US1] Update `frontend/src/app/features/invoices/invoice-detail.component.ts` per research R8: a **Send** button shown when `allowedTransitions` includes `'Sent'`, an **Edit** link shown only when `isEditable`; a `pending` signal (`'send' | null`) shows the inline question ("Send this invoice? This locks it and it cannot be edited afterwards.") with **Yes, send** and **Keep it**; while the request runs (`acting` signal) the buttons are disabled; on success replace the loaded invoice with the response; on error show a `role="alert"` message (`problemDetail(error)` or a generic text) and reload the invoice so the actions match reality; show the sent time (`sentAtUtc | date:'medium'`) when present. No status rules in the component beyond reading the flags (depends on T013, T016)
- [X] T018 [US1] Update `frontend/src/app/features/invoices/invoice-form.component.ts`: in edit mode, when the loaded invoice is not `isEditable`, set a new load state `'locked'` and show "Only a Draft can be edited." with a **Back to invoice** link instead of the form; a `409` from a save is shown at the top like other refusals (the existing `applyProblemToForm` path already puts a 409's detail in the top message) (depends on T013)
- [X] T019 [US1] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; T010-T013 pass (depends on T014-T018)

**Checkpoint**: US1 acceptance scenarios 1-9 pass via tests; the browser check is in the quickstart.

---

## Phase 4: User Story 2 - Cancel a Draft or Sent invoice (Priority: P2)

**Goal**: An Admin can cancel a Draft or Sent (including Overdue) invoice; an Agent cannot; Paid and Cancelled invoices cannot be cancelled; a Cancelled invoice is final.

**Independent Test**: As an Admin, cancel a Draft and a Sent invoice (both listed as Cancelled); as an Agent the request is `403` and nothing changes; a second cancel is `409`.

### Tests for User Story 2 (write first)

- [X] T020 [P] [US2] Extend `InvoiceServiceStatusTests.cs`: `CancelAsync` from Draft gives Cancelled with `CancelledAtUtc`, no `SentAtUtc`, the same number, lines and amounts (frozen at that moment), `allowedTransitions` empty, `isEditable` false; from Sent keeps `SentAtUtc` and the frozen amounts; from a Sent invoice past its due date (Overdue) it is cancelled; on Paid and Cancelled it is a `Conflict` `invoice_status_change_not_allowed` ("An invoice that is Paid cannot be cancelled.") and nothing is saved; an unknown id is `NotFound`; a lost race is `invoice_changed`; a Cancelled invoice cannot be sent (`409`) or edited (`409 invoice_not_draft`); cancelling does not touch the invoice number (the next invoice created gets the next number, no gap)
- [X] T021 [P] [US2] Extend `InvoiceStatusEndpointTests.cs`: an **Admin** cancels a Draft, a Sent and an Overdue (seeded past due) invoice: `200`, `"status":"Cancelled"`, `cancelledAtUtc` set, amounts kept; an **Agent** gets `403` for an existing Draft, for a Sent invoice, for a Cancelled invoice and for an **unknown and a non-GUID id** (the policy comes first) and a `GET` afterwards shows nothing changed; no token is `401`; a second cancel by the Admin is `409` naming Cancelled; cancel on a Paid invoice (seed it) is `409`; a Cancelled invoice still appears in the list with status Cancelled and its number; a lost race is `409` "Invoice changed"
- [X] T022 [P] [US2] Extend `EfInvoiceStatusTests.cs` on real SQL: cancel from Draft and from Sent round-trips (`CancelledAtUtc`, frozen amounts present, `SentAtUtc` null or kept); the checks reject a `Cancelled` row with no `CancelledAtUtc` and a `Paid` row with no `SentAtUtc`; two contexts both cancel the same Sent invoice: one true, one false; a cancelled invoice keeps its number and lines
- [X] T023 [P] [US2] Extend the Angular specs. `invoice.service.spec.ts`: `cancel(id)` POSTs to `/cancel` with no body. `invoice-detail.component.spec.ts`: as an **Admin** a Draft and a Sent (and Overdue) invoice offer **Cancel**; as an **Agent** Cancel is never offered (the auth service stub returns the Agent role); Cancel shows the inline question ("Cancel this invoice? This cannot be undone.") with **Yes, cancel** and **Keep it**; success shows Cancelled with the cancelled time and no Send, Cancel or Edit; a `403`, `404` and `409` show a message and reload; a Paid or Cancelled invoice offers no Cancel even to an Admin

### Implementation for User Story 2

- [X] T024 [US2] In `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs` add `CancelAsync(Guid id, CancellationToken)`: find -> `NotFound`; `!InvoiceStatusRules.CanChange(status, Cancelled)` -> `Conflict` `invoice_status_change_not_allowed` ("An invoice that is {shown} cannot be cancelled."); `invoice.Cancel(now)`; save false -> `ChangedByAnotherPerson`; return the DTO. The role is **not** checked here (the endpoint policy does it before this runs) (depends on T014)
- [X] T025 [US2] In `InvoiceEndpoints.cs` map `group.MapPost("/{id}/cancel", CancelAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly)` (the mixed-role pattern of `ProductEndpoints`): an Agent is refused with `403` before the id is parsed; update the group comment. The `InvoiceAccessTests` from T011 must now also assert cancel requires the Admin policy (depends on T024)
- [X] T026 [P] [US2] Add `cancel(id: string): Observable<Invoice>` to `frontend/src/app/core/invoices/invoice.service.ts`
- [X] T027 [US2] Update `invoice-detail.component.ts`: a **Cancel** button shown when `allowedTransitions` includes `'Cancelled'` **and** `auth.role() === 'Admin'` (inject `AuthService` like `product-detail.component.ts`; the API still enforces 403); reuse the `pending`/`acting` mechanism from T017 with the question "Cancel this invoice? This cannot be undone." and **Yes, cancel**; show the cancelled time (`cancelledAtUtc | date:'medium'`) when present (depends on T017, T023, T026)
- [X] T028 [US2] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; T020-T023 pass (depends on T024-T027)

**Checkpoint**: US2 acceptance scenarios 1-9 pass via tests; the browser check is in the quickstart.

---

## Phase 5: User Story 3 - See which invoices are Overdue (Priority: P3)

**Goal**: A Sent invoice whose due date is before today (Saudi date) is shown as Overdue everywhere status is shown, with no action by anyone; nothing else is ever shown as Overdue.

**Independent Test**: A Sent invoice due tomorrow shows Sent; one due yesterday shows Overdue in the details and the list; a Draft, Paid or Cancelled invoice with a past due date never does.

### Tests for User Story 3 (write first)

- [X] T029 [P] [US3] Add to `InvoiceServiceStatusTests.cs`: `GetAsync` and `SearchAsync` report `isOverdue` false for Sent due today and true for Sent due yesterday, and **false for Draft, Paid and Cancelled with a past due date**; the Saudi day boundary with a `FixedTimeProvider`: at 20:59 UTC on the due date (23:59 in Riyadh) it is not overdue and at 21:00 UTC (midnight in Riyadh) it is; the same invoice becomes overdue when the clock advances past the boundary without any save (`SaveChangesCalls` unchanged); an Overdue invoice still has `allowedTransitions` `["Cancelled"]`, `isEditable` false, `status` `"Sent"`, and cannot be sent again (`409` naming Overdue)
- [X] T030 [P] [US3] Extend `InvoiceStatusEndpointTests.cs`: `GET /api/invoices/{id}` and `GET /api/invoices` return `isOverdue` true for a seeded Sent invoice due yesterday and false for one due today or tomorrow, and false for a Draft, Cancelled and Paid invoice due yesterday; `status` for the late invoice is still `"Sent"` (never `"Overdue"`); a Sent invoice's amounts are the frozen ones
- [X] T031 [P] [US3] Extend the Angular specs. `invoice-detail.component.spec.ts`: the status badge reads **Overdue** when `isOverdue` is true (and the stored status `'Sent'`), **Sent** when it is false, and the stored status for Draft, Paid and Cancelled; an Overdue invoice offers Cancel to an Admin and no Send. `invoice-list.component.spec.ts`: each row's status cell shows Overdue when `isOverdue` is true and the stored status otherwise; the loading, error-with-retry and empty states are unchanged

### Implementation for User Story 3

- [X] T032 [US3] Update `invoice-detail.component.ts`: the status badge text is `isOverdue ? 'Overdue' : status` (a display choice, not a rule) and gets an "overdue" style (the error-container colours already used for the inactive badge); nothing else changes
- [X] T033 [US3] Update `frontend/src/app/features/invoices/invoice-list.component.ts`: the status cell shows the same text and style; leave paging, the URL query state and the states unchanged
- [X] T034 [US3] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; T029-T031 pass (depends on T032, T033)

**Checkpoint**: US3 acceptance scenarios 1-6 pass via tests.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T035 [P] Update `README.md`: the `AddInvoiceStatuses` migration step, what each status means and the three legal changes, Send (any staff) and Cancel (Admin only) and the inline confirmations, that a Sent invoice is read-only, that Overdue is shown (never stored) for a late Sent invoice, that amounts are frozen when leaving Draft, that two people acting at once get a 409, and what to check by hand (the quickstart's steps); add a Troubleshooting row for "saving or opening an invoice fails with a database error mentioning `SentAtUtc`/`RowVersion`: run `dotnet ef database update ...`"
- [X] T036 [P] Update `CLAUDE.md`: project line (spec 007 added the statuses, the frozen amounts and the row version), the layout (`InvoiceStatusRules` in Domain), the Invoices bullet (`POST {id}/send` any signed-in user, `POST {id}/cancel` Admin only via `AdminOnly` so 403 comes before 404/409; the legal changes; illegal change and locked edit are 409 naming the current status; past-due send is 400 on `dueDate`; Overdue is derived and returned as `isOverdue`; `allowedTransitions` and `isEditable`), the snapshot/amounts bullet (amounts of a non-Draft invoice are the frozen columns), and the concurrency note (`RowVersion`: two changes in the same instant, one 409; 005's "last save wins" no longer applies)
- [X] T037 [P] Secret and hygiene scan of everything committable: no connection-string literal or credential in the new files; no `Overdue` in the enum, a column, a check constraint or any request type (only in derived output and messages); the legal changes are spelled out only in `InvoiceStatusRules` (search `Sent` and `Cancelled` in `backend/src/BillingCrm.Application` and the Angular components for a re-implemented rule); no `float`/`double` on any amount; no arithmetic on money in `frontend/src/app`; no `DELETE`, un-send or reopen route; every `InvoiceEndpoints.cs` route still declares its access and cancel has `AdminOnly`
- [X] T038 Re-run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false` and `ng build`; all pass (SC-009)
- [X] T039 Run the full quickstart validation (`specs/007-invoice-status/quickstart.md`) on the developer's machine, including every browser check (Agent sends a Draft and sees the lock and the missing Cancel, the locked edit address, amounts unchanged after a product change, Admin cancels a Draft and a Sent invoice, the cancelled invoice is final, an invoice made late by the SQL line shows Overdue in the details and the list and can still be cancelled, sign-out redirect) and the API checks (403 for an Agent on cancel including an unknown id, 409 messages, 400 on `dueDate`); the developer starts the servers and confirms it is running (Constitution II)
- [X] T040 Run `api-reviewer`, `angular-reviewer`, `spec-auditor` and **`security-reviewer`**; fix or explicitly record every finding in `specs/007-invoice-status/research.md` (a "Review disposition" section like 006's). Give `api-reviewer` these focus areas: the state machine is spelled out once and every illegal change is a 409 that changes nothing, the `AdminOnly` policy comes before the id lookup (403 for an unknown id), the locked-edit check comes before the body is judged, the frozen amounts are written for the invoice and every line in one save and are what a non-Draft invoice reads, `RowVersion` behaviour (two sends, an edit racing a send, the loser's 409), the Saudi-day boundary of `isOverdue`, the migration and check constraints, and that reading never throws. Give `security-reviewer`: the Admin-only cancel (policy, no way around it through another route, the client's hiding is not the enforcement), that an Agent can never reach a cancelled state, that responses expose no `RowVersion`, and that the new routes add no unauthenticated or CSRF-like exposure. Give `angular-reviewer`: no re-derived transition rules or money arithmetic, role-gated Cancel, the inline confirmation and disabled-while-acting behaviour, error and reload handling, the locked-edit page, accessibility of the confirmation and badges
- [X] T041 Re-run `dotnet test backend/BillingCrm.sln`, `ng test --watch=false` and `ng build` after any review fixes; all pass
- [X] T042 Mark spec 007 done only after T039 and T040 are complete, and update `specs/007-invoice-status/spec.md` status; do not start spec 008 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates all code generation; T002 is independent.
- **Foundational**: T003 first (tests), T004 needs T003, T005 needs T004, T006 needs T005, T007 needs T005, T008 is independent of the back end (parallel with T003-T007), T009 needs T003-T008.
- **US1**: after Foundational. Tests T010-T013 in parallel; T014 needs T005 and T007; T015 needs T014; T016 is independent; T017 and T018 need T013 and T016 (T018 needs only T013); T019 needs T014-T018.
- **US2**: after US1 (it reuses the service, endpoint file, detail component and the confirmation mechanism). Tests T020-T023 in parallel; T024 needs T014; T025 needs T024; T026 is independent; T027 needs T017, T023 and T026; T028 needs T024-T027.
- **US3**: after US2 (the detail and list components are shared); tests T029-T031 in parallel; T032 needs T031; T033 needs T031; T034 needs both. No back-end code is expected (Overdue is built in Foundational, T007); a failure in T029 or T030 is a bug to fix there.
- **Polish**: after all stories. T039 and T040 are the done-ness gate.

### User Story Dependencies

- **US1 (P1)**: needs Foundational only.
- **US2 (P2)**: builds on US1's service, routes and detail screen.
- **US3 (P3)**: verifies the Foundational `isOverdue` and adds its display.

### Within Each Story

Tests first and failing -> implementation -> run tests. Back end: service -> endpoint. Front end: service -> detail / form / list.

### Parallel Opportunities

- Foundational: T008 with T003-T007; T007 with T006.
- US1: tests `T010-T013` together; `T016` with `T014-T015`.
- US2: tests `T020-T023` together; `T026` with `T024-T025`.
- US3: tests `T029-T031` together; `T032` with `T033`.
- Polish: T035-T037.

---

## Parallel Example: User Story 1

```text
# Tests, all at once:
T010 service   T011 API   T012 real SQL (races, checks)   T013 Angular specs

# Then two tracks:
Back end:   T014 -> T015
Front end:  T016 -> T017 || T018
Then:       T019
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001-T002) and Foundational (T003-T009).
2. US1 (T010-T019).
3. Stop and validate: create a Draft for ABC with `CNS-001` quantity 2, send it (as an Agent), see Sent with the time, 230.00 unchanged, no Edit or Send, an edit refused with a `409`. This is the request's stated acceptance scenario.

### Incremental Delivery

1. Add US2 (T020-T028): Admin-only cancel and the final Cancelled status.
2. Add US3 (T029-T034): Overdue shown in the details and the list.
3. Polish, four reviewers, developer verification (T035-T042).

---

## Notes

- T001 is a hard gate; do not generate code before the developer answers. Item (5), the row version, changes an earlier rule.
- Payments and any way to become Paid, credit notes, un-sending, cancellation reasons and history, delivery, status filters and reminders
  belong to later specs. Nothing in this spec makes an invoice Paid; tests set a Paid invoice up directly (through the store).
- The three items deferred to 012-security in spec 004 (decimal JSON converter, `StaffOnly` policy, Development header leak) apply to
  this API too and stay deferred.
- Commit only when the developer asks; there are no commits yet.
