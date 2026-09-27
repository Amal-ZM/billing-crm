# Data Model: Security Hardening

No new domain entity, no new table, no new stored column. One migration adds a trigger only (a database object, not a schema change to
any row shape). Everything else here is a validation rule, a serialization rule, or a piece of configuration.

## `EfInvoiceStore.SaveChangesAsync` (existing method, widened catch, no new type)

Catches a SQL Server deadlock (`DbUpdateException` whose base exception is `SqlException` with `Number == 1205`) alongside the existing
`DbUpdateConcurrencyException`, returning `false` (the existing `invoice_changed` 409) for either, exactly as today's concurrency check
already does. No retry, no new type, no new error code. See research R1b.

## `Payments` (existing table, gains a trigger only)

Unchanged columns. Gains `TR_Payments_Immutable`, an `INSTEAD OF UPDATE, DELETE` trigger (see research R6), the same shape as
`TR_InvoiceHistoryEntries_Immutable` (spec 010). `PaymentConfiguration.cs` gains `ToTable(t => t.HasTrigger("TR_Payments_Immutable"))` so
EF's own `INSERT` (recording a payment) is unaffected. No column, no index, no constraint changes.

## New Application types

- `FreeTextGuard` (`Application/Common/`, static, pure): `bool Refuses(string value)` — true when `value` contains a Unicode
  bidirectional control character (`U+200E`, `U+200F`, `U+202A`-`U+202E`, `U+2066`-`U+2069`) or another C0/C1 control character other than
  tab (`U+0009`), line feed (`U+000A`) or carriage return (`U+000D`). No entity, no state: a pure text check called from validators.

## Validators gaining the guard (existing types, one new rule each)

Each keeps its existing field-error key and its existing length rule; the guard adds one more possible error on the same field, never
replacing what is already there.

| Validator | Field(s) | New rule |
|-----------|----------|----------|
| `CustomerValidator` | `name`, `address` | `FreeTextGuard.Refuses` -> a 400 on that field |
| `ProductValidator` | `name` | `FreeTextGuard.Refuses` -> a 400 on that field |
| `InvoiceValidator` | `notes` | `FreeTextGuard.Refuses` -> a 400 on `notes` |
| `PaymentValidator` | `note` | `FreeTextGuard.Refuses` -> a 400 on `note` |

## New Infrastructure type

- `DecimalJsonConverter : JsonConverter<decimal>` (`Infrastructure/Serialization/`): reads the raw JSON token; throws `JsonException` (the
  existing pipeline's clean-400 path) for exponent notation or more than 28 significant digits; otherwise parses normally. Registered once,
  in `Program.cs`'s `ConfigureHttpJsonOptions`, and so applies to every `decimal`-bound field already in the API without touching any of
  them individually.

## New configuration (no new type)

- **Rate limiting**: one named fixed-window policy, `"login"`, registered in `Program.cs`, applied only to `POST /api/auth/login`.
- **Headers**: one small `app.Use` middleware in `Program.cs` setting `X-Frame-Options: DENY` on every response and `Cache-Control:
  no-store` on every response under `/api`.
- **Body size**: `KestrelServerOptions.Limits.MaxRequestBodySize` set once in `Program.cs`.

## New documentation artifact

- `SECURITY.md` (repository root): Authentication & Sessions; Authorization (operation category -> role, a table); Input Validation;
  Output Encoding; Secrets; Known Gaps (the four items research R-Known-gaps lists, each with its one-line reason and, for closed items,
  a pointer to the enforcing code or test).

## Rules

- Every new refusal that is a genuine business rule (the decimal converter, the free-text guard) is a 400 naming its field, through the
  existing `Result`/`ProblemDetails` path — no new error shape.
- The body-size limit (413) and the rate limiter (429) are enforced by ASP.NET Core middleware before any endpoint runs, and are the one
  documented, narrow exception to the `Result` path (plan.md's Complexity Tracking).
- Nothing here changes what any screen shows or how paging, search or any existing filter behaves; every existing test must still pass
  unchanged.
