# BillingCRM

Internal staff CRM covering customers, products and invoices (money is always `decimal(18,2)`,
currency SAR). This repository currently contains the foundation (spec `001-foundation`), staff
sign-in with two roles (spec `002-staff-auth`), customers (spec `003-customers`) and products
(spec `004-products`), Draft invoices (spec `005-invoice-create`), their amounts (spec
`006-invoice-totals`), invoice status and payments (specs `007-invoice-status`, `008-payments`), a
printable invoice (spec `009-invoice-print`) and a permanent invoice history (spec
`010-invoice-history`): a .NET 8 API, an Angular + Material app, and a LocalDB database holding the staff accounts,
the customers, the products, the invoices and their history. An invoice stores each line's quantity, unit price and VAT percent and
an optional discount; the line amounts, VAT and totals are calculated from those every time and shown in SAR.

- `backend/` — .NET 8 solution (`Domain`, `Application`, `Infrastructure`, `Api`) and tests
- `frontend/` — Angular (standalone components) + Angular Material
- `specs/` — Spec Kit specs; `.specify/memory/constitution.md` holds the project rules
- `SECURITY.md` — what's enforced (auth, authorization, input validation, output encoding, secrets) and what's
  a documented, open gap

## Prerequisites

| Tool | Version | Check |
|------|---------|-------|
| .NET SDK | 8.x | `dotnet --version` |
| Node.js | 20.19+ / 22.12+ / 24 | `node -v` |
| npm | 11+ recommended (see note) | `npm -v` |
| Angular CLI | 21.x | `ng version` |
| SQL Server LocalDB | instance `MSSQLLocalDB` | `sqllocaldb i` |

> **npm 9 and 10 cannot resolve the Angular 21 dependency tree from scratch**: without a
> lock file, or when adding/updating packages, they fail with
> `Cannot read properties of null (reading 'edgesOut')`. `npm install` from the committed
> `frontend/package-lock.json` works with npm 9. Whenever you add or update a package, use npm 11+:
> `npm install -g npm@11`, or prefix the command with `npx npm@11` (for example
> `npx npm@11 install <package>`) to avoid a global change.

## Configure the database connection

The connection string is **never committed**. Supply it on your machine only, using user-secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your LocalDB connection string>" --project backend/src/BillingCrm.Api
```

Use a LocalDB instance and a database named `BillingCrm`, in the form
`Server=(localdb)\<instance>;Database=BillingCrm;Trusted_Connection=True;TrustServerCertificate=True`.

Alternatively set the environment variable `ConnectionStrings__DefaultConnection` (environment
variables override user-secrets).

User-secrets are only read when the API runs in the `Development` environment. Plain
`dotnet run` (below) uses the `http` launch profile, which sets it for you.

If the setting is missing, the API stops at start-up with:
`Required setting 'ConnectionStrings:DefaultConnection' is missing...`

## Configure the signing key

Sign-in tokens are signed with a secret key. Like the connection string it is **never committed**;
generate a random one and keep it in your user-secrets. Do this **before** the next step, because
`dotnet ef` starts the API and the API refuses to start without it:

```powershell
$key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:SigningKey" $key --project backend/src/BillingCrm.Api | Out-Null
```

(`Out-Null` keeps the key from being echoed to the console.) Or set the environment variable
`Jwt__SigningKey`. The key must be at least 32 bytes. If it is missing or too short the API stops
at start-up with a message naming `Jwt:SigningKey` (never the value).

Non-secret token settings live in `backend/src/BillingCrm.Api/appsettings.json` under `Jwt`; the
session lasts 60 minutes by default (`Jwt:ExpiryMinutes`, or the environment variable
`Jwt__ExpiryMinutes`).

## Create the database tables

```powershell
dotnet tool restore
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

