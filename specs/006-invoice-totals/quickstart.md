# Quickstart: Invoice Totals

**Feature**: 006-invoice-totals | Validation guide; the README is updated with the same steps.
Run from the repository root. Builds on 001 to 005 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-005 working: you can sign in as `admin@crm.local` / `agent1@crm.local` (see CLAUDE.md, "Seed Users"). You need one active
customer (for example **ABC**) and three active products: `CNS-001` (price 100.00, VAT 15), `EXP-001` (price 50.00, VAT 0, for the
mixed-VAT check) and `ODD-001` (price 10.55, VAT 15, for the rounding check). Add them in the app if they do not exist. Having at
least one invoice made before this spec is useful for the "old Draft" check.

## 1. Add the discount column

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies the `AddInvoiceDiscount` migration (column `Invoices.Discount`, default 0, so every existing invoice gets no discount).
No new secrets or settings are needed.

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate in the browser (maps to the spec's acceptance scenarios)

Each block below is its own invoice, so the numbers are easy to follow. "Save" always means the **Save** button on the form.

### A. An invoice made before this spec, and the basic amounts

| Check | Expected |
|-------|----------|
| Open a Draft made in 005 (before this spec), if you have one | It shows the totals block and each line's net and VAT straight away, no discount (US1.7, SC-006) |
| **Invoice 1**: **Create invoice** for **ABC**, `CNS-001`, quantity `2`, no discount, Save | Line net **200.00**, VAT **30.00**; total net 200.00, total VAT 30.00, grand total **230.00 SAR** (US1.1, SC-001, SC-008). The list shows no amounts |
| Invoice 1, **Edit**: **Add line** `EXP-001` (VAT 0) quantity `1`, Save | Line VATs 30.00 and 0.00; total net 250.00, total VAT 30.00, grand total **280.00** (US1.2) |
| **Invoice 2**: `ODD-001` (price 10.55, VAT 15) quantity `1.25`, Save | Line net **13.19** (13.1875 rounded), VAT **1.98**, grand total 15.17 (US1.3) |

### B. The discount

| Check | Expected |
|-------|----------|
| Invoice 1, **Edit**: **Discount in SAR** `30.00`, Save | Discount 30.00, net after discount 220.00; the discount is shared in proportion to the nets 200 : 50, so the lines show discount shares 24.00 and 6.00 and VAT 26.40 and 0.00; total VAT 26.40, grand total **246.40** (US2.2) |
| **Invoice 3**: `CNS-001` quantity `2`, Save (230.00). **Edit**, Discount `20.00`, Save | Discount 20.00, net after discount 180.00, VAT 27.00, grand total **207.00**; the line shows its share 20.00 (US2.1) |
| Invoice 3, Edit: Discount `200` (equal to the net), Save | Accepted; net after discount, VAT and grand total 0.00 (US2.3). Set it back to `20.00` and Save |
| Discount `-1`, `20.001`, `abc`, or `500` (more than the net) | A message on the Discount field; nothing saved; every other value stays on screen (US2.4, US2.7) |
| Invoice 3, Edit: change the quantity to `0.1` (net 10.00) with the 20.00 discount still there, Save | Refused with a message on the Discount field; the invoice is unchanged (US2.5). Put the quantity back |
| Invoice 3, Edit: clear the Discount box, Save | No discount; amounts back to 230.00 (US2.6) |

### C. Amounts follow the saved lines, never the product

| Check | Expected |
|-------|----------|
| As Admin, change `CNS-001` to price `150` and VAT `5`, then reopen Invoice 3 (230.00) | Every amount is unchanged (US3.1, SC-003). Deactivating the product changes nothing either |
| Invoice 3, Edit: quantity `2` to `3`, Save | Net 300.00 (still at the saved 100.00), VAT 45.00, grand total **345.00** (US3.2) |
| Invoice 3, Edit: **Add line** `CNS-001` quantity `1` (reactivate the product first if you deactivated it), Save | The new line uses 150.00 and 5%: net 150.00, VAT 7.50. The first line is unchanged; total net 450.00, total VAT 52.50, grand total **502.50** (US3.3) |
| Sign out and open an invoice | Sent to sign-in (US1.8, SC-007) |

## 4. Validate the API directly

```powershell
# Sign in for a token (the response has accessToken), then:
curl -i -X POST http://localhost:5080/api/invoices -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" -d "{\"customerId\":\"<CUSTOMER-ID>\",\"dueDate\":\"2030-01-31\",\"notes\":\"Net 30\",\"discount\":20.00,\"lines\":[{\"productId\":\"<PRODUCT-ID>\",\"quantity\":2}]}"
```

| Request | Expected |
|---------|----------|
| The call above (product 100.00 at 15%) | `201`; `totals`: totalNet 200.00, discount 20.00, netAfterDiscount 180.00, totalVat 27.00, grandTotal 207.00 |
| The same call without `discount` | `201`; 200.00 / 0.00 / 200.00 / 30.00 / 230.00 |
| `"discount": -1`, `20.001`, or `500` (above the net) | `400` with `errors.discount`; nothing created |
| `PUT /api/invoices/{id}` with `discount` left out | `200`; discount 0.00 |
| `GET /api/invoices/{id}` | The same amounts as saved |
| Any call with no token | `401` |

## 5. Automated

```powershell
dotnet test backend/BillingCrm.sln
cd frontend; ng test --watch=false; ng build
```

All pass (SC-008). The Infrastructure tests need LocalDB and delete only the throwaway `BillingCrm_Tests_*` database they create.
