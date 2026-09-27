# Data Model: Invoice History

Additions to the 009 model. Everything not listed is unchanged. `Invoices`, `InvoiceLines` and `Payments` gain no column.

## InvoiceHistoryEntry (new entity, table `InvoiceHistoryEntries`)

One permanent record of one change to one invoice. Written once by `InvoiceService` (through the store's `RecordHistory`), in the same save as the
change; never updated or deleted.

| Field | Type | Rules |
|-------|------|-------|
| `Id` | Guid | Key. Made by the application (`Guid.NewGuid`), never by the database |
| `Sequence` | `bigint` identity, unique | Filled by the database on insert; the order in which entries were applied; entries are listed by it, newest (highest) first |
| `InvoiceId` | Guid | Foreign key to `Invoices`, **no cascade** (invoices are never deleted; history must never vanish with one) |
| `Kind` | text, max 20 | Exactly `Created`, `DraftEdited`, `Sent`, `Payment` or `Cancelled` (`CK_InvoiceHistoryEntries_Kind`); the enum `HistoryKind` stored by name |
| `OccurredAtUtc` | datetime2 (UTC) | The service's clock at the moment of the change (the same instant as the invoice's `UpdatedAtUtc`); tagged UTC again on read like the other times |
| `ActorEmail` | nvarchar(256), required | The signed-in user's email address **as it was then** (copied from the token's `email` claim) |
| `ActorRole` | text, max 20 | Exactly `Admin` or `Agent` (`CK_InvoiceHistoryEntries_ActorRole`), as it was then (copied from the token's `role` claim) |
| `ChangesJson` | nvarchar(max), required | JSON array of `{ "field", "before", "after" }`, every value text or null; `CHECK (ISJSON(ChangesJson) = 1)`; in the model an `IReadOnlyList<HistoryChange>` (`Changes`) |

Indexes: `IX_InvoiceHistoryEntries_Sequence` (unique) and `IX_InvoiceHistoryEntries_InvoiceId_Sequence` (serves listing one invoice's history).

**Trigger** `TR_InvoiceHistoryEntries_Immutable`: `INSTEAD OF UPDATE, DELETE`; raises error 51000 "Invoice history entries cannot be changed or
deleted." and changes nothing. `INSERT` is unaffected. Created and dropped by the migration.

**No setters, no mutating methods**: `InvoiceHistoryEntry` is created by `InvoiceHistoryEntry.Create(invoiceId, kind, occurredAtUtc, email, role,
changes)` and read; nothing in Domain, Application or Infrastructure changes one.

## HistoryChange (value in an entry)

`HistoryChange(string Field, string? Before, string? After)`: a named thing that changed, with its value before (null when it did not exist,
such as a Created entry or an added line) and after (null when removed or cleared). All text; money as exact two-decimal invariant text
(`100.00`), dates `yyyy-MM-dd`, quantities and percentages two decimals, statuses their stored names (`Draft`, `Sent`, `Paid`, `Cancelled`).

## HistoryKind (new enum)

`Created`, `DraftEdited`, `Sent`, `Payment`, `Cancelled`. Exactly these five.

## What each kind records (from research R4)

| Kind | Fields |
|------|--------|
| `Created` | `Customer`, `Due date`, `Notes` (when present), `Discount (SAR)` (when above 0), one `Line: {product} ({sku})` per line (after: `{quantity} x SAR {price}, VAT {percent}%`) |
| `DraftEdited` | only the changed ones of: `Customer`, `Due date`, `Notes`, `Discount (SAR)`, and `Line added: ...`, `Line removed: ...`, `Line quantity: ...` |
| `Sent` | `Status` Draft -> Sent |
| `Payment` | `Payment date`, `Payment amount (SAR)`, `Payment method`, `Payment note` (when present), `Amount paid (SAR)` before -> after, `Amount remaining (SAR)` before -> after, `Status` Sent -> Paid (only when it settles the invoice) |
| `Cancelled` | `Status` Draft or Sent -> Cancelled |

## New application types

- `Actor(string Email, StaffRole Role)` and `ICurrentUser { Actor Current { get; } }` (Application, `Auth/`): who is acting. The API's adapter reads the
  token's `email` and `role` claims and throws if either is missing.
- `IInvoiceStore` gains: `void RecordHistory(InvoiceHistoryEntry entry)` (adds to the current unit of work; saved by the next `SaveChangesAsync`
  or `AddAsync`), `Task<IReadOnlyList<InvoiceHistoryEntry>> ListHistoryAsync(Guid invoiceId, CancellationToken)` (newest first, no tracking) and
  `Task<int?> FindNumberAsync(Guid invoiceId, CancellationToken)` (the invoice's number, or null if there is no such invoice).
- Read DTOs: `InvoiceHistoryDto(string Number, bool StartsWithCreation, IReadOnlyList<HistoryEntryDto> Entries)`,
  `HistoryEntryDto(Guid Id, string Kind, DateTime OccurredAtUtc, string ActorEmail, string ActorRole, IReadOnlyList<HistoryChangeDto> Changes)`,
  `HistoryChangeDto(string Field, string? Before, string? After)`.

## Rules

- **Atomic**: an entry is recorded only after every check has passed, immediately before the one save; that save writes the change and the entry in one
  transaction. A refused, failed or lost-race save writes neither.
- **As at the time**: values are built from the invoice and the request as they are at the moment of the change, formatted as text; they are never
  recalculated, and never re-read from a customer, product or user when the history is shown.
- **`startsWithCreation`** is true exactly when the invoice's entries include a `Created` entry.
- **Nothing else writes an entry**: seeding and tests that put an invoice in a store directly write none (that is how an older invoice looks).
