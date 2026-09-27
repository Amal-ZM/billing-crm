---
name: invoice-money
description: Use when calculating invoice lines, VAT, discount, payments, or remaining balance.
---
# Money
- Line net = Round(qty * unitPrice, 2). Line VAT = Round((net - its discount share) * vatPercent/100, 2).
- Rounding is half away from zero (MidpointRounding.AwayFromZero), once per line for net and once per line for VAT; totals are plain sums of already-rounded amounts, never rounded again.
- Header discount is a fixed SAR amount on the net, taken off before VAT: it is shared over the lines in proportion to their net (whole halalas, largest remainder, ties to the earlier line, shares add up to the discount exactly), and each line's VAT is on its own discounted net. It cannot be negative or more than the total net. Document it in code comments.
- Grand total = net after discount + total VAT, 2 decimals, SAR. Never use double/float.
- The one implementation is `InvoiceMoney` in the Domain; call it, do not re-implement the rules. Amounts are calculated from what each line saved while the invoice is a Draft, then frozen (stored) when it leaves Draft, and never calculated in Angular.
- Paid and remaining come from `InvoiceMoney.Balance(grandTotal, paymentAmounts)`: paid is the plain sum of the payments, remaining is the (frozen) grand total minus it. Exact two-decimal arithmetic, never rounded, never stored, never calculated in Angular.
- Payments cannot exceed remaining (an overpayment is refused, never trimmed). Paid only when remaining == 0.