# Contract: Customers API

**Feature**: 003-customers | Base address in development: `http://localhost:5080`

Every operation requires a signed-in user (`Authorization: Bearer <token>`); Admin and Agent have the
same access. Signed-out requests get `401` from the auth pipeline (spec 002). Errors are
`application/problem+json` (`ProblemDetails`); bodies never contain exception text.

| Operation | Purpose | Success | Refusals |
|-----------|---------|---------|----------|
| `POST /api/customers` | Add a customer | `201` + body + `Location: /api/customers/{id}` | `400`, `401`, `409` |
| `GET /api/customers` | Search / list (paged) | `200` | `400`, `401` |
| `GET /api/customers/{id}` | One customer | `200` | `401`, `404` |
| `PUT /api/customers/{id}` | Edit details | `200` + body | `400`, `401`, `404`, `409` |
| `PUT /api/customers/{id}/active` | Activate / deactivate | `200` + body | `400`, `401`, `404` |

There is **no** delete operation (spec FR-009).

## Customer (response body)

```json
{
  "id": "0b6f3c1e-...",
  "name": "شركة الأمل",
  "type": "Company",
  "email": "Accounts@Example.com",
  "phone": "+966 11 555 0100",
  "vatNumber": "300000000000003",
  "address": "12 King Fahd Rd\nRiyadh",
  "isActive": true,
  "createdAtUtc": "2026-09-24T10:00:00Z",
  "updatedAtUtc": "2026-09-24T10:00:00Z"
}
```

`vatNumber` is `null` when the customer has none. `email` keeps the casing as entered.

## POST /api/customers and PUT /api/customers/{id}

Request (`application/json`):

```json
{
  "name": "Acme Trading",
  "type": "Company",
  "email": "accounts@acme.example",
  "phone": "+966 11 555 0100",
  "vatNumber": "300000000000003",
  "address": "12 King Fahd Rd, Riyadh"
}
```

`isActive` is **not** accepted here; a new customer is active, and the state changes only through
`/active`. Every text value is trimmed before validation and before saving.

### Validation rules → 400

| Field | Rule |
|-------|------|
| `name` | required, at most 200 characters |
| `type` | required, `Company` or `Individual` (any letter case is accepted; the response always uses the canonical name) |
| `email` | required, at most 256 characters, shaped like an email address (single `@`, non-empty local part, a dotted domain whose labels are all non-empty, no spaces) |
| `phone` | required, 7-20 characters from digits (any script, so Arabic-Indic numerals are accepted and stored as typed), space, `+`, `-`, `(`, `)` |
| `address` | required, at most 500 characters |
| `vatNumber` | optional; when given, at most 30 characters from letters, digits, space, hyphen; empty means none |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "detail": "See the errors property.",
  "status": 400,
  "errors": {
    "email": ["Email must be a valid email address."],
    "phone": ["Phone is required."]
  }
}
```

Every offending field appears in `errors` (keys are camelCase field names). Malformed or empty JSON
bodies are also `400` problem details.

### Duplicate email → 409

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Email already in use",
  "status": 409,
  "detail": "Another customer already uses this email address."
}
```

- Applies on add **and** edit, compared ignoring letter case and surrounding spaces, across active and
  inactive customers.
- Saving a customer with its **own** current email is not a conflict.
- Nothing is created or changed; the customer keeps its previous values.
- The body does not reveal the other customer's identity or details.

## GET /api/customers

Query string (all optional):

| Parameter | Meaning |
|-----------|---------|
| `q` | Text fragment, matched (contains, ignoring letter case) against `name`, `email`, `phone` and `vatNumber`; trimmed, blank means no search; at most 100 characters after trimming. `%`, `_`, `[` and `` match literally |
| `type` | `Company` or `Individual` |
| `active` | `true` = only active, `false` = only inactive; omitted = both |
| `page` | 1-based whole number, default `1`, at least 1 (very large numbers are fine and give an empty page; a number too big for a 32-bit integer is a 400) |
| `pageSize` | default `20`, from 1 to 100 |

