# Quickstart: Invoice Print

**Feature**: 009-invoice-print | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 008. **No migration and no new setting**: nothing on the server changes.

## Prerequisites

Specs 001-008 working: you can sign in as `admin@crm.local` (Admin) and `agent1@crm.local` (Agent) (see CLAUDE.md, "Seed Users"). You need
one active customer (for example **ABC**) and active products `CNS-001` (price 100.00, VAT 15) and one at a different VAT percent (for
example `AR-1`, price 250.50, VAT 0). Use a current desktop Chrome or Edge for the print preview (Ctrl+P).

## 1. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

(No `dotnet ef` step: 009 has no database change.)

## 2. Validate in the browser (maps to the spec's acceptance scenarios)

### A. The page and the match with the details

| Check | Expected |
|-------|----------|
| Create an invoice for ABC with `CNS-001` quantity 2 and `AR-1` quantity 1.5, discount `20.00`, a due date next week and notes `Net 30` then a second line of text. Open it | The details show the totals, the lines and the status **Draft** |
| Click **Print** on the details | The page opens **in the same tab** at `/invoices/<id>/print`; there is **no toolbar, menu or Sign out** on it; you see a **Print** button and **Back to invoice** at the top (US1.1, US1.4) |
| Read the page | Title **BillingCRM**, the number, the status word, the customer name, Created and Due dates, the notes with their line break, both lines with product, SKU, unit, unit price, VAT %, quantity, net, discount share and VAT, then Total net, Discount, Net after discount, Total VAT and Grand total (US1.2, US2.2) |
| Put the details and the print page side by side (Back, then Print again if needed) | Every amount and the status are **identical**, digit for digit (US2.1, SC-002) |
| Open a second invoice **without** a discount and print it | No discount rows and no discount column (US2.3) |

### B. Every status

| Check | Expected |
|-------|----------|
| **Send** the first invoice, then **Print** | Status **Sent**; a **Sent** date appears (US2.4) |
| Make it late (dev database only): `sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d <your database name>` then `UPDATE Invoices SET DueDate = DATEADD(day, -2, CAST(SYSDATETIME() AS date)) WHERE Number = 123` (use the digits of its number) | Details and print both say **Overdue** (US2.4) |
| Record payments until it is **Paid**, then **Print** | Status **Paid**, full totals, **no** paid, remaining or payment list (US2.4, FR-012) |
| **Cancel** a Draft (as Admin), then **Print** | Status **Cancelled** and a **Cancelled** date (US2.6) |
| As the **Agent**, open any invoice and click **Print** | The page opens (any signed-in user may print) (FR-001) |
| Change `CNS-001`'s price to 150 and rename the customer, reopen **Print** for a Sent invoice | Lines and amounts are unchanged; the customer shows the current name, as on the details (US2.5) |

### C. The paper (browser print preview)

| Check | Expected |
|-------|----------|
| On the print page press **Print** (or Ctrl+P) | The browser's print dialog opens (it did **not** open by itself before you asked) (FR-008) |
| Look at the preview | Only the document: **no** toolbar, menus, buttons or back link; black text on white; the page margin looks even (SC-003, US1.3) |
| Make a long invoice (below) and look at the preview | It runs over more than one page, the heading row (Product, SKU, ...) repeats on each page, no line is cut in two, the Total net ... Grand total block is not alone at the top of a page (US1.5, SC-004) |
| Add a product priced `999999999.99` (the largest price allowed), use it on an invoice with quantity `8000` (a net of about 8 trillion, so with the default 15% VAT the total is about 9.2 trillion, still under the 9,999,999,999,999.99 limit; a bigger quantity is refused when you save) and print | The amount is printed in full, on one line; a line this wide may be scaled down by the browser to fit the page width, and that is acceptable as long as nothing is cut off (Edge cases) |
| Use an Arabic customer name and an Arabic product name | They appear as written and read in the right direction; numbers stay left to right (US2.7) |
| In the print dialog untick **Headers and footers** (Chrome and Edge: More settings) and look at the preview again | The browser's own header and footer (the page address, which contains the invoice id and the host, the date and the page numbers) disappear from the paper. Leave them ticked only for a copy that stays inside the company: they are the browser's, not the page's |
| Choose **Save as PDF** in the print dialog | The browser makes a PDF of the document (this is the browser's feature; the application makes no file) |

**A long invoice, quickly** (40 lines through the API; uses the Admin token and your customer and product ids):

```powershell
$token = "<TOKEN>"; $customer = "<CUSTOMER ID>"; $product = "<CNS-001 PRODUCT ID>"
$due = (Get-Date).AddDays(7).ToString("yyyy-MM-dd")
$lines = 1..40 | ForEach-Object { @{ productId = $product; quantity = $_ } }
@{ customerId = $customer; dueDate = $due; notes = "Long invoice"; lines = $lines } | ConvertTo-Json -Depth 4 -Compress | Set-Content -Encoding utf8 long.json
curl.exe -s -X POST http://localhost:5080/api/invoices -H "Authorization: Bearer $token" -H "Content-Type: application/json" --data-binary "@long.json"
Remove-Item long.json
```

### D. Nothing changes; states; signed out

| Check | Expected |
|-------|----------|
| Note the invoice's **Last changed** time on the details, open **Print**, press **Print**, come back | **Last changed** and the status are the same (FR-011, SC-006) |
| Type `/invoices/00000000-0000-0000-0000-000000000000/print` | "Invoice not found" and a way back; no Print button |
| Stop the API and reload a print page | "The invoice could not be loaded." with **Retry**; start the API and press **Retry**: the page loads |
| Sign out, then open a print address | Sent to sign-in; after signing in you are returned to the print page (US1.6) |

## 3. Automated

```powershell
dotnet test backend/tests/BillingCrm.Application.Tests
dotnet test backend/tests/BillingCrm.Api.Tests
dotnet test backend/tests/BillingCrm.Infrastructure.Tests      # needs LocalDB (unchanged; proves nothing moved)
cd frontend; ng test --watch=false; ng build
```

Run the three backend projects one at a time on a busy machine. All must pass.