This creates the `BillingCrm` database if needed and applies the migrations, which currently create
the `StaffUsers` table, (migration `AddCustomers`) the `Customers` table, (migration `AddProducts`) the
`Products` table and (migration `AddInvoices`) the `Invoices` and `InvoiceLines` tables plus the database
sequence `InvoiceNumbers` that hands out invoice numbers, (migration `NoCacheInvoiceNumbers`) makes that sequence
skip no numbers after a restart, (migration `AddInvoiceDiscount`) adds the invoice discount column, (migration `AddInvoiceStatuses`) adds the Sent, Paid and Cancelled statuses, the sent and cancelled times, the frozen amounts and a row version, (migration `AddPayments`) adds the `Payments` table, (migration `AddInvoiceHistory`) adds the `InvoiceHistoryEntries` table together with the database
trigger `TR_InvoiceHistoryEntries_Immutable` that refuses any `UPDATE` or `DELETE` on it, (migration `AddProductUnitPriceUpperBound`) widens
the product price check to also refuse anything above 999,999,999.99, and (migration `AddPaymentsImmutability`) adds the trigger
`TR_Payments_Immutable`, the identical guarantee applied to the `Payments` table. Until you do this, `/health` reports the database as
`Unhealthy`, and the customer, product and invoice screens show an error.

## Run

Two terminals, from the repository root.

```powershell
# 1. API  ->  http://localhost:5080
dotnet run --project backend/src/BillingCrm.Api

# 2. Web  ->  http://localhost:4200
cd frontend
npm install        # first time only (installs from package-lock.json)
ng serve
```

On its first start in `Development` the API creates two local-development accounts. It does this
only against a LocalDB connection string, only if the accounts are missing, and it never changes an
existing account. The seeder is not even registered in any other environment:

| Email | Role |
|-------|------|
| `admin@crm.local` | Admin |
| `agent1@crm.local` | Agent |

Their passwords are the documented development values in `CLAUDE.md` ("Seed Users"). They are for
your own LocalDB only. If the log says seeding was skipped because migrations are pending, run the
`dotnet ef database update` step above and restart the API.

## Check that it works

- Open <http://localhost:5080/health>. Expected: HTTP 200 and
  `{"status":"Healthy","checks":{"database":"Healthy"}}` (no sign-in needed).
- Open <http://localhost:4200>. You are sent to the sign-in page.
- Sign in as the Admin: you see the "BillingCRM" toolbar with your email and role, a **Sign out**
  button and an **Administration** link, and a card showing `API status: Healthy`.
- Sign out, then sign in as the Agent: there is no Administration link, and typing
  <http://localhost:4200/admin> shows "You do not have access to this area."
- A wrong password or an unknown email shows "Email or password is incorrect." and creates no
  session.
- When a session expires you are sent back to sign-in with "Your session expired. Sign in again."
  and, after signing in, return to the page you were on.
- Open **Customers** in the toolbar (any signed-in user). **Add customer** with a name, type
  (Company or Individual), email, phone and address; the VAT number is optional. The customer opens on
  its details page as **Active**.
- Add a second customer with the **same email** (even in a different letter case, or with spaces
  around it): it is refused with "Another customer already uses this email address." on the email
  field, and everything you typed stays on screen.
- Use the search box (name, email, phone or VAT number) and the Type and Status filters; combine them.
  Arabic and English names both work. The search text, filters and page are kept in the address bar, so
  the Back button and bookmarks bring the same list back.