Results are ordered by `name`, then `id`, so pages are stable.

```json
{
  "items": [
    { "id": "...", "name": "Acme Trading", "type": "Company", "email": "accounts@acme.example",
      "phone": "+966 11 555 0100", "vatNumber": null, "isActive": true }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

- No match → `200` with `items: []`, `totalCount: 0`, `totalPages: 0`.
- A page beyond the last, however large the number, returns `200` with `items: []` and the real `totalCount` (it never causes a server error).
- An unknown `type`, a non-boolean `active`, `page < 1` or not a 32-bit integer, `pageSize` outside 1-100, or `q` over 100 characters → `400` with
  the field named in `errors` (`q`, `type`, `active`, `page`, `pageSize`).

## GET /api/customers/{id}

`200` with the Customer body. An unknown or malformed `id` → `404`:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Customer not found",
  "status": 404, "detail": "No customer exists with this id." }
```

## PUT /api/customers/{id}/active

Request: `{ "isActive": false }`. `isActive` is required (`400` if absent or not a boolean).
Idempotent: sending the value the customer already has returns `200` and changes nothing (including
`updatedAtUtc`). A real change updates `updatedAtUtc`. Unknown `id` → `404`.

## UI contract (Angular routes)

| Route | Behaviour |
|-------|-----------|
| `/customers` | List: search box (debounced), type filter (All/Company/Individual), status filter (Both/Active/Inactive), table with name, type, email, phone, VAT number and a clear **Inactive** marker, paginator (page size 20 by default; 10, 20, 50 or 100 selectable). State is in the query string (`q`, `type`, `active`, `page`, `pageSize`) so Back and bookmarks work; out-of-range `page`/`pageSize` values in the address are replaced by safe defaults. While a new page loads the previous rows stay on screen, and a slow older response never replaces a newer one. States: loading, empty ("No customers match"), error with retry |
| `/customers/new` | Form; on success go to the new customer's details |
| `/customers/:id` | All fields, created/changed times, active/inactive badge, **Edit**, **Deactivate/Activate**; unknown id shows "Customer not found" |
| `/customers/:id/edit` | Same form, pre-filled; on success back to details |

Form behaviour: required-field and format errors shown per field; a `409` shows the API's message on
the **email** field; a `400` maps `errors` to fields; every refusal keeps the entered values; the
submit button is disabled while saving. All routes need a signed-in user (existing guard); the shell
shows a **Customers** link to every signed-in user. Text inputs and cells use `dir="auto"`.

## Contract tests

Backend (API level, fake store) and integration (real SQL Server) tests must cover, at least:
1. Add → 201 with `Location`, body, active, equal timestamps; appears in the list.
2. Add duplicate email (exact, different case, surrounding spaces) → 409 with the clear message; count
   unchanged. Same on edit; own email on edit → 200.
3. Each validation rule → 400 naming the field; several bad fields → all named; empty/malformed body → 400.
4. Search by fragment of name, email, phone, VAT number; case-insensitive; `%` and `_` literal; Arabic
   substring; blank `q` = no search.
5. `type` and `active` filters, their combination with `q`; default returns both active and inactive.
6. Paging: `pageSize`, `page`, `totalCount`, `totalPages`, stable order; out-of-range values → 400;
   page beyond the end → empty items.
7. Deactivate and reactivate: customer still listed, fields unchanged, idempotent, timestamps rules.
8. Unknown id (and non-GUID id) → 404 on get, edit, and active.
9. No token → 401 on every operation; every customer endpoint declares its access.
10. Real database only: unique index rejects a duplicate normalized email even if the pre-check is
    bypassed (race); check constraint rejects an invalid `Type`; 10,000 customers, first search page
    returns in under 1 second.
