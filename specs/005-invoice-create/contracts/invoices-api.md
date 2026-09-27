# Contract: Invoices API

**Feature**: 005-invoice-create | Base address in development: `http://localhost:5080`

Every operation requires a signed-in user (`Authorization: Bearer <token>`); Admin and Agent have the same access. Signed-out
requests get `401` from the auth pipeline (spec 002). Errors are `application/problem+json` (`ProblemDetails`); bodies
never contain exception text.

| Operation | Purpose | Success | Refusals |
|-----------|---------|---------|----------|
| `POST /api/invoices` | Create a Draft with its lines | `201` + body + `Location: /api/invoices/{id}` | `400`, `401` |
| `GET /api/invoices` | List (newest first, paged) | `200` | `400`, `401` |
| `GET /api/invoices/{id}` | One invoice with its lines | `200` | `401`, `404` |
| `PUT /api/invoices/{id}` | Save the whole Draft (header and lines) | `200` + body | `400`, `401`, `404`, `409` |

There is **no** delete, issue, cancel or totals operation in this spec. Nothing here is Admin-only.

## Invoice (response body)

```json
{
  "id": "6a0e3c1e-...",
  "number": "INV-000123",
  "status": "Draft",
  "customer": { "id": "0b6f3c1e-...", "name": "ABC", "isActive": true },
  "dueDate": "2026-10-15",
  "notes": "Net 30",
  "createdAtUtc": "2026-09-24T10:00:00Z",
  "updatedAtUtc": "2026-09-24T10:00:00Z",
  "lines": [
    {
      "id": "d1c1...",
      "position": 1,
      "productId": "5f0c1a2e-...",
      "productName": "Consulting hour",
      "sku": "CNS-001",
      "unit": "Hour",
      "unitPrice": 100.00,
      "vatPercent": 15.00,
      "quantity": 2.00
    }
  ]
}
```

- `number` is `INV-` plus six digits, fixed at creation. `notes` is `null` when there are none.
- A line's `productName`, `sku`, `unit`, `unitPrice` and `vatPercent` are the **saved copy** from when the line was added; they
  never follow later changes to the product. `customer.name` and `isActive` are the customer's **current** values.
- No totals, VAT amounts or line amounts appear anywhere (spec FR-013).

## POST /api/invoices and PUT /api/invoices/{id}

Request (`application/json`):

```json
{
  "customerId": "0b6f3c1e-...",
  "dueDate": "2026-10-15",
  "notes": "Net 30",
  "lines": [
    { "productId": "5f0c1a2e-...", "quantity": 2 }
  ]
}
```

- **POST**: every line is new: `{ "productId", "quantity" }`. A line with an `id` is refused (`lines[i].id`).
- **PUT** sends the **whole desired Draft**. A line is either **existing** `{ "id": "<line id>", "quantity": 3 }` (only the quantity
  can change; its saved copy is kept) or **new** `{ "productId": "...", "quantity": 1 }` (copied from the product now).
  Existing lines that are not in the list are removed. Exactly one of `id` / `productId` per line.
- `status`, `number`, timestamps and any other property are not accepted here (ignored).
- Text values are trimmed; the request is validated as a whole and saved in one step, so a refusal changes nothing.

### Validation rules → 400 (all problems are reported together)