- **Edit** a customer (saving with its own email is fine; taking another customer's email is refused).
  **Deactivate** a customer: it stays in the list, marked **Inactive**, keeps all its data and keeps its
  email reserved; **Activate** restores it. There is no way to delete a customer.
- **Products, as the Admin:** open **Products** in the toolbar and **Add product** with a name, a SKU, a unit
  (Hour, Item or Service), a unit price in SAR and a VAT percent (leave it empty for 15). For example SKU
  `CNS-001`, price `100`, VAT `15`: after saving (or refreshing the list) it appears with `SAR 100.00` and `15%`.
  A SKU is saved in upper case and must be unique: adding `cns-001` or ` CNS-001 ` again is refused with
  "Another product already uses this SKU." on the SKU field, everything you typed stays, and nothing is created. A
  price with three decimals (`10.005`), a negative price, a VAT above 100 or a SKU with spaces or Arabic letters is
  refused on its field: nothing is ever rounded silently.
- Search products by name or SKU (Arabic and English both work), filter by unit, and use the **Status** filter
  (Active, Inactive, All; the default is Active). **Edit** a product (saving with its own SKU is fine; taking another
  product's SKU is refused). **Deactivate** hides a product: it leaves the default list and the Agents' view, keeps
  all its data and keeps its SKU reserved; show **Inactive** or **All** to find it and **Activate** to bring it back.
  There is no way to delete a product.
- **Products, as the Agent:** the same list, search and details, read-only: no Add product button, no Status filter,
  no Edit or Deactivate, and inactive products are never shown (opening one by its address says "Product not
  found"). Typing <http://localhost:4200/products/new> (or `/products/<id>/edit`) shows "You do not have access to
  this area."
- **Invoices (any signed-in user):** you need an active customer (for example **ABC**) and an active product
  (for example `CNS-001`, price 100, VAT 15). Open **Invoices** and **Create invoice**: choose the customer (type
  part of the name), a due date, optional notes, then **Add line**, choose the product (type its name or SKU) and a
  quantity such as `2`, and **Save**. The invoice opens as a **Draft** with a number like `INV-000123` and exactly one
  line showing the product's name, SKU, unit, unit price and VAT as they were when the line was added, and the
  amounts: line net **200.00** and VAT **30.00**, total net 200.00, total VAT 30.00 and grand total **230.00 SAR**.
- The saved line does **not** follow the product: as Admin change `CNS-001`'s price and VAT (or deactivate it) and
  reopen the invoice, it still shows the old values; a line added afterwards uses the new ones. Only **active**
  customers and products can be chosen, so an inactive one is not offered (and is refused if submitted anyway).
- A quantity must be greater than zero with at most two decimals (`1.5` is fine; `0`, `-1`, `0.001` and `abc` are
  refused on the line, nothing is saved, and everything you entered stays on screen). The due date cannot be in the
  past, a Draft needs at least one line and can have at most 100.
- **Edit** a Draft: change a quantity, **Add line**, **Remove** a line (never the last one), change the due date, notes
  or customer, and **Save**: it is all saved together or not at all. An existing line keeps its saved product values;
  the product of an existing line cannot be swapped (remove it and add a new line). If a product or the customer became
  inactive after the Draft was made, the Draft can still be edited: existing lines and the same customer are kept, but
  a new line or a different customer must be active. There is no way to delete an invoice.
- **Status: send, cancel, Overdue (spec 007):** a new invoice is a **Draft**. Open it and click **Send** (any signed-in user; you
  are asked "Send this invoice? This locks it…" and answer **Yes, send**): it becomes **Sent**, the time sent is shown, and it is
  now **read-only** (no Edit, no line, discount, customer, due date or notes changes; its edit address says "Only a Draft can
  be edited"). Its amounts are frozen at that moment, so changing a product's price later never moves them. A Draft whose due
  date has already passed cannot be sent: change the due date first. An **Admin** (only) can also **Cancel invoice** a Draft or
  a Sent invoice (**Yes, cancel**; permanent): it shows **Cancelled**, stays on record with its number and amounts, and can
  never be sent, edited or reinstated. An Agent is never offered Cancel, and the API refuses it with 403. A Sent invoice whose
  due date is before today (Saudi date) is shown as **Overdue** in the details and the list, with nobody having to do
  anything (Overdue is worked out, never stored, and an Overdue invoice can still be cancelled by an Admin, as long as it has no payment). Any change that is
  not allowed (send twice, cancel a Cancelled invoice, edit a Sent invoice) is refused with a message naming the invoice's
  status. Two people acting on the same invoice at the same moment: one wins and the other is told it "was changed by someone
  else" (reload and try again). Payments (spec 008, below) are what make an invoice **Paid**. To check all of this by hand,
  follow `specs/007-invoice-status/quickstart.md` (it includes the one SQL line that makes a Sent invoice late so you can see **Overdue**).
- **Payments (spec 008):** on a **Sent** invoice (also one shown as **Overdue**) any signed-in user can click **Record payment**
  and enter the **date** the money was received, the **amount** in SAR, the **method** (**Cash**, **Bank** or **Other**) and an
  optional **note**. This is a **record** of money staff received, not a payment gateway: nothing charges a card or moves money.
  The invoice then shows what has been **Paid**, what **Remaining** (the invoice's grand total minus the payments, worked out by
  the server) and the list of payments. A payment cannot be more than what remains (no overpaying; 10.005 is refused, never
  rounded), cannot be dated in the future (Saudi date) or before the invoice was sent, and cannot be recorded on a **Draft**,
  **Cancelled** or already **Paid** invoice. The payment that brings the remaining to exactly 0.00 makes the invoice **Paid** in the
  same step. Payments are **permanent**: there is no way to edit, void or delete one, and an invoice that has a payment **cannot be
  cancelled** (there are no refunds yet). Two people paying the same invoice at the same moment: one wins, the other is told it "was
  changed by someone else". To check all of this by hand, follow `specs/008-payments/quickstart.md`.
- **Print (spec 009):** every invoice's details have a **Print** action (any signed-in user, any status: Draft, Sent, Overdue, Paid
  or Cancelled). It opens a printable page **in the same tab** (a new tab would not have your session) showing the title
  **BillingCRM**, the customer's name, the invoice number, its dates (created, due, and sent or cancelled when they exist), the
  notes when there are any, every line, the totals and the status, as a clean document with none of the application's menus. The
  figures are exactly the details' (the page calculates nothing). It deliberately leaves out payments, paid and remaining, the
  customer's address and VAT number and any seller details. The page's **Print** button (or Ctrl+P) opens the browser's own print
  dialog; nothing prints by itself, and "Save as PDF" is the browser's feature (the application makes no file). The browser adds its
  own header and footer (the page address, which contains the invoice's id and this computer's host name, the date and page numbers):
  for a copy that leaves the company, untick **Headers and footers** in the print dialog. Opening or printing
  changes nothing on the invoice. There is **no migration** for this spec. To check it by hand, follow
  `specs/009-invoice-print/quickstart.md` (it includes the browser print-preview checks).
- **History (spec 010):** every invoice's details have a **History** action (any signed-in user, any status). It shows every change ever made
  to that invoice, newest first: who made it (their email and role, exactly as they were at that moment), when, and what changed — its kind
  (**Created**, **Draft edited**, **Sent**, **Payment** or **Cancelled**) and, for each thing that changed, its field and its value before and
  after (dates, money and quantities as plain text, never recalculated). Send an invoice and open its History: a **Sent** row appears showing
  your own email and role and `Status: Draft -> Sent`. Editing a Draft that changes nothing, a refused action and a lost race add no row. Renaming
  the customer or repricing the product afterwards never changes what an old entry says. An invoice made before this feature has no earlier
  history: the screen says so instead of inventing anything. History **cannot be edited or deleted**: the screen has no control for it, there is
  no API route for it beyond the one read, and the database itself refuses any `UPDATE` or `DELETE` on the table. To check all of this by hand,
  follow `specs/010-invoice-history/quickstart.md`.
- **Amounts and discount:** each line shows its net (quantity x unit price, rounded to two decimals) and its VAT (its
  own VAT percent applied to that net). **Edit** the Draft and type **Discount in SAR** `20.00`, then **Save**: the discount
  comes off the net before VAT is worked out (net after discount 180.00, VAT 27.00, grand total **207.00**), shared over
  the lines in proportion to their net so lines with different VAT are each charged correctly. Clear the box to remove it.
  A discount cannot be negative, have more than two decimals, or be more than the total net; amounts above
  9,999,999,999,999.99 are refused. Rounding is half away from zero (0.025 becomes 0.03), once per line. The amounts
  come from the API; the screens never calculate them, and changing a product's price later never changes them.
- The invoice list shows the invoices newest first (number, customer, due date, status) with paging.
- **Dashboard (spec 011):** Home shows five figures for any signed-in user, computed live every time the page loads (never cached): a
  **Draft** count, an **Unpaid** count (every Sent invoice, on-time and overdue alike), an **Overdue** count (the Sent invoices whose due
  date has passed, a subset of Unpaid), a **Paid this month** count (a Paid invoice counts by the date of the payment that actually
  settled it, never an earlier part payment) and the **unpaid amount** — the SAR total still owed across the unpaid invoices. Click any
  card to open the invoice list narrowed to exactly that set (the Unpaid card and the unpaid amount both lead to the same Sent invoices).
  A Cancelled invoice is never part of any figure. There is **no migration** for this spec: everything is read from the invoices and
  payments already stored. To check all of this by hand, follow `specs/011-dashboard/quickstart.md`.
- **Security hardening (spec 012):** nothing changes on screen for normal use; see [`SECURITY.md`](SECURITY.md) for the full picture. A
  few things are now newly visible: typing a wrong password more than 10 times in a minute shows the same "Email or password is
  incorrect." each time up to the 10th, then a brief pause is enforced before another attempt is accepted (the API answers `429` with a
  `Retry-After` header); a name, address, note or invoice notes containing a right-to-left/left-to-right override character (never
  something a person types by hand — normal Arabic, English or mixed text and emoji are unaffected) is refused the same way a too-long
  one already was; a product price, VAT percent, invoice quantity, discount or payment amount typed in scientific notation (`1e5`) or
  with far more digits than any real price needs is refused rather than silently rounded. Every response now also carries
  `X-Frame-Options: DENY`, and every API response also `Cache-Control: no-store`; a request body has a 1 MiB limit. To check all of this
  by hand, follow `specs/012-security/quickstart.md`.
- If the API is not running, the home page still loads and shows "API unreachable".
- If the database cannot be reached (for example the database does not exist yet),
  `/health` returns HTTP 503 with `{"status":"Unhealthy","checks":{"database":"Unhealthy"}}`.

Every API endpoint other than `/health` and `POST /api/auth/login` requires a signed-in user; the
Admin-only operations under `/api/admin` refuse Agents with HTTP 403. The customer operations under
`/api/customers` are open to any signed-in Admin or Agent (add, list/search, get, edit, and
`PUT /{id}/active`); a duplicate email is HTTP 409 and there is no delete. The product operations under
`/api/products` are readable by any signed-in Admin or Agent (list/search with `q`, `unit`, `status`, `page`,
`pageSize`, and get by id) but **only an Admin may add, edit or activate/deactivate**: an Agent gets HTTP 403 before
the request body is even read. A duplicate SKU is HTTP 409, the list shows only active products unless an Admin asks
for `status=inactive|all` (an Agent never sees inactive products), and there is no delete. The invoice operations
under `/api/invoices` are open to any signed-in Admin or Agent: `POST` creates a Draft with all its lines in one
request (201 + `Location`), `GET` lists newest first (`page`, `pageSize`), `GET /{id}` reads one, and `PUT /{id}` saves
the **whole** Draft (header plus every line: an existing line by `id` with its quantity, a new line by `productId`, a
line left out is removed) in one all-or-nothing step. Every refusal is a 400 naming the field or line
(`lines[0].quantity`, `discount`), an unknown or malformed id is 404, a `PUT` that lost a race with another edit of the same invoice is a 409 (reload and try again), and there is no delete. `POST /api/invoices/{id}/send` (any signed-in user) sends a Draft, and `POST /api/invoices/{id}/cancel` (**Admin only**, 403 for an Agent before anything else is looked at) cancels a Draft or Sent invoice that has no payment (409 once it has one); both take no body and answer with the invoice, which carries `status` (Draft, Sent, Paid or Cancelled), `isOverdue`, `sentAtUtc`, `cancelledAtUtc`, `allowedTransitions`, `isEditable`, `paid`, `remaining`, `payments` and `acceptsPayment`. `POST /api/invoices/{id}/payments` (any signed-in user; body `date`, `amount`, `method`, `note`) records a payment on a Sent invoice and answers 201 with the updated invoice; a payment on a Draft, Cancelled or Paid invoice is a 409 naming the status, and a bad date, amount, method or note is a 400 naming the field. An illegal change or an edit of a non-Draft invoice is a 409 naming the current status, and a Draft with a past due date cannot be sent (400 on `dueDate`). Cross-origin calls are
allowed only from the origins in `Cors:AllowedOrigins`
(`backend/src/BillingCrm.Api/appsettings.json`, default `http://localhost:4200`). Override it with an
environment variable such as `Cors__AllowedOrigins__0=http://localhost:4300`. `*` is rejected at
start-up.

## Tests

```powershell
dotnet test backend/BillingCrm.sln     # Application, API and Infrastructure tests
cd frontend
ng test --watch=false                  # component and service tests
```

The Application and API tests need no database. The **Infrastructure tests need SQL Server LocalDB**:
they check the real search (case, `%`/`_` wildcards, Arabic), the unique-email and unique-SKU indexes, the
price/VAT/unit check constraints, exact decimal round-trips, and 10,000-customer and 5,000-product timings against SQL Server. Each run creates its own throwaway database named
`BillingCrm_Tests_<guid>`, applies the real migrations, and deletes only that database afterwards; your
`BillingCrm` database is never touched. The connection is built in code with Windows authentication (no
credentials). To use another server, set the environment variable `BILLINGCRM_TEST_SERVER`.

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| API exits with "Required setting ... is missing" | Set the named setting (connection string or `Jwt:SigningKey`, see above); run the API in `Development` (plain `dotnet run`) if using user-secrets |
| `dotnet ef` fails with a message about `Jwt:SigningKey` | Set the signing key first (see "Configure the signing key"); EF starts the API to read the model |
| `/health` returns 503 | The `BillingCrm` database does not exist or the connection string points elsewhere: run `dotnet ef database update ...` |
| Infrastructure tests fail with "These tests need SQL Server LocalDB" | Start or install LocalDB (`sqllocaldb i`, `sqllocaldb start MSSQLLocalDB`); the tests never skip, so an unreachable database is a failure |
| Leftover `BillingCrm_Tests_*` databases in LocalDB | A test run was killed before cleaning up; they are safe to delete (`sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "SELECT name FROM sys.databases WHERE name LIKE 'BillingCrm_Tests_%'"`) |
| The Customers screen shows "The customers could not be loaded" | The `Customers` table is missing: run `dotnet ef database update ...` and reload |
| The Products screen shows "The products could not be loaded" | The `Products` table is missing: run `dotnet ef database update ...` (migration `AddProducts`) and reload |
| The Invoices screen shows "The invoices could not be loaded" | The `Invoices` tables are missing: run `dotnet ef database update ...` (migration `AddInvoices`) and reload |
| An invoice shows no amounts, or saving fails with a database error mentioning `Discount` | The `Invoices.Discount` column is missing: run `dotnet ef database update ...` (migration `AddInvoiceDiscount`) and reload |
| Opening, sending or saving an invoice fails with a database error mentioning `SentAtUtc`, `RowVersion` or `Frozen...` | The status columns are missing: run `dotnet ef database update ...` (migration `AddInvoiceStatuses`) and reload |
| Opening an invoice fails with a database error mentioning `Payments` | The `Payments` table is missing: run `dotnet ef database update ...` (migration `AddPayments`) and reload |
| Saving or opening an invoice fails with a database error mentioning `InvoiceHistoryEntries` | The history table (and its trigger) is missing: run `dotnet ef database update ...` (migration `AddInvoiceHistory`) and reload |
| Creating an invoice fails with an unexpected error after a very large number of invoices | The sequence `InvoiceNumbers` stops at 999,999 (the six-digit form); raising it needs a new migration |
| An Admin cannot find a product they deactivated | The default list shows Active only: set the **Status** filter to Inactive or All |
| Cannot sign in as the seeded users | The API log says seeding was skipped: migrations pending, database unreachable, or the connection string is not a `(localdb)` instance (seeding never runs against any other server). Apply the migrations and restart the API in `Development` |
| Signed out unexpectedly | The session is time-limited (60 minutes by default) and is kept per browser tab; closing the tab ends it |
| Stopping LocalDB does not make `/health` fail | LocalDB restarts itself on the next connection; make the database genuinely unavailable (wrong database name) to test the 503 |
| `npm install` / `npm install <pkg>` fails with `edgesOut` | Use npm 11+ (see Prerequisites) |
| Browser blocks the API call (CORS) | Serve Angular from `http://localhost:4200`, or add your origin to `Cors:AllowedOrigins` |
