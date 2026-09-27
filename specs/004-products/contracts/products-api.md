# Contract: Products API

**Feature**: 004-products | Base address in development: `http://localhost:5080`

Every operation requires a signed-in user (`Authorization: Bearer <token>`). **Reading is open to Admin and
Agent; changing is Admin only.** Signed-out requests get `401` from the auth pipeline (spec 002); an Agent
attempting a change gets `403` (checked before the body is read, so nothing is validated or saved). Errors are
`application/problem+json` (`ProblemDetails`); bodies never contain exception text.

| Operation | Who | Purpose | Success | Refusals |
|-----------|-----|---------|---------|----------|
| `POST /api/products` | Admin | Add a product | `201` + body + `Location: /api/products/{id}` | `400`, `401`, `403`, `409` |
| `GET /api/products` | Admin, Agent | Search / list (paged) | `200` | `400`, `401` |
| `GET /api/products/{id}` | Admin, Agent | One product | `200` | `401`, `404` |
| `PUT /api/products/{id}` | Admin | Edit details | `200` + body | `400`, `401`, `403`, `404`, `409` |
| `PUT /api/products/{id}/active` | Admin | Activate / deactivate | `200` + body | `400`, `401`, `403`, `404` |

There is **no** delete operation (spec FR-012).

## Product (response body)

```json
{
  "id": "5f0c1a2e-...",
  "name": "Consulting hour",
  "sku": "CNS-001",
  "unit": "Hour",
  "unitPrice": 100.00,
  "vatPercent": 15.00,
  "isActive": true,
  "createdAtUtc": "2026-09-24T10:00:00Z",
  "updatedAtUtc": "2026-09-24T10:00:00Z"
}
```

`unitPrice` is SAR. Both numbers are exact decimals, never floating point. `sku` is always the normalized
(upper-case) value.

## POST /api/products and PUT /api/products/{id}

Request (`application/json`):

```json
{ "name": "Consulting hour", "sku": " cns-001 ", "unit": "Hour", "unitPrice": 100, "vatPercent": 15 }
```

`isActive` is **not** accepted here; a new product is active and the state changes only through `/active`.
Text values are trimmed; the SKU is upper-cased (`" cns-001 "` is saved as `CNS-001`).

### Validation rules → 400

| Field | Rule |
|-------|------|
| `name` | required, at most 200 characters |
| `sku` | required, 1-50 characters, only `A-Z` (any letter case in, saved upper-case), `0-9`, `-`, `_` |
| `unit` | required, `Hour`, `Item` or `Service` (any letter case accepted; the response uses the canonical name; numbers are refused) |
| `unitPrice` | required, 0 to 999999999.99, at most two decimal places (`10.005` is refused, `100`, `100.5` and `100.50` are accepted) |
| `vatPercent` | 0 to 100, at most two decimal places. **Add:** empty/absent means 15. **Edit:** required. `0` is kept as 0 |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "detail": "See the errors property.",
  "status": 400,
  "errors": {
    "sku": ["SKU can only contain letters A-Z, digits, hyphens and underscores."],
    "unitPrice": ["Unit price must not have more than two decimal places."]
  }
}
```

Every offending field appears in `errors` (camelCase keys). A malformed or empty body, or a value of the wrong JSON type
(a non-numeric `unitPrice`/`vatPercent`, a number where `name`, `sku` or `unit` should be text), is a `400` problem details
without field names: it cannot even be read into the request.

### Duplicate SKU → 409

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "SKU already in use",
  "status": 409,
  "detail": "Another product already uses this SKU."
}
```

- Applies on add **and** edit, ignoring letter case and surrounding spaces, across active and inactive products.
- Saving a product with its **own** current SKU is not a conflict.
- Nothing is created or changed; the body does not reveal the other product.

### Agent → 403

An Agent's `POST`, `PUT /{id}` or `PUT /{id}/active` returns `403` with title `Forbidden` (the existing
authorization-denied body), whatever the body contains, and changes nothing.

## GET /api/products

Query string (all optional):

