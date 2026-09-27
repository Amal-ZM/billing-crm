# Contract: Printable Invoice Page

**Feature**: 009-invoice-print | There is **no API contract**: no endpoint, request or response changes. This contract describes the one
new screen and the one new link. The invoice data it reads is in [data-model.md](../data-model.md).

## Address and access

| Address | Who | Behaviour |
|---------|-----|-----------|
| `/invoices/:id/print` | any signed-in Admin or Agent (`authGuard`) | the printable page for that invoice |
| the same, signed out or session expired | anyone | sent to `/login` (with `returnUrl` back to this page); no invoice data is shown |

The route is a top-level route, **not** a child of the signed-in shell: the application's toolbar, menus and Sign out button are not part
of the page.

## The Print link (invoice details)

`<a data-testid="print">Print</a>` in the details screen's action row, for every invoice in every status and for both roles, pointing at
`/invoices/<id>/print`, with no `target` (it opens in the same tab; a new tab would not have the session).

## Page structure (in this order)

```text
[ on-screen bar, class "no-print" ]   Print button (data-testid="print-button")   Back to invoice link (data-testid="back")
[ document ]
  h1   BillingCRM
  h2   <number>                data-testid="number"        (dir=ltr)
       <status word>           data-testid="status-badge"  Draft | Sent | Overdue | Paid | Cancelled
  dl   Customer                data-testid="customer"      (dir=auto)
       Created                 data-testid="created-at"
       Due date                data-testid="due-date"
       Sent                    data-testid="sent-at"       only when it exists
       Cancelled               data-testid="cancelled-at"  only when it exists
  Notes                        data-testid="notes"         only when present (no empty heading)
  table  Product | SKU | Unit | Unit price | VAT | Quantity | Net | [Discount] | VAT amount
         one row per line      data-testid="line-row"
  totals Total net             data-testid="total-net"
         [Discount]            data-testid="discount"            only with a discount
         [Net after discount]  data-testid="net-after-discount"  only with a discount
         Total VAT             data-testid="total-vat"
         Grand total           data-testid="grand-total"
```

The `data-testid`s are the details screen's own, so the parity test can compare the two pages by id. The other states have their own ids:
`detail-loading` (loading), `detail-error` and `retry` (error), and a heading "Invoice not found" (not found), as on the details.

## Print behaviour (what the browser prints)

| Rule | Detail |
|------|--------|
| Only the document prints | everything inside the element with class `no-print` is hidden in print; the shell is not on the page |
| Colour | black text on white; no background colour or image is needed to read the page; the status is a word |
| Page | margin 15 mm (`@page`, only while this page is on screen); paper size and orientation are the browser's (A4 portrait is the target) |
| Long invoices | the lines continue over pages; the table heading row repeats on every page; a line is never split; the totals block stays with the last lines |
| Wrapping | text cells wrap inside their column; amounts never wrap in the middle of a number |
| Direction | names and notes `dir="auto"`; numbers, dates, SKUs and amounts `dir="ltr"` |

## Behaviour and errors

| Situation | Result |
|-----------|--------|
| Opening the page | one `GET /api/invoices/{id}`; nothing else; the print dialog does not open by itself |
| Print button pressed | the browser's print dialog opens (`window.print()`), once per press |
| Unknown or malformed id | "Invoice not found" (the API's 404), a link back to the invoices; no Print button |
| Any other failure loading | "The invoice could not be loaded." + the API's message when there is one + **Retry**; no Print button |
| Signed out / expired | `/login`, as everywhere |
| Text containing `<`, `>` or `&` | shown as text, never as markup |

## Acceptance checks (used by the tests and the quickstart)

1. INV-000123: every figure and the status on the print page equal the details' (digit for digit).
2. With a discount: the discount, net after discount and each line's discount share are present and equal; without one, none appears.
3. A late Sent invoice reads Overdue on both; Draft, Sent, Paid and Cancelled read as themselves on both.
4. A Cancelled invoice shows its cancelled date; a Sent, Paid or Overdue invoice shows its sent date; a Draft shows neither.
5. The printed output (browser print preview) shows only the document: no toolbar, menus, buttons or back link.
6. A 40-line invoice prints over more than one page with the table heading repeated and no line cut in two.
7. No payments, paid, remaining, ids, contact details or active flag appear.
8. Opening the page and printing change nothing (status and "last changed" time are the same afterwards).
9. Signed out: `/invoices/<id>/print` goes to sign-in.
