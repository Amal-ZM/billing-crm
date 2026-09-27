# Research: Invoice History

Every decision below was made from the spec, the constitution and the code as it stands after 009. No `NEEDS CLARIFICATION` remains (the two
open questions were answered in the spec's Clarifications: no backfill for older invoices with a note on their History, and any signed-in user
may read any invoice's history). The one item the spec left to planning, database-level immutability, is decided in R5 and is confirmed with the
developer at T001.

## R1. Where history entries live: outside the invoice aggregate

**Decision**: an entry is its own record in its own table, `InvoiceHistoryEntries`, with a foreign key to the invoice. `Invoice` does **not**
have a `History` collection and `FindByIdAsync` does **not** load entries. `IInvoiceStore` gains three members: `RecordHistory(entry)` (adds the
entry to the same database context the invoice change is being made in, without saving), `ListHistoryAsync(invoiceId)` (a read, newest first,
no tracking) and `FindNumberAsync(invoiceId)` (the invoice's number, or nothing if it does not exist).

**Rationale**: an invoice's payments and lines are part of what the invoice **is**, so they load with it. History is what happened **to** it:
it only grows, is only appended and read, and would otherwise make every load, edit and payment read and track a list that keeps getting longer.
Recording through the store puts the entry in the same context as the change, so the existing single `SaveChangesAsync` writes both in one
transaction (R7). The aggregate needs no actor and no time for history, so its methods keep their signatures.

**Alternatives considered**: `Invoice.History` as a child collection like payments (loaded and tracked on every request; every aggregate
method would need the actor); a separate `IHistoryStore` with its own save (two saves, so an entry could exist without its change or the other
way round, which FR-002 forbids); domain events dispatched after save (a second, non-atomic step and a new mechanism).

## R2. The current user: a port in Application, an adapter in the API

**Decision**: `ICurrentUser` (Application, `Auth/`) exposes `Actor Current`, where `Actor` is a record of `Email` and `Role` (`StaffRole`).
`InvoiceService` takes an `ICurrentUser` and reads `Current` when it builds an entry. The API implements it as `HttpContextCurrentUser`,
reading the `email` and `role` claims from `IHttpContextAccessor.HttpContext.User` (the token already carries both: `sub`, `email`, `role`; the
JWT bearer setup keeps the raw claim names, `MapInboundClaims = false`). If there is no authenticated user or a claim is missing or not a
known role, `Current` **throws** `InvalidOperationException`: every invoice endpoint requires sign-in, so that is a defect, and an entry
without a user (FR-009) must never be written. No request body, header or query carries an actor.

**Rationale**: FR-009 (the user is the signed-in user, a request cannot claim to be someone else) and SC-002. The service stays free of ASP.NET
types (Clean Architecture: `Application` has no web reference), and a test double is a one-line fake. It is the only way to reach the caller
without adding an `actor` parameter to five service methods and every call site.

**Alternatives considered**: an `Actor` argument on `CreateAsync`, `UpdateAsync`, `SendAsync`, `CancelAsync` and `RecordPaymentAsync`, filled
by each endpoint (explicit, but every endpoint and test call changes and an endpoint could forget it); reading claims inside the service
(`Application` would reference ASP.NET); storing the user's id as well as the email (the spec says email and role; an id would link to an
account the spec wants the entry to be independent of).

**Consequence**: `InvoiceService`'s constructor gains a fifth parameter. The fifteen places that construct it (tests) pass a fake; the API
gets it from dependency injection.

## R3. What an entry stores: text as it was, in one JSON column

**Decision**: `InvoiceHistoryEntries.ChangesJson` (`nvarchar(max)`, `CHECK (ISJSON(ChangesJson) = 1)`) holds a JSON array of
`{ "field": "...", "before": "..." | null, "after": "..." | null }`. Every value is **text**: names as they were, dates as `yyyy-MM-dd`,
money as exact two-decimal text (`100.00`), quantities as two-decimal text, percentages as two-decimal text, status as its stored name. In
the model the list is `IReadOnlyList<HistoryChange>` (Domain record `HistoryChange(Field, Before, After)`), converted to and from JSON by an EF
value converter. The entry also has plain columns for what is queried or checked: `Kind`, `OccurredAtUtc`, `ActorEmail`, `ActorRole`.

**Rationale**: FR-008 (as at the time; later renames change nothing; no recalculation). Text cannot drift when a product or customer changes,
and the screen shows it exactly. One column avoids a second table, a join and a second trigger for a list that is only ever read whole and only
ever appended. The `ISJSON` check stops a malformed row even from raw SQL.

**Alternatives considered**: a child table `InvoiceHistoryChanges` (relational, but a second table to protect, join and order for no query
that needs it); one column per possible field (five kinds with different fields would be mostly empty); storing ids and joining to customers
and products (an entry would change when they do, against FR-008); a full invoice snapshot per entry (large, and the spec wants what changed).

## R4. What each of the five events records

The Application-level `InvoiceHistoryBuilder` (pure, no I/O) turns the facts of an event into the entry's changes. Values use the invariant
culture. The invoice's number is not part of any entry (the History screen is opened on an invoice and shows its number once).

| Event | Kind | Changes (field: before -> after) |
|-------|------|----------------------------------|
| Invoice created | `Created` | `Customer`: none -> name; `Due date`: none -> date; `Notes`: none -> text (only when present); `Discount (SAR)`: none -> amount (only when above 0); and for each line `Line: {product} ({sku})`: none -> `{quantity} x SAR {unit price}, VAT {percent}%` |
| Draft saved with a change | `DraftEdited` | only what changed: `Customer`, `Due date`, `Notes`, `Discount (SAR)` (before -> after; a cleared note shows its text -> none); `Line added: {product} ({sku})`: none -> `{quantity} x SAR {unit price}, VAT {percent}%`; `Line removed: {product} ({sku})`: that text -> none; `Line quantity: {product} ({sku})`: old quantity -> new quantity. One entry per save, several changes in it; **no entry when nothing changed** |
| Sent | `Sent` | `Status`: `Draft` -> `Sent` |
| Payment recorded | `Payment` | `Payment date`, `Payment amount (SAR)`, `Payment method` (each none -> value), `Payment note` (only when present), `Amount paid (SAR)`: before -> after, `Amount remaining (SAR)`: before -> after, and, when the payment settles the invoice, `Status`: `Sent` -> `Paid` (one entry; there is no separate Paid kind) |
| Cancelled | `Cancelled` | `Status`: `Draft` or `Sent` -> `Cancelled` |

The status shown is the **stored** status (`Sent`), never `Overdue`, which is not a state (007). Before and after values of the money that
already exists on the invoice (`Invoice.Paid`, `Invoice.Remaining`, a line's saved price and VAT) are taken from the invoice as it is when the
event happens: the builder formats them, it does not calculate them.

**Rationale**: FR-003 to FR-007 and the edge cases (several changes are one entry; a settling payment is one Payment entry; nothing changed
means nothing recorded). A Draft edit is diffed **in the service**, which already knows the saved lines and the requested lines: it takes a
snapshot of the header and lines before it applies the edit and another after, and the builder compares the two snapshots. A line is
identified by its id (existing lines keep theirs; a removed line is one that is gone; an added line is one that is new).

**Alternatives considered**: one entry per changed field (the spec says one save is one entry); recording the whole invoice each time (the
spec asks for what changed); a sixth `Paid` kind (the spec lists five).

## R5. Immutability: the application offers no way, and the database also refuses

**Decision**: (1) **Application**: there is no method, service operation, route or screen that updates or deletes an entry, and `InvoiceHistoryEntry`
has no setters and no mutating methods; the one write is `RecordHistory` (an insert). (2) **Database**: the migration creates an `INSTEAD OF
UPDATE, DELETE` trigger on `InvoiceHistoryEntries` that raises an error (`THROW 51000, 'Invoice history entries cannot be changed or
deleted.', 1`) and changes nothing, so even a direct `UPDATE` or `DELETE` from any tool is refused. The foreign key to `Invoices` has no cascade
(invoices are never deleted anyway). EF is told about the trigger with `ToTable(t => t.HasTrigger("TR_InvoiceHistoryEntries_Immutable"))` so it
inserts without an `OUTPUT` clause conflict. `Down` drops the trigger, then the table.

**Rationale**: the spec's "Immutable log" and SC-004; it left the database-level part to planning because a trigger is a new kind of database
object (Constitution V), so the developer confirms it at T001. An `INSTEAD OF` trigger is used (not `AFTER` plus rollback) so the refused change
never happens at all. The trigger does not stop someone with administrator rights on the database from dropping it (a recorded gap): nothing
short of a different storage system would.

**Alternatives considered**: application-only immutability (the spec's minimum, but a bug or a direct edit could change history silently);
denying `UPDATE`/`DELETE` permissions to the application's login (the app uses a Windows login on LocalDB and this is not portable to a
developer machine); a `ROWVERSION`/hash chain (tamper-evident, not tamper-proof, and far more machinery); an append-only ledger table (a SQL
Server 2022 feature, not available on LocalDB 2019).

## R6. The read: `GET /api/invoices/{id}/history`

**Decision**: one route in the existing `/api/invoices` group (any signed-in Admin or Agent, no extra policy). It answers `200` with
`{ "number": "INV-000123", "startsWithCreation": true | false, "entries": [ ... ] }`, entries newest first (by `Sequence` descending); each
entry is `{ id, kind, occurredAtUtc, actorEmail, actorRole, changes: [ { field, before, after } ] }`. `startsWithCreation` is `true` when the
invoice has a `Created` entry: an invoice made before this feature has none, and the screen then shows the note that earlier events were not
recorded (FR-012); the browser does not work it out. An unknown or malformed id is the existing `404 invoice_not_found`; no token is `401`.
Reading writes nothing. There is no `POST`, `PUT`, `PATCH` or `DELETE` on history, no `GET` of a single entry and no query string (page, filter,
search, sort are not offered and any that is sent is ignored).

**Rationale**: FR-010, FR-011, FR-012, FR-013. Sending the flag keeps the rule ("no Created entry means recording began later") in one place and
matches the project's "the client shows what the API says" habit (`isEditable`, `acceptsPayment`).

**Alternatives considered**: putting the history inside `GET /api/invoices/{id}` (grows every invoice read); a paged endpoint (the spec shows all
entries; an invoice's life is bounded); the client deriving the note from the list (a second copy of the rule).

## R7. Atomicity: the entry is saved by the same save as the change

**Decision**: each write path of `InvoiceService` records its entry **after** every check has passed and **immediately before** its one
`SaveChangesAsync` (or `AddAsync` for a new invoice, which saves the invoice, its lines and the entry together). In the EF store,
`RecordHistory` adds the entry to the shared `BillingDbContext`, so it is written by the same `SaveChanges`, in the same transaction, as
the invoice row, lines and payments. If that save is refused (`DbUpdateConcurrencyException`, a constraint) the whole transaction, entry
included, rolls back. A `UpdateAsync` that changes nothing does not save and records nothing. The fake stores used by the tests model this:
recorded entries stay **pending** until a successful save commits them and a refused or lost-race save discards them, so the Application
tests can assert both "one entry per saved change" and "none per refused change".

**Rationale**: FR-002 and SC-001. Two stores or two saves could not give this guarantee.

**Alternatives considered**: writing the entry after the save (a crash between the two loses it); a database trigger on `Invoices` writing history
(the trigger has no idea who the user is and would put business rules in the database).

## R8. Angular: a new screen and a link

**Decision**: `InvoiceHistoryComponent` at `/invoices/:id/history`, a normal lazy screen **under the signed-in shell** (it is a working screen, unlike
the print page), with the four states (loading, not found, error with Retry, loaded), reading `InvoiceService.history(id)`. Loaded, it shows the
heading "History" and the invoice number, a link back to the invoice, the note when `startsWithCreation` is false, the empty message when there are
no entries, and the entries newest first: for each the time (`date: 'medium'`), the user's email and role, the kind as words (Created, Draft
edited, Sent, Payment, Cancelled) and a table of its changes (Field, Before, After, with an em dash for none). Values are shown **exactly as
received** (they are already text); the screen formats and calculates nothing. The screen has **no button, form, menu or control that could
change history**: its only controls are the back link and Retry. Text uses `dir="auto"`, amounts and times `dir="ltr"`, long text wraps.
The details screen gets a **History** link (`data-testid="history"`) for every invoice, any status, either role.

**Rationale**: FR-011, FR-015, US2 and US3. Under the shell because a person moves between details, history and payments like any other screen.

**Alternatives considered**: a panel on the details (grows a screen that is already busy, and cannot show a long history well); a dialog (a new
pattern the project avoids); a tab component (a new pattern).

## R9. Order

**Decision**: `Sequence` is a `bigint` **identity** column with a unique index; entries are listed by `Sequence` descending. The time is the
service's clock at the moment of the change (the same value used for the invoice's `UpdatedAtUtc`), shown to the reader but not used to order.

**Rationale**: the spec wants a definite, repeatable order for entries at the same instant ("the order in which the changes were applied"). The
database's own counter is the only order that cannot tie and that follows commit order. An identity column on a non-key column is supported
by SQL Server and by EF (`UseIdentityColumn`).

**Alternatives considered**: ordering by time then id (ties, and a random Guid); a second sequence object (one more object where an identity
column will do; the invoice number sequence is kept for numbers people see).

## R10. Test approach

- **Builder (pure)**: each event's fields, before and after, formats (money `100.00`, quantity, dates, percent), Arabic and English text,
  only-what-changed for an edit (each header field, a line added, removed, a quantity changed, several at once, nothing changed = no entry),
  a settling payment (with `Status`) and a part payment (without), a cleared note, no discount and no notes omitted.
- **Service (fakes)**: one entry per saved change of each kind with the fake current user's email and role (Admin and Agent), the time from the
  clock, no entry for every refusal (locked edit, illegal change, overpayment, validation error, unknown id, lost race, a save that changes
  nothing), values as at the time (rename the customer and reprice the product afterwards: the entry is unchanged), the entry recorded only
  when the save succeeds, `GetHistoryAsync` (order, the flag, 404).
- **API**: the actor is the token's user for an Admin and for an Agent (create, edit, send, pay, cancel, then `GET` history); nothing in any
  request body can set the user; `401`; `404` for an unknown and a non-GUID id; newest first; `startsWithCreation` false for an invoice seeded
  without entries; the pinned route list has exactly one new `GET`; no `POST`, `PUT`, `PATCH` or `DELETE` under `history`.
- **Infrastructure (real SQL)**: the table, columns, checks, index and trigger exist; `UPDATE` and `DELETE` are refused (through EF and as raw
  SQL) and `INSERT` works; a change and its entry are saved together and a lost race leaves neither; a failed constraint leaves neither;
  entries list in `Sequence` order; `ISJSON` refuses a malformed row; an invoice's history survives the customer and product being renamed.
- **Angular**: the History screen's states and content for each kind, the note for an older invoice, the empty message, newest first,
  direction, "no control that changes history" (only Retry and the back link exist), no write call, the History link on the details, the route.

## R11. Docs

`README.md` gains the `AddInvoiceHistory` migration step, the History screen, what is recorded and that it cannot be changed; `CLAUDE.md` gains
a "History (spec 010)" bullet, the `ICurrentUser` port and the layout lines. The `invoice-money` skill needs no change. These are listed tasks, as
for 007 to 009.

## Known gaps

- No backfill for older invoices; no paging, retention or purge; no history for customers, products or staff.
- The user is email and role only.
- A database administrator can drop the trigger or table; the trigger stops the application and casual direct edits. **`TRUNCATE TABLE
  InvoiceHistoryEntries` is a distinct, lower-privilege bypass**: SQL Server does not fire `INSTEAD OF DELETE` triggers for `TRUNCATE`, and
  the table is referenced by nothing else (only the FK it holds to `Invoices`), so a login with `ALTER` on the table — a lesser right than
  dropping the trigger or the table — can silently erase all history without touching the trigger at all (security-reviewer, spec 010).
- No redaction or erasure path for free text once it is written into a history entry (an invoice note or payment note that is later cleared
  or corrected still reads verbatim, forever, in old entries — a deliberate consequence of "as at the time", made permanent by the trigger;
  security-reviewer, spec 010). There is categorically no in-application way to remove sensitive text a staff member typed by mistake.
- History times follow the browser's time zone like the details (the open question from 009).

## Confirmations (task T001, Constitution V) — 2026-09-27

The developer confirmed all the new patterns from plan.md, as recommended:

1. The shape: history entries are their own table outside the invoice aggregate, written through the store's `RecordHistory` in the same save as the change and not loaded with the invoice; each keeps its changes as JSON text in one column (every value text as it was at the time); read by `GET /api/invoices/{id}/history` (newest first, with `startsWithCreation`) and a History screen under the shell reached from a History link on the details.
2. The actor: a new `ICurrentUser` port in Application, implemented in the API from the token's `email` and `role` claims and injected into `InvoiceService` (a fifth constructor argument).
3. The database trigger: `INSTEAD OF UPDATE, DELETE` on the history table, raising an error, created by the migration.

## Quickstart coverage (task T032) — 2026-09-27

Every row of `quickstart.md` sections 3 and 4 walked against the automated suite:

| Section | Row | Automated? | Where |
|---------|-----|------------|-------|
| 3A | Create as Agent, one Created entry | Yes | `InvoiceServiceHistoryTests`, `InvoiceHistoryRecordingEndpointTests`, `InvoiceHistoryEndpointTests` |
| 3A | Edit (due date, quantity, add line), one DraftEdited entry with before/after | Yes | `InvoiceHistoryBuilderTests`, `InvoiceServiceHistoryTests` |
| 3A | Edit that changes nothing, no new entry | Yes | `InvoiceServiceHistoryTests`, `EfInvoiceHistoryRecordingTests` |
| 3A | Send, one Sent entry, newest first | Yes | `InvoiceServiceHistoryTests`, `InvoiceHistoryEndpointTests` (newest-first) |
| 3A | Two payments, settling one shows Sent->Paid, no separate Paid entry | Yes | `InvoiceHistoryBuilderTests`, `EfInvoiceHistoryRecordingTests` |
| 3A | Admin cancels a second invoice, Cancelled entry with Admin's email/role | Yes | `InvoiceServiceHistoryTests`, `InvoiceHistoryRecordingEndpointTests` |
| 3B | Overpayment refused, no entry | Yes | `InvoiceServiceHistoryTests`, `InvoiceHistoryRecordingEndpointTests` |
| 3B | Edit/send of a Sent invoice refused, history unchanged | Yes | `InvoiceServiceHistoryTests`, `InvoiceHistoryImmutabilityEndpointTests` |
| 3B | Rename customer / reprice product, entries unchanged | Yes | `EfInvoiceHistoryRecordingTests`, `EfInvoiceHistoryImmutabilityTests` (real SQL) |
| 3B | Newest first; long/Arabic text wraps and reads in the right direction; amounts/times read left to right | Partly | Ordering and `dir` attributes are asserted (`invoice-history.component.spec.ts`); actual visual wrapping in a real browser is manual only |
| 3C | Older invoice: empty message | Yes | `InvoiceHistoryEndpointTests`, `invoice-history.component.spec.ts` |
| 3C | Cancel/send an older invoice: note shown, no invented Created | Yes | `InvoiceServiceHistoryReadTests` (`StartsWithCreation` false with only later entries), `invoice-history.component.spec.ts` (note) |
| 3C | Unknown/malformed id: "Invoice not found" | Yes | `InvoiceHistoryEndpointTests`, `invoice-history.component.spec.ts` |
| 3C | API down: error + Retry, then Retry loads once it is back | Partly | The stubbed error/Retry mechanics are tested (`invoice-history.component.spec.ts`); actually stopping the real API is manual only |
| 3C | Signed out, opens a History address, sent to sign-in and returned after | Yes | `authGuard`/`roleGuard` tests already cover this generically for every shell child; `invoice-history.routes.spec.ts` confirms the route is a guarded shell child |
| 3C | Agent can open History | Yes | `InvoiceHistoryEndpointTests`, `invoice-history.routes.spec.ts` ("no role guard of its own") |
| 3D | No edit/delete/hide/reorder/add control anywhere | Yes | `invoice-history.component.spec.ts` (T026) |
| 3D | Earlier entries exactly as they were after later events | Yes | `EfInvoiceHistoryImmutabilityTests`, `InvoiceHistoryImmutabilityEndpointTests` |
| 4 | `GET .../history`, both tokens, shape | Yes | `InvoiceHistoryEndpointTests` |
| 4 | `DELETE`/`PUT .../history`: 404/405, nothing changes | Yes | `InvoiceHistoryImmutabilityEndpointTests` |
| 4 | `GET` unknown id: 404 | Yes | `InvoiceHistoryEndpointTests` |
| 4 | `GET` no token: 401 | Yes | `InvoiceHistoryEndpointTests` |
| 4 | `sqlcmd` `SELECT` the latest entries | Yes (equivalent) | `EfInvoiceHistoryStorageTests` reads back through EF; the literal interactive `sqlcmd` session is manual only |
| 4 | `sqlcmd` `UPDATE`/`DELETE`: error 51000, nothing changed | Yes | `EfInvoiceHistoryImmutabilityTests` (raw SQL, not through `sqlcmd.exe` itself, but the same statements against the same trigger) |

No missing automated test was found for a row that could be automated; the only genuinely manual rows are visual rendering in a real browser
and physically stopping/restarting the API process or opening an interactive `sqlcmd` session, none of which a unit or integration test can
stand in for. T033 (developer walkthrough) covers those.

## Review disposition (task T034) — 2026-09-27

`api-reviewer`, `security-reviewer`, `angular-reviewer` and `spec-auditor` each reviewed the full implementation.

- **api-reviewer**: no blocker. Confirmed atomicity on all five write paths, the actor coming only from `ICurrentUser`, invariant-culture
  text formatting, the builder's line-id diff, `Sequence` ordering, the migration/trigger/`Down` order, `startsWithCreation`, the read
  route's authorization, aggregate isolation (history never loads with an invoice) and that no store/service/route can update or delete an
  entry. One cosmetic nit (`FindNumberAsync`'s `(int?)` cast is "slightly implicit" but correct and tested) — no change made.
- **security-reviewer**: no exploitable vulnerability. The actor cannot be spoofed by any header, query string or body field (proven by
  tests); the trigger and the absence of write paths make history append-only end to end for anything short of elevated database rights.
  Two doc-accuracy gaps, both **fixed**: (1) `TRUNCATE TABLE` bypasses the trigger without needing to drop it (a distinct, lower-privilege
  gap from "a DBA can drop the trigger") — added to "Known gaps" above and to the spec-012 deferred list; (2) there is no redaction path
  for a note that is later cleared but still reads verbatim in old entries forever — added to the same two places. Text injection into the
  History screen and log/error leakage were both confirmed clean; no code change was needed for either.
- **angular-reviewer**: solid implementation, one real finding, **fixed**: `history-note` and `history-empty` had no ARIA role, so a screen
  reader would not announce them appearing after the async load resolves, unlike this app's existing `role="status"` convention (e.g.
  `invoice-list.component.ts`'s `list-empty`/`total`, and this component's own loading paragraph). Added `role="status"` to both; the
  component's test suite (28 tests) still passes.
- **spec-auditor**: no genuine mismatch between spec.md and the code across every user story, functional requirement, success criterion
  and edge case; everything not resolvable from source and tests was correctly marked Unknown, deferred to T033 (visual rendering, the
  "under 10 seconds" human-timing claim, the sign-in redirect's visual behaviour, and "all tests currently pass on this developer's
  machine"). One minor, non-blocking gap, **fixed**: no test pinned that an edit touching all 100 lines a Draft may hold (spec 005's
  ceiling) still produces exactly one `DraftEdited` entry; added
  `InvoiceHistoryBuilderTests.An_edit_touching_100_lines_at_once_is_still_one_entry`.

All four fixes were re-verified: `dotnet test` for the three backend projects and `ng test --watch=false` all still pass (see T035).