| Parameter | Meaning |
|-----------|---------|
| `q` | Fragment matched (contains, ignoring letter case) against `name` **or** `sku`; trimmed, blank = no search; at most 100 characters; `%`, `_`, `[` and `\` match literally |
| `unit` | `Hour`, `Item` or `Service` |
| `status` | `active` (default), `inactive` or `all`. **Admin only has effect**: for an Agent the list is always active-only, whatever is sent |
| `page` | 1-based whole number, default `1` (a very large number gives an empty page; too big for a 32-bit integer is a 400) |
| `pageSize` | default `20`, from 1 to 100 |

Results are ordered by `name`, then `id`. Response is the existing `PagedResult` shape:

```json
{
  "items": [
    { "id": "...", "name": "Consulting hour", "sku": "CNS-001", "unit": "Hour",
      "unitPrice": 100.00, "vatPercent": 15.00, "isActive": true }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

- No match → `200` with `items: []`, `totalCount: 0`, `totalPages: 0`; a page beyond the last → `200`, empty `items`, the real
  `totalCount`.
- Unknown `unit`, unknown `status` (for an Admin), `page`/`pageSize` out of range, or `q` over 100 characters → `400` naming the field.

## GET /api/products/{id}

`200` with the Product body. An unknown or malformed `id` → `404`; an **inactive** product requested by an
**Agent** → the same `404`:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Product not found",
  "status": 404, "detail": "No product exists with this id." }
```

## PUT /api/products/{id}/active

Request: `{ "isActive": false }`; `isActive` is required (`400` if absent or not a boolean). Idempotent:
sending the value the product already has returns `200` and changes nothing (including `updatedAtUtc`). Unknown
`id` → `404`. Admin only.

## UI contract (Angular routes)

| Route | Who | Behaviour |
|-------|-----|-----------|
| `/products` | Admin, Agent | List: debounced search box (name or SKU), unit filter (All/Hour/Item/Service), table with name, SKU, unit, unit price (SAR, two decimals), VAT %, and an **Inactive** marker; paginator (20 default). State in the query string (`q`, `unit`, `status`, `page`, `pageSize`); unsafe values are replaced by defaults. Admin also sees a **Status** filter (Active / Inactive / All) and an **Add product** button; Agents see neither. States: loading (previous rows stay), empty ("No products match"), error with retry |
| `/products/new` | Admin | Form; success goes to the new product's details. An Agent is sent to the existing access-denied page |
| `/products/:id` | Admin, Agent | All fields, created/changed times, badge. Admin also sees **Edit** and **Deactivate/Activate**. Unknown id (or inactive for an Agent) shows "Product not found" |
| `/products/:id/edit` | Admin | Same form pre-filled; success returns to details. Agent → access denied |

Form behaviour: required and format errors per field; VAT percent is pre-filled with 15 on add; `409` puts the API's
message on the **SKU** field; `400` maps `errors` to fields; every refusal keeps the entered values; submit is disabled
while saving. Text inputs and cells use `dir="auto"`; the SKU input is `dir="ltr"`. The shell shows a **Products** link to
every signed-in user.

## Contract tests

API-level (fake store) and integration (real SQL Server) tests must cover, at least:
1. Admin adds `CNS-001`, price 100, VAT 15 → 201 with `Location`, body active, equal timestamps; it appears in the list.
2. VAT omitted on add → 15; VAT `0` on add → 0; VAT omitted on edit → 400.
3. Duplicate SKU on add (exact, other case, surrounding spaces) → 409, count unchanged; same on edit; own SKU on edit → 200;
   an **inactive** product's SKU still conflicts.
4. Each validation rule → 400 naming the field (including price with three decimals, negative price, price above the
   maximum, VAT 100.01, bad SKU characters, Arabic letters in SKU, unknown unit); several bad fields →
   all named; malformed/empty body or a value of the wrong JSON type (for example a number as unit) → 400 without field names.
5. Search by fragment of name and of SKU, case-insensitive, `%`/`_` literal, Arabic name substring, blank `q`; `unit` filter
   alone and combined with `q`.
6. Paging: `pageSize`, `page`, `totalCount`, `totalPages`, stable order; out-of-range → 400; huge page → empty items.
7. Status: default active only; Admin `inactive` / `all`; **Agent with `status=all` still gets active only**; bad `status` → 400
   for Admin.
8. Deactivate/reactivate: leaves and rejoins the default list, fields unchanged, idempotent, timestamps rule; Admin can
   still `GET` and edit an inactive product; Agent `GET /{id}` of an inactive product → 404.
9. **Agent → 403 on POST, PUT /{id}, PUT /{id}/active**, including with a malformed body, and nothing changes; Agent → 200 on both
   GETs of an active product.
10. Unknown id (and non-GUID id) → 404 on get, edit and active. No token → 401 on every operation; every product endpoint
    declares access (existing test).
11. Real database only: unique index rejects a duplicate SKU when the pre-check is bypassed, and is what `TryAdd/TryUpdate`
    report as `false`; check constraints reject negative price, VAT 101 and unknown unit; `100.10` and `999999999.99`
    round-trip exactly; 5,000 products, first search page under 1 second.
