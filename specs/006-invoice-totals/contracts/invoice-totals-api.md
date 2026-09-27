# Contract: Invoice Totals (changes to `/api/invoices`)

**Feature**: 006-invoice-totals | No new route. The bodies of the existing add, edit and get operations change. Everything not
mentioned here is exactly as in [005's contract](../../005-invoice-create/contracts/invoices-api.md).

| Operation | Change |
|-----------|--------|
| `POST /api/invoices` | Body may carry `discount`. The `201` body now has `discount`, `totals` and per-line amounts. New `400` keys below. |
| `PUT /api/invoices/{id}` | Same. Because `PUT` sends the **whole desired Draft**, a body without `discount` (or `"discount": null`) clears a saved discount. |
| `GET /api/invoices/{id}` | The `200` body has `discount`, `totals` and per-line amounts, calculated from the saved lines (also for invoices made before this feature). |
| `GET /api/invoices` | **Unchanged** (no amounts in the list). |

Access is unchanged: any signed-in Admin or Agent; no token is `401`.

## Request additions

```json
{
  "customerId": "<guid>",
  "dueDate": "2030-01-31",
  "notes": "Net 30",
  "discount": 20.00,
  "lines": [ { "productId": "<guid>", "quantity": 2 } ]
}
```

- `discount` is optional: absent, `null` or `0` mean no discount. It is a JSON **number** (an amount in SAR), never a percentage,
  never a string. A string or other wrong type is the same generic `400` (no field name) as a wrong-typed `quantity`.

## Response additions

The example is the acceptance case: 2 x 100.00 at 15%, no discount (a `discount` of 20.00 would give `discountShare` 20.00,
`vat` 27.00, `netAfterDiscount` 180.00, `grandTotal` 207.00).

```json
{
  "id": "…", "number": "INV-000123", "status": "Draft",
  "customer": { "id": "…", "name": "ABC", "isActive": true },
  "dueDate": "2030-01-31", "notes": "Net 30",
  "createdAtUtc": "…", "updatedAtUtc": "…",
  "discount": 0.00,
  "totals": {
    "totalNet": 200.00,
    "discount": 0.00,
    "netAfterDiscount": 200.00,
    "totalVat": 30.00,
    "grandTotal": 230.00
  },
  "lines": [
    {
      "id": "…", "position": 1, "productId": "…", "productName": "Consulting hour", "sku": "CNS-001",
      "unit": "Hour", "unitPrice": 100.00, "vatPercent": 15.00, "quantity": 2.00,
      "net": 200.00, "discountShare": 0.00, "vat": 30.00
    }
  ]
}
```

- Every amount (`discount`, everything in `totals`, and each line's `net`, `discountShare`, `vat`) is an exact decimal in SAR
  written as a JSON number with two decimals. Amounts never exceed 9,999,999,999,999.99, so they are exact in any client.
- The rules that produce them are in [data-model.md](../data-model.md) (line net, VAT on the discounted net, sharing of the
  discount, rounding half away from zero once per line). The client shows these numbers and never calculates its own.
- The invariants a client may rely on: `totalNet` = sum of the lines' `net`; `discount` = sum of the lines' `discountShare`;
  `totalVat` = sum of the lines' `vat`; `netAfterDiscount` = `totalNet` - `discount`; `grandTotal` = `netAfterDiscount` + `totalVat`.

## New validation errors (`400`, same `ValidationProblemDetails` shape and "all problems together" rule as 005)

| Key | When | Message |
|-----|------|---------|
| `discount` | negative, or more than two decimal places | Discount must be a number of 0 or more with at most two decimal places. |
| `discount` | above 9,999,999,999,999.99 | Discount cannot be more than 9,999,999,999,999.99. |
| `discount` | more than the total net of the lines this save would produce | Discount cannot be more than the total net of the lines. |
| `lines[i].quantity` | that line's net would be above 9,999,999,999,999.99 | The amount of this line is too large. Reduce the quantity. |
| `lines` | the total net or grand total would be above 9,999,999,999,999.99 | The invoice total is too large. Reduce the quantities or remove lines. |

- The last three keys need valid lines and resolved products to mean anything, so they are checked only when the rest of the
  request has no errors; they are reported (together with each other) once those are fixed. A line that is too large already
  makes the total too large, so in that case only the line (`lines[i].quantity`) is named and `lines` is left out.
- A refused request changes nothing: the invoice, its lines and its saved discount stay exactly as they were.
- A discount equal to the total net is accepted (all amounts after the discount are 0.00).
- No new `404`, `409` or `401` cases. The `409` of 005 (edit clashed with another edit) is unchanged.

## Acceptance checks this contract must satisfy (mapped to tests)

1. `POST` with quantity 2 for a 100.00 / 15% product, no discount: `totals` 200.00 / 0.00 / 200.00 / 30.00 / 230.00; the line's
   `net` 200.00, `vat` 30.00.
2. `POST` with `discount` 20.00: 200.00 / 20.00 / 180.00 / 27.00 / 207.00; line `discountShare` 20.00.
3. `GET` of the invoice returns the same amounts; after an Admin changes the product's price and VAT, still the same.
4. `PUT` changing quantity to 3 keeps the saved price: net 300.00, VAT 45.00, grand 345.00 (with the discount, if any, kept).
5. `PUT` with `discount` omitted after one was saved: discount 0.00.
6. Discount -1, 20.001, 20.10 over a 20.00 net: `400` on `discount`; nothing changed.
7. `PUT` that removes lines so the saved discount is now above the net: `400` on `discount`; nothing changed.
8. A line net above the ceiling: `400` on that line's `quantity`; total above the ceiling: `400` on `lines`.
9. An invoice created before this feature (no discount stored) reads with `discount` 0.00 and correct amounts.
10. No token: `401` on every operation.