| Field | Rule |
|-------|------|
| `customerId` | required, a valid id, an existing **active** customer. On PUT, keeping the invoice's current customer is always allowed |
| `dueDate` | required, `yyyy-MM-dd`, a real calendar date, not before today (UTC+03:00). On PUT an unchanged date is always allowed |
| `notes` | optional, at most 1,000 characters after trimming; empty means none |
| `lines` | required, 1 to 100 entries |
| `lines[i].id` | PUT only. Must be a line of this invoice, used at most once. Not allowed together with `productId` |
| `lines[i].productId` | new lines only. Required, a valid id, an existing **active** product |
| `lines[i].quantity` | required, greater than 0, at most 999999.99, at most two decimal places (`0.001` is refused, `1.5` and `1.50` are accepted) |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "detail": "See the errors property.",
  "status": 400,
  "errors": {
    "dueDate": ["Due date cannot be in the past."],
    "lines[0].quantity": ["Quantity must be a number greater than zero with at most two decimal places."],
    "lines[1].productId": ["This product is inactive or does not exist. Choose an active product."]
  }
}
```

- Keys are camelCase field names with array paths (`lines[0].quantity`). A malformed or empty body, or a value of the wrong JSON
  type (a text quantity, a number where text is expected), is a `400` without field names (it cannot be read at all).
- An unknown customer or product id, an inactive customer or product, and a malformed id are all field errors like these; the
  response never says which ids exist.

## GET /api/invoices

Query string (all optional): `page` (1-based whole number, default `1`; a number too big for a 32-bit integer is a `400`;
a huge but valid number gives an empty page) and `pageSize` (default `20`, 1 to 100). Newest first (`createdAtUtc`, then
`number`), so the order is stable.

```json
{
  "items": [
    { "id": "6a0e3c1e-...", "number": "INV-000123", "status": "Draft", "customerId": "0b6f3c1e-...",
      "customerName": "ABC", "dueDate": "2026-10-15", "createdAtUtc": "2026-09-24T10:00:00Z" }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

- No invoices → `200`, `items: []`, `totalCount: 0`, `totalPages: 0`. A page beyond the last → `200`, empty `items`, the real total.
- `page` or `pageSize` out of range → `400` naming the field. There is no search or filter.

## GET /api/invoices/{id}

`200` with the Invoice body (lines in the order added). An unknown or malformed `id` → `404`:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Invoice not found",
  "status": 404, "detail": "No invoice exists with this id." }
```

`PUT` of an unknown or malformed `id` is the same `404`, checked before the *content* of the body is judged. (A body that cannot be read at all, for example malformed JSON, is a `400` first: it is refused before the endpoint runs.)

## UI contract (Angular routes)

| Route | Behaviour |
|-------|-----------|
| `/invoices` | List: number, customer, due date, status, paginator (20 default, 10/20/50/100). State in the query string (`page`, `pageSize`, unsafe values replaced by defaults). Loading (previous rows stay), empty ("No invoices yet" with a **Create invoice** button), error with retry. **Create invoice** button |
| `/invoices/new` | Form: customer picker (active customers), due date, notes, lines. Each line: product picker (active products, name + SKU), quantity, a **Remove** button; **Add line**. Success goes to the new invoice's details |
| `/invoices/:id` | Number, status badge, customer (with an **Inactive** mark if applicable), due date, notes, created/changed times, every line (product name, SKU, unit, unit price in SAR, VAT %, quantity), **Edit**. Unknown id shows "Invoice not found" |
| `/invoices/:id/edit` | The same form, pre-filled from the invoice. Existing lines show their **saved** name and price (read-only) with an editable quantity and **Remove**; new lines use the product picker. Success returns to details |

Form behaviour: required and format errors per field and per line; the API's `errors` (including `lines[i].…` paths) reach the
matching line controls; every refusal keeps everything entered; **Save** is disabled while saving; removing the last line is
blocked with a message; at most 100 lines. Text uses `dir="auto"`, SKU/quantity/prices `dir="ltr"`. The shell shows an
**Invoices** link to every signed-in user.

## Contract tests

Backend (API level, fake store) and integration (real SQL Server) tests must cover, at least:
1. Create for an active customer with `CNS-001` quantity 2 → `201`, `Location`, status `Draft`, exactly one line of quantity 2,
   number `INV-000001` (then `INV-000002` for the next); it appears in the list; the body has no totals.
2. Snapshot: change the product's name, price, VAT and then deactivate it (through the products API) → the invoice still shows
   the old saved values; adding the same product on a later line uses the new values; the same product twice is two lines.
3. Refusals with nothing created: missing/unknown/malformed/inactive customer; missing/unknown/malformed/inactive product; each
   quantity rule (0, -1, 0.001, missing, text, 1000000); due date missing/invalid/past; notes over 1,000; no lines; 101 lines;
   a line `id` on POST; several problems reported together with array-path keys; malformed and empty body.
4. Edit: change quantity (snapshot untouched), add a line, remove a line, change due date/notes/customer, all in one save;
   removing the last line refused; an unchanged past due date accepted; another invoice's line id, a duplicated id, an id
   plus a productId all refused; a refused edit leaves the invoice exactly as it was; a no-change save does not move
   `updatedAtUtc`.
5. Draft with a now-inactive product: existing line kept/edited/removed, new line for it refused; now-inactive customer kept
   allowed, a different inactive customer refused.
6. Numbers: format, first number, strictly increasing, unique under simultaneous creation (real SQL Server, parallel).
7. List: newest first, paging, `totalCount`, huge page empty, bad `page`/`pageSize` `400`, empty list; details 404 for unknown and
   non-GUID ids (also on PUT).
8. No token → `401` on every operation; every invoice endpoint declares access; no `DELETE` route exists.
9. Real database only: sequence never repeats under parallel inserts; check constraints (quantity, price, VAT, unit, status);
   foreign keys; exact decimals (`0.01`, `999999.99`, `100.10`, `999999999.99`); replace-lines semantics (new line inserted,
   removed line deleted, kept line updated); list of 5,000 invoices, first page under 1 second.

## 409: the Draft was changed by someone else (PUT only)

Two people can open the same Draft. If the other person's save gets there first in a way this one cannot be applied on top of
(for example they removed a line this save was going to change), the save is refused as a whole, nothing of it is written, and
the answer is a `409` (never a `500`):

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Invoice changed",
  "status": 409, "detail": "This invoice was changed by someone else. Reload it and try again." }
```

The form shows this message at the top and keeps everything the person typed. Edits that do not touch the same rows (different
lines, or the header) simply both succeed, the later one wins on the header fields it changed; there is no version column in this spec.
