# Research: Payments

Every decision below was made from the spec, the constitution and the code as it stands after 007. No `NEEDS CLARIFICATION` remains
(the two open questions were answered in the spec's Clarifications: payments are permanent, and an invoice with payments cannot be
cancelled).

## R1. Where payments live

**Decision**: `Payment` is a child entity of the `Invoice` aggregate (a `Payments` table, `Invoice.Payments` read-only view over a
private list, exactly like lines). The only way to add one is `Invoice.RecordPayment(...)`. There is no separate payment service or
store: `InvoiceService.RecordPaymentAsync` loads the invoice (with lines and payments), calls the aggregate and saves through the
existing `IInvoiceStore.SaveChangesAsync`.

**Rationale**: "paid", "remaining", the Paid status and the payment rows must change together or not at all, and the rules about them
(only when Sent, never more than remaining, settle to Paid) are the invoice's rules. One aggregate and one save is one transaction with
one concurrency check. The pattern is the lines pattern already approved in 005.

**Alternatives considered**: a `PaymentService` and `IPaymentStore` (a second aggregate: the invoice status would need a cross-store
transaction and the concurrency rule would have to be invented twice); storing a running `PaidAmount` column on `Invoices` (a figure
that can drift from the rows, against Constitution IV's spirit; rejected, see R4).

## R2. The route

**Decision**: `POST /api/invoices/{id}/payments` in the existing `/api/invoices` group (any signed-in user, no extra policy). Body:
`{ "date": "yyyy-MM-dd", "amount": 100.00, "method": "Cash" | "Bank" | "Other", "note": "..." }` (note optional). Success is
`201 Created` with `Location: /api/invoices/{id}` and the **whole updated invoice** as the body. There is no `GET` for payments (they are
part of the invoice) and no `PUT` or `DELETE` (payments are permanent); `InvoiceAccessTests` pins the exact route list, so a stray route
fails a test.

**Rationale**: a payment is a thing that is created under an invoice, so a POST to a sub-resource reads naturally (in MVC terms it is a
`Payments` action on the invoice's controller that takes the invoice id and a small model). Answering with the invoice gives the screen
the new status, paid, remaining and payment list in one call, the same as send and cancel answer with the invoice. `201` with the
invoice's address as `Location` matches how create works in 005 (`Location` points at where the thing can be read).

**Alternatives considered**: `PUT /api/invoices/{id}` with a payments array (would let a payment be edited or removed by omission, the
opposite of permanent); `POST /api/payments` with an `invoiceId` in the body (a new top-level group for something that never exists
outside an invoice); answering with only the payment (the screen would need a second read for the new status and figures); `200 OK`
(the send/cancel routes use 200 because they change something that exists; this creates a row, so 201).

## R3. Status rules: Sent to Paid

**Decision**: `InvoiceStatusRules` gains the fourth legal change, **Sent to Paid**. It is the only change that no person requests: the
aggregate makes it, inside `RecordPayment`, when the remaining reaches exactly 0.00. `Invoice.AllowedTransitions` (what the API reports
as `allowedTransitions`) is the table's next statuses **minus Paid** (nothing offers "mark as paid") and **minus Cancelled while the
invoice has payments** (R5). Paid still moves nowhere; Draft to Paid and Cancelled to Paid stay illegal, and `Send` on a Paid invoice is
the existing illegal change.

**Rationale**: the table stays the single place that says which changes exist, so 007's tests that pin it are updated once (three
changes become four) and nothing else can quietly settle an invoice. Keeping Paid out of `allowedTransitions` keeps the client honest:
it offers Send, Cancel and Edit from flags and would have nothing to offer for Paid anyway.

**Alternatives considered**: not putting Sent to Paid in the table and setting the status directly (a hidden second way to change
status, exactly what 007's "spelled out once" rule prevents); listing Paid in `allowedTransitions` (a lie about what a person can do).

## R4. Paid and remaining: calculated, with Paid as the stored consequence

**Decision**: nothing about the balance is stored except the invoice's status. `Invoice.Paid` is the sum of its payments and
`Invoice.Remaining` is `Amounts.GrandTotal` (the frozen grand total for a non-Draft invoice) minus `Paid`. The arithmetic lives in
`InvoiceMoney` as one small pure method, `Balance(grandTotal, paymentAmounts)`, returning `(Paid, Remaining)`, so the `invoice-money`
rule "the one implementation is `InvoiceMoney`, call it" holds. All amounts already have two decimals, so nothing is ever rounded; a
sum and a difference of two-decimal decimals are exact. Overpaying is impossible by construction: `RecordPayment` refuses an amount
above the remaining.

**Rationale**: a stored running total is a second copy of the truth that two racing saves or a bug could desynchronise. Recalculating
from the rows cannot disagree with them. The frozen grand total (007) is what makes "remaining" stable: a product repricing cannot
change what a customer owes. `InvoiceMoney` is where the money rules already live, so the balance sits with them; it is a method on
an existing class, not a new abstraction.

**Alternatives considered**: `Invoices.PaidAmount` and `Remaining` columns (drift, plus a consistency check that cannot see other
rows); a database view (a new kind of object for two additions).

## R5. Cancelling an invoice that has payments

**Decision**: `CancelAsync` gains one rule, applied after the existing legality check: if the invoice has any payment, refuse with
`409 invoice_has_payments` ("An invoice that has payments cannot be cancelled."). The aggregate's `Cancel` also returns false in that
case (the same "the service refuses first, the aggregate backs it up" arrangement as 007). The order for an Admin's cancel is: not
found, then illegal change (Paid or Cancelled: the existing message), then has-payments. An Agent is still stopped by the policy first.
A Draft has no payments, so it is unaffected.

**Rationale**: this was answered in the spec (Clarifications). Without refunds a Cancelled invoice would hold money with nowhere to say
it is owed back. A Paid invoice cannot be cancelled anyway (Paid is final), so the only new refusal is a partly paid Sent invoice.

**Alternatives considered**: allowing it and keeping the payments (rejected by the developer); refunding automatically (out of scope).

## R6. Concurrency

**Decision**: reuse 007's `RowVersion`. Adding a `Payment` row does not change the invoice row by itself, so the rule already in
`EfInvoiceStore.SaveChangesAsync` ("an invoice whose lines are being written is written too") is extended to payments: an invoice
with an added payment has its `UpdatedAtUtc` marked modified. The save then checks the row version, and a stale one throws
`DbUpdateConcurrencyException`, which the store turns into `false` and the service into the existing `409 invoice_changed`. Also, when
the payment settles the invoice the status changes anyway (the invoice row is written).

**Rationale**: two payments loaded at the same balance both pass the "not more than remaining" check in memory; only the row version
makes the second fail, so the invoice can never end up overpaid (SC-007). A payment racing a cancel is the same mechanism (both write the
invoice row). It is the mechanism approved in 007 and costs three lines.

**Alternatives considered**: a serializable transaction or an application lock (heavier, and a new pattern); a database trigger that
sums payments (a new kind of object and logic in two places).

## R7. Response shape

**Decision**: `InvoiceDto` gains `paid` and `remaining` (SAR, two decimals, always present: for a Draft or Cancelled invoice `paid` is
0.00 and `remaining` is its grand total, which the screens simply do not show), `payments` (each `{id, date, amount, method, note,
recordedAtUtc}`, ordered by the time recorded then id, so the order is total), and `acceptsPayment` (true only when the invoice is Sent,
including Overdue, **and** something remains). `allowedTransitions` comes from the invoice (R3, R5). The list rows are unchanged: they
already show the status, which now can be Paid.

**Rationale**: the client must never derive a rule (007's principle): "may I offer Record payment" is `acceptsPayment`, exactly like
`isEditable` decides Edit. Always sending `paid` and `remaining` keeps the shape uniform.

**Alternatives considered**: sending `payments` only when non-empty (a shape that changes by state); putting `paid` and `remaining`
inside `totals` (that object is the invoice's amounts, unchanged since 006; keeping it separate avoids implying payments are part of
the calculation).

## R8. Errors and the order of checks

**Decision**: for `RecordPaymentAsync` the order is: (1) invoice exists (404 `invoice_not_found`); (2) the invoice is Sent (else `409
invoice_payment_not_allowed`, "An invoice that is {Draft|Cancelled|Paid} cannot take a payment.", naming the shown status; a Sent
invoice is never named Overdue because it is allowed); (3) the body: date, amount, method and note are all checked and **all problems
reported together** as one `400` with keys `date`, `amount`, `method`, `note`; (4) apply and save (a lost race is `409 invoice_changed`).
The status is judged before the body, exactly as 007 judges a locked edit before its body.

Field rules: **amount** required, greater than zero, at most two decimals (refused, never rounded), not more than the remaining (message
"Amount cannot be more than the remaining 130.00 SAR." using invariant culture; when the amount itself is malformed the overpayment
check is skipped); **date** required, `yyyy-MM-dd`, a real date, not after today's Saudi date ("Date cannot be in the future."), not
before the Saudi date the invoice was sent ("Date cannot be before the invoice was sent (2026-09-26)."); **method** required, exactly
`Cash`, `Bank` or `Other`, spelled exactly that way (deliberately stricter than a product's unit, which ignores letter case: a money record should not guess; `cash` is refused); **note** optional, trimmed, at most 1000 characters (the invoice notes limit).
An unreadable body is the framework's 400, as elsewhere. Body binding uses `decimal?` so 100.10 never passes through a float.

**Rationale**: the same conventions as 005 to 007, so the Angular `applyProblemToForm` works unchanged. `amount` carries the overpayment
message because that is the field the person fixes.

**Alternatives considered**: reporting the status refusal together with field errors (mixes a 409 and a 400); rounding a third decimal
(the project refuses, never rounds).

## R9. Angular

**Decision**: one new lazy form screen, `PaymentFormComponent`, at `/invoices/:id/payments/new` (under the signed-in shell, no role
guard: any staff member may record). It loads the invoice for the heading (number, customer name, remaining as returned), shows the
form (date, amount, method as a select, note), and follows the established form pattern: reactive form, amounts held as text and
checked as text with `decimal-text` (new small variant: greater than zero), `applyProblemToForm` for the field errors, inputs kept on a
refusal, submit disabled while a request runs, a top message and a **reload** button after a 409 (as in the edit form), and a message
instead of the form when the invoice does not accept a payment (`acceptsPayment` false: "This invoice cannot take a payment. It is
Paid."). On success it navigates to `/invoices/:id` with `replaceUrl`. The date box is prefilled with the browser's date (the same
`todayText()` idea as the due date) and the server is the judge. The details screen shows **Paid**, **Remaining** and the payments
list for a Sent or Paid invoice, a **Record payment** link only when `acceptsPayment`, the existing Cancel gating from
`allowedTransitions`, and a Paid badge. The list only needs the Paid badge style. No Angular code adds or compares money.

**Rationale**: forms in this app are screens with their own route (customers, products, invoices), so a payment form is the reused
pattern, not a new one; a dialog or an inline form would be a new pattern. The details screen already renders every other amount from
the API, so paid and remaining follow the same rule.

**Alternatives considered**: an inline form on the details page (a new pattern, mixes read and write state, and the 409/locked handling
would have to be duplicated there); a Material dialog (a new component pattern the project deliberately avoids).

## R10. Migration `AddPayments`

**Decision**: one new table `Payments`: `Id` (uniqueidentifier, key, made by the application), `InvoiceId` (foreign key to `Invoices`,
**no cascade**: invoices are never deleted and payment rows must never vanish silently), `PaidOn` (`date`), `Amount` (`decimal(18,2)`),
`Method` (`nvarchar(10)`, stored as its name), `Note` (`nvarchar(1000)` null), `RecordedAtUtc` (`datetime2`, tagged UTC on read like the
other times). Checks: `CK_Payments_Amount` (`[Amount] > 0 AND [Amount] <= 9999999999999.99`, the invoice money ceiling),
`CK_Payments_Method` (`IN ('Cash','Bank','Other')`). Index `IX_Payments_InvoiceId_RecordedAtUtc`. `Invoices` is untouched: 007's
`CK_Invoices_Status`, `CK_Invoices_StatusTimes` and `CK_Invoices_FrozenAmounts` already describe a Paid invoice (sent, not cancelled,
frozen amounts present). The migration is added with the same `dotnet ef` `--project`/`--startup-project`, never edits an applied
one, and needs no data backfill (there are no payments yet).

**Rationale**: every rule the database can hold on its own is a check (positive amount, known method); the one it cannot (payments
total no more than the grand total) is the application's job plus `RowVersion`, recorded in Known gaps.

## R11. Test approach

- **Domain/Application (fakes)**: the transition table (four changes, every other pair illegal); `Invoice.RecordPayment` (partial,
  settling, exact remaining 0.00, one halala over refused, Draft/Cancelled/Paid refused, Paid set only at exactly 0.00, `UpdatedAtUtc`
  moved); `InvoiceMoney.Balance` (exact after many small payments, e.g. 230.00 in 23 x 10.00 and in odd halala amounts);
  `PaymentValidator` (every field rule, all problems together, third decimal, 0, negative, missing, bad method casing, date bounds at
  the Saudi day boundary 20:59/21:00 UTC, note 1000/1001); the service's order of checks (404, then status, then body), the worked
  example 230 = 100 + 130, an Overdue invoice paid down and settled, cancel refused with a payment and allowed without, `allowedTransitions`
  and `acceptsPayment`.
- **API**: 401; 201 with `Location` and body; either role; 404 (unknown and non-GUID id); 409 for Draft, Cancelled, Paid; 400 field
  errors together; overpay by 0.01; status judged before the body; cancel 409 with payments and 403 for an Agent first; the pinned route
  list (exactly one new route, no PUT/DELETE); a repriced product does not change remaining.
- **Infrastructure (real SQL)**: the migration and its checks; a saved payment round trips its date, decimal amount, method and note;
  settling saves the payment and the Paid status in one save; two contexts recording payments at the same balance have exactly one
  winner and the total never exceeds the grand total; a payment racing a cancel has exactly one winner; the check constraints refuse
  a zero amount and an unknown method.
- **Angular (Vitest)**: the form (field messages, kept input, disabled while sending, 409 reload, not-payable message, success
  navigation), the details screen (paid/remaining, list, empty message, link only when `acceptsPayment`, Cancel hidden once a payment
  exists), the service call and encoding, the Paid badge.

## R12. Docs and skills

`README.md` and `CLAUDE.md` gain the migration step, the payment rules and the route. The `invoice-money` skill already says "Payments
cannot exceed remaining. Paid only when remaining == 0"; it should also say that paid and remaining come from `InvoiceMoney.Balance`
and that amounts are frozen when leaving Draft (a sentence that was already stale after 007). These are instruction files, so the
change is made only with the developer's OK.

## Known gaps

- Payments cannot be corrected or reversed; refunds and credit notes are a later spec (and would revisit R5).
- No record of who recorded a payment.
- The database cannot check "payments total at most the grand total"; the application and `RowVersion` do.
- A save that loses a deadlock is a 500 (recorded for spec 012 with the other deferred hardening).

## Confirmations (task T001, Constitution V) — 2026-09-26

The developer confirmed all four groups of new patterns from plan.md, as recommended:

1. Payments belong to the invoice aggregate (`Payments` child table, written only through `Invoice.RecordPayment`); no payment service or store.
2. `POST /api/invoices/{id}/payments` answers `201` with `Location: /api/invoices/{id}` and the whole updated invoice; no `GET`, `PUT` or `DELETE` on payments.
3. Sent to Paid is the fourth legal change but is never offered to a person (`allowedTransitions` omits Paid, no route sets it); paid and remaining come from a new `InvoiceMoney.Balance` and are never stored.
4. A new lazy form route `/invoices/:id/payments/new` like the other forms; the response additions `paid`, `remaining`, `payments`, `acceptsPayment`; the error codes `invoice_payment_not_allowed` and `invoice_has_payments`.

## Review disposition (2026-09-26)

Four read-only reviews ran after implementation (`api-reviewer`, `security-reviewer`, `angular-reviewer`, `spec-auditor`). None found a
Blocker or a Critical defect. One High (Angular) and several Medium items were real and are fixed; everything else is fixed, or recorded
here with the reason. The developer verified the running app (T039) and closed the spec on 2026-09-26.

### api-reviewer (no blockers, no High)

| Finding | Disposition |
|---------|-------------|
| M1: "status before body" is not true for a body the framework cannot bind (malformed JSON, text where a number belongs): that is a plain 400 first | **Documented and pinned**: spec scenario 10 and the contract now say "the values in a readable body"; a test pins the 400 on Draft, Cancelled and Paid. Same behaviour as 007's `PUT`; binding is not restructured |
| M2: the 201 is not proven end to end through HTTP on real SQL | **Recorded**: the endpoint is proven against in-memory stores and the same service against real SQL; the developer's browser run (T039) covers the full path |
| M3: `CLAUDE.md` and the skill do not describe 008 | **Not a defect**: both were updated (T035, T036 with the developer's OK); the reviewer read them before the edits |
| L1 huge-precision JSON amount is shortened by the JSON reader | **Deferred to 012** (the decimal JSON converter); the behaviour is pinned by a test that says so |
| L2 `CK_Payments_Method` is case-insensitive under the default collation | **Recorded**: the application writes and reads exact names; not a guarantee to assume at the database |
| L3 to L7 comment, naming and unreachable-backstop nits | **Fixed** where they overclaimed (test names, the atomicity comment); the backstops are intended |
| L8 known gaps (no database check on the sum, deadlock victim) | **Already recorded** (Known gaps; 012) |

### security-reviewer (no Critical, High or Medium apart from one design decision)

| Finding | Disposition |
|---------|-------------|
| M1: payments are permanent and unattributed, and any Agent can record one | **Decision for the developer, deferred to 012 with send and cancel**: the spec records "no history of who" as an explicit decision (FR-016, Assumptions). Adding `RecordedBy` later cannot backfill existing rows; the developer is told this |
| L1 no request-body size limit | **Deferred to 012** (already on the list; a small limit on the payments route is the cheap first step) |
| L2 no idempotency protection against a retried POST | **Mitigated in the screen** (see Angular H1: after a lost answer Save stays off until the invoice is checked); a server-side key or duplicate guard is **deferred to 012** |
| L3 bidi and control characters in free text (note, invoice notes) | **Deferred to 012**, once for all free-text fields |
| L4 permanence is enforced only by the application, not the database | **Deferred to 012** (revoke UPDATE/DELETE on `Payments`, or a trigger) |
| L5 stale role in a token, HTTP only / `AllowedHosts`, deadlock victim 500 | **Deferred to 012** (already listed); payments make the token-role item weigh more |
| L6 decimal JSON converter | **Deferred to 012**; not exploitable here (no overflow path, culture invariant) |
| L7 missing body-edge API tests | **Fixed**: string amount, `1e400`, wrong types, empty body, `null`, `[]`, `text/plain`, exponent notation, and the long-digit case |

### angular-reviewer (no blockers; one High)

| Finding | Disposition |
|---------|-------------|
| H1: a lost or unknown-outcome response invites a duplicate payment (permanent) | **Fixed**: after no reply or a 5xx the form says the payment may have been recorded, turns Save off and offers "Check the invoice again" (what was typed stays); only then can it be saved again. Tests for 0, 500, 503, the check, and a settle found by the check |
| M1: leaving while a request runs; the Cancel link stays live | **Fixed**: the link is taken away while saving and renamed "Back to invoice"; the `destroyed` guard is now tested |
| M2: Record payment ignores `busy()` and `stale` on the details screen | **Fixed**: hidden while a cancel question is open, a request runs or the page could not be refreshed; test |
| M3: focus may not move after a client-side refusal (zoneless) | **Fixed**: change detection runs before the first invalid field is looked up; tests assert `document.activeElement` for a client refusal and for an API refusal |
| M4: weak tests | **Fixed**: messages asserted inside their own field, the real select and its placeholder, the exact date text, reload resets every field, saving re-enables after a 409, `invoice_has_payments` on the details screen |
| M5: "not payable" message unclear for a Sent invoice with nothing left | **Fixed**: adds "Nothing remains to be paid on it." |
| M6: a 409 or an amount 400 leaves a stale figure at the top | **Fixed**: the invoice is fetched again quietly after a 409 or an amount error; the form gives way to the message if it can no longer take a payment |
| L1 default `conflictField` works by accident | **Commented** at the call |
| L2 `maxlength` hides the note rule | **Fixed**: attribute removed so the message can show |
| L3 to L6, L10, L11, L13 naming, hints, overlapping loads, stale comments, unused parameter | **Fixed** (labels, a date hint and an amount hint, "Saving…", request numbering for loads, the comment, the parameter) |
| L7, L8, L9, L12, L14 | **Recorded, not done**: the status gate matches the contract; the link sits with the other actions; a 401 is handled by the interceptor; the success response is small |

### spec-auditor (no Critical; one High that was a wording gap)

| Finding | Disposition |
|---------|-------------|
| H1: a status refusal did not reload the invoice (spec US3.5, FR-012) | **Fixed** (Angular M6): the invoice is fetched again after a 409 |
| M1: a 1000-character note cannot be typed (`maxlength`) | **Fixed** (Angular L2) |
| M2: several wrong together shows only what the form can check | **Documented** in the quickstart (same convention as 005's invoice form) |
| M3: the quickstart request used a 2030 date, which is in the future | **Fixed**: the example builds its body with today's date |
| M4: no record of consent to edit the instruction files | **Recorded**: `CLAUDE.md` was edited as listed task T035 and the skill only after the developer said yes (T036) |
| M5: repricing tests do not discriminate | **Fixed**: renamed to what they prove; the aggregate and service tests that change a line's stored price prove FR-002 |
| L1 wrong JSON type is a 400 before status | **Documented and pinned** (API M1) |
| L2 README not fully updated | **Fixed**: the API paragraph and the Overdue-cancel sentence |
| L3 stale comments (`InvoiceStatus`, `InvoiceMoney`, test helper, decimal-text header, `mat-select` wording) | **Fixed** |
| L4 tests named for more than they prove; two-context tests are sequential, not truly parallel | **Recorded**: the row version is proven deterministically with two stale copies, which is the reliable way |
| No test that the payment route is registered or redirects when signed out | **Recorded**: covered by the browser run (T039) and the generic guard specs |

### Verification (T041)

`dotnet test`: Application 709, API 471, Infrastructure 222, all pass (each project run on its own). `ng test --watch=false`: 637 pass.
`ng build`: clean. The developer ran `specs/008-payments/quickstart.md` (T039) and confirmed 008 is running and verified.
