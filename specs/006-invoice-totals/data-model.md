# Data Model: Invoice Totals

**Feature**: 006-invoice-totals | One stored value is added; all other amounts are calculated (research R2).

## Table change: `Invoices`

| Column | Type | Rules |
|--------|------|-------|
| `Discount` | `decimal(18,2)` | **New.** Required, default `0`. Check constraint `CK_Invoices_Discount`: `[Discount] >= 0`. The header discount in SAR, taken off the total net. `0` means no discount. |

- Migration `AddInvoiceDiscount`: adds the column with `defaultValue: 0m` (`NOT NULL`), so every existing invoice gets 0 and reads
  correctly with no backfill; adds the check constraint. Applied with `dotnet ef database update`; never edited once applied.
- **No other column changes.** `InvoiceLines` is untouched: no `Net`, `Vat` or `Total` column exists or may be added (the amounts are
  calculated from `Quantity`, `UnitPrice` and `VatPercent`, which are the saved snapshot from 005).
- "At most two decimals" is the column scale (SQL would round), so the validator refuses a third decimal before saving (as for
  products).
- "Not more than the total net" cannot be a check (it needs the other rows) and is a service rule (R6).

## Domain types

- `Invoice` gains `Discount` (`decimal`, private setter), `UpdateDetails(customerId, dueDate, notes, discount, nowUtc)` (returns true and
  moves `UpdatedAtUtc` only if something really changed, the discount included) and `Amounts` (calls
  `InvoiceMoney.Calculate` over `Lines` in position order). `Invoice.Create` takes the discount too (`0` for a plain create).
- `InvoiceMoney` (static, pure), `AmountLine(Quantity, UnitPrice, VatPercent)`, `LineAmounts(Net, DiscountShare, Vat)`,
  `InvoiceAmounts(Lines, TotalNet, Discount, NetAfterDiscount, TotalVat, GrandTotal)`, constant `MaxAmount`.

## The rules (from the `invoice-money` skill and the spec)

For lines `i = 1..k` in position order, with saved `q_i` (quantity), `p_i` (unit price), `v_i` (VAT percent) and the invoice's
discount `D`:

```text
net_i        = Round(q_i * p_i, 2)                         # once per line, half away from zero
TotalNet     = sum(net_i)
D            must satisfy 0 <= D <= TotalNet, at most two decimals
share_i      = D shared in proportion to net_i, in whole halalas, sum(share_i) = D exactly   (research R4)
vat_i        = Round((net_i - share_i) * v_i / 100, 2)     # VAT on the discounted net, once per line
NetAfterDisc = TotalNet - D
TotalVat     = sum(vat_i)
GrandTotal   = NetAfterDisc + TotalVat
```

Every amount has at most two decimals, and no amount is above `MaxAmount` (9,999,999,999,999.99).

## Worked examples (each is a test vector)

| # | Lines (qty x price @ VAT) | Discount | Line nets | Shares | Line VATs | Total net | Total VAT | Grand |
|---|---------------------------|----------|-----------|--------|-----------|-----------|-----------|-------|
| A | 2 x 100.00 @ 15% | none | 200.00 | 0.00 | 30.00 | 200.00 | 30.00 | **230.00** |
| B | 2 x 100.00 @ 15%; 1 x 50.00 @ 0% | none | 200.00; 50.00 | 0.00; 0.00 | 30.00; 0.00 | 250.00 | 30.00 | 280.00 |
| C | 1.25 x 10.55 @ 15% | none | 13.19 | 0.00 | 1.98 | 13.19 | 1.98 | 15.17 |
| D | 0.5 x 0.05 @ 15% | none | 0.03 (0.025 up) | 0.00 | 0.00 (0.0045 down) | 0.03 | 0.00 | 0.03 |
| E | 2 x 100.00 @ 15% | 20.00 | 200.00 | 20.00 | 27.00 | 200.00 | 27.00 | **207.00** |
| F | 1 x 100.00 @ 15%; 1 x 50.00 @ 0% | 30.00 | 100.00; 50.00 | 20.00; 10.00 | 12.00; 0.00 | 150.00 | 12.00 | 132.00 |
| G | 3 lines net 10.00 each @ 15% | 0.01 | 10.00 x3 | 0.01; 0.00; 0.00 | 1.50; 1.50; 1.50 (see note) | 30.00 | 4.50 | 34.49 |
| H | 2 x 100.00 @ 15% | 200.00 (= net) | 200.00 | 200.00 | 0.00 | 200.00 | 0.00 | 0.00 |
| I | 1 x 0.00 @ 15%; 2 x 100.00 @ 15% | none | 0.00; 200.00 | 0.00; 0.00 | 0.00; 30.00 | 200.00 | 30.00 | 230.00 |

Note on G: line 1 is 9.99 after its share, so its VAT is Round(9.99 x 0.15, 2) = Round(1.4985, 2) = 1.50 (the other two are 1.50
each), total VAT 4.50, net after discount 29.99, so grand = 29.99 + 4.50 = 34.49.

## Request and response shapes (summary; the wire format is in the contract)

- `InvoiceInput(CustomerId, DueDate, Notes, Lines, Discount = null)`; `Discount` is `decimal?`.
- `InvoiceLineDto` gains `Net`, `DiscountShare`, `Vat`.
- `InvoiceDto` gains `Discount` and `Totals` (`InvoiceTotalsDto`: `TotalNet`, `Discount`, `NetAfterDiscount`, `TotalVat`,
  `GrandTotal`).
- `InvoiceSummary` (the list) is unchanged.

## Validation summary

| Field | Rule | When |
|-------|------|------|
| `discount` | null or a number `>= 0` with at most two decimals and `<= MaxAmount` | validator, always |
| `discount` | `<= total net` of the lines the save would produce | service, after lines and products are resolved without errors |
| `lines[i].quantity` | that line's net `<= MaxAmount` | service, same moment |
| `lines` | total net and grand total `<= MaxAmount` | service, same moment |
