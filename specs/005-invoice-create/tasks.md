---

description: "Task list for 005-invoice-create"
---

# Tasks: Create Draft Invoices

**Input**: Design documents from `/specs/005-invoice-create/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/invoices-api.md, quickstart.md

**Tests**: Included. FR-020 requires automated tests, and contracts/invoices-api.md lists contract tests that map to test
tasks below. Write each story's tests first and confirm they fail (a test that does not compile yet counts as failing).

**Organization**: Grouped by user story (US1 create a Draft invoice with lines, US2 edit a Draft and its lines, US3 see
invoices and open one). **US1 ends at the create form and the details page**; the invoice list and the shell link arrive with
US3 (Phase 5), so until then open `/invoices/new` and `/invoices/:id` by address.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (setup, foundational and polish tasks carry no story label)

## Path Conventions

`backend/src/BillingCrm.*`, `backend/tests/`, `frontend/src/app/` as in plan.md. All commands run from the repository root
unless a task says otherwise; run frontend commands from PowerShell. The API and `ng serve` may be running from an earlier
step and lock build output: stop the API before `dotnet build`, `dotnet test` or `dotnet ef`.

**Rules that apply to every task**: use the `minimal-api-conventions`, `angular-ui-conventions` and `sql-migrations` skills for
matching edits (and read `invoice-money` only to confirm this spec stays out of totals); expected failures (validation,
inactive or missing customer/product, not found) are `Result` -> `ProblemDetails`, never exceptions; money and quantity are
`decimal` everywhere and never `float`/`double`, a value with more than two decimals is refused and never rounded; **no line
amount, VAT amount, invoice total or balance is calculated, shown or stored anywhere**; a line's name, SKU, unit, unit price and
VAT percent are copied from the product **once** when the line is created and never rewritten; no connection string, key or
credential in any committed file; every API endpoint declares its access explicitly; **there is no delete, issue, cancel or
status change operation anywhere**; do not change customer or product behaviour (003 and 004 are closed); Arabic and English
text must round-trip unchanged.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirmations and a green baseline. No behaviour yet.

- [X] T001 **Gate (Constitution V)**: before generating code, get the developer's answers on the new items in plan.md "Patterns and decisions to confirm": (1) the aggregate `PUT /api/invoices/{id}` that saves the whole Draft (existing lines by `id` with quantity only, new lines by `productId`, omitted lines removed, one atomic save, no per-line endpoints, no `DELETE`), (2) invoice numbers from the SQL sequence `InvoiceNumbers` stored as an `int` and formatted `INV-` + six digits, (3) snapshot columns plus a product reference, (4) quantity as `decimal(9,2)`, dates as `date`, "today" at a fixed UTC+03:00, (5) every refusal as a field error with array paths and ids parsed as text, (6) the edit rules for an existing Draft, (7) the Angular pieces: customer and product picker components, `core/money/decimal-text.ts`, bracket paths in `applyProblemToForm`, (8) the spec's defaults (Admin and Agent both allowed, at least one line, 100 lines, 999,999.99, 1,000 characters, same product twice allowed). If (1) is rejected, stop and re-plan the edit contract before any code; if (2) or (4) change, adjust T003, T008, T009 and the contract. Record the answers in `specs/005-invoice-create/research.md` (R1, R2, R4-R7, R12)
- [X] T002 Baseline check before any change: stop the API and `ng serve` if running, then `dotnet test backend/BillingCrm.sln` and (from PowerShell, in `frontend/`) `npx --yes npm@11 exec -- ng test --watch=false` pass, `sqllocaldb i` lists `MSSQLLocalDB`, and `dotnet ef migrations list --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api` shows `AddProducts` as the last applied migration

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, shared types, port, tables, the number sequence, the test harnesses and the shared front-end helpers.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T003 [P] Add `InvoiceStatus` (enum `Draft`) in `backend/src/BillingCrm.Domain/InvoiceStatus.cs` and the static `InvoiceNumber` in `backend/src/BillingCrm.Domain/InvoiceNumber.cs`: `Format(int)` returns `INV-` plus the number left-padded with zeros to six digits (`123` gives `INV-000123`), and `TryParse(string, out int)` accepts exactly that shape
- [X] T004 [P] Add the entity `InvoiceLine` in `backend/src/BillingCrm.Domain/InvoiceLine.cs` per data-model.md: private setters and an EF constructor; properties `Id`, `InvoiceId`, `Position`, `ProductId`, `ProductName`, `ProductSku`, `Unit` (`ProductUnit`), `UnitPrice`, `VatPercent`, `Quantity` (all `decimal` for the three numbers); a static `CreateFrom(Product product, int position, decimal quantity)` that copies name, SKU, unit, unit price and VAT percent from the product **once** (new `Guid.NewGuid()` id); and a single change method `SetQuantity(decimal)` returning whether it changed. No other setter is public and there is no line total member
- [X] T005 Add the aggregate root `Invoice` in `backend/src/BillingCrm.Domain/Invoice.cs` per data-model.md: private setters and an EF constructor; `Id`, `Number` (`int`, 0 until the database assigns it), `CustomerId`, `Status`, `DueDate` (`DateOnly`), `Notes` (nullable), `CreatedAtUtc`, `UpdatedAtUtc`; a private `List<InvoiceLine>` field exposed as `IReadOnlyList<InvoiceLine> Lines` ordered by `Position`; `Create(customerId, dueDate, notes, nowUtc)` (Draft, equal timestamps); `UpdateDetails(customerId, dueDate, notes, nowUtc)`, `AddLine(Product, quantity, nowUtc)` (position = highest existing position + 1, or 1), `RemoveLine(lineId, nowUtc)` and `SetLineQuantity(lineId, quantity, nowUtc)`; every change method returns whether anything really changed and moves `UpdatedAtUtc` only then (depends on T003, T004)
- [X] T006 [P] Add the invoice types in `backend/src/BillingCrm.Application/Invoices/InvoiceModels.cs`: `InvoiceLineInput(string? Id, string? ProductId, decimal? Quantity)`, `InvoiceInput(string? CustomerId, string? DueDate, string? Notes, IReadOnlyList<InvoiceLineInput>? Lines)` (one record for add and edit), `InvoiceCustomerDto(Guid Id, string Name, bool IsActive)`, `InvoiceLineDto` (id, position, productId, productName, sku, unit as text, unitPrice, vatPercent, quantity), `InvoiceDto` (id, number as `INV-` text, status text, customer, dueDate as `yyyy-MM-dd` text, notes, timestamps, lines), `InvoiceSummary` (id, number text, status text, customerId, customerName, dueDate text, createdAtUtc) and `InvoiceQuery(string? Page, string? PageSize)`. Reuse `PagedResult<T>` unchanged. Money and quantity members are `decimal`, never `double`
- [X] T007 Add the port `IInvoiceStore` in `backend/src/BillingCrm.Application/Invoices/IInvoiceStore.cs`: `FindByIdAsync(Guid)` (the invoice **with its lines**, tracked), `AddAsync(Invoice)` (saves; the number is filled in), `SaveChangesAsync()` (saves an edited tracked invoice) and `SearchAsync(int page, int pageSize)` returning `PagedResult<InvoiceSummary>` (depends on T005, T006)
- [X] T008 Add `DbSet<Invoice>` and `DbSet<InvoiceLine>` and the sequence to `backend/src/BillingCrm.Infrastructure/Persistence/BillingDbContext.cs` (`modelBuilder.HasSequence<int>("InvoiceNumbers").StartsAt(1).IncrementsBy(1).HasMin(1).HasMax(999999)`), and add `backend/src/BillingCrm.Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs` and `InvoiceLineConfiguration.cs` per data-model.md: table `Invoices` (`Number` int, `ValueGeneratedOnAdd`, `HasDefaultValueSql("NEXT VALUE FOR [InvoiceNumbers]")`, unique index `IX_Invoices_Number`; `CustomerId` foreign key to `Customers` restrict with index `IX_Invoices_CustomerId`; `Status` stored as the enum name nvarchar(20) with check `CK_Invoices_Status`; `DueDate` column type `date`; `Notes` nvarchar(1000) nullable; timestamps datetime2 tagged UTC on read as in `ProductConfiguration`; index `IX_Invoices_CreatedAtUtc_Number`), table `InvoiceLines` (`Id` `ValueGeneratedNever`; `InvoiceId` cascade with the non-unique index `IX_InvoiceLines_InvoiceId_Position`; `ProductId` foreign key to `Products` restrict; `ProductName` nvarchar(200), `ProductSku` nvarchar(50), `Unit` enum name nvarchar(20) with check `CK_InvoiceLines_Unit`, `UnitPrice` **decimal(18,2)** with check `>= 0` (`CK_InvoiceLines_UnitPrice`), `VatPercent` **decimal(5,2)** with check `BETWEEN 0 AND 100` (`CK_InvoiceLines_VatPercent`), `Quantity` **decimal(9,2)** with check `> 0 AND <= 999999.99` (`CK_InvoiceLines_Quantity`)), and map `Invoice.Lines` through its private field (`HasMany(...).WithOne().HasForeignKey(...)` with field access) (depends on T005)
- [X] T009 Create migration `AddInvoices` with `dotnet ef migrations add AddInvoices --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api --output-dir Persistence/Migrations`, review it against data-model.md (the sequence with its minimum, maximum and no cycling, exact column types, the constraints, the indexes, the three foreign keys; nothing unrelated), apply with `dotnet ef database update ...`, and confirm with sqlcmd that the sequence, `Invoices` and `InvoiceLines` exist with the unique number index and check constraints, that `Customers`, `Products` and `StaffUsers` are untouched, and that no total or amount column exists (depends on T008)
- [X] T010 [P] Add `FakeInvoiceStore` (in-memory, hands out increasing numbers on `AddAsync`, records the last search, tracks calls) in `backend/tests/BillingCrm.Application.Tests/Fakes/FakeInvoiceStore.cs`; extend the existing `FakeCustomerStore` and `FakeProductStore` only if a test needs a member they already lack (depends on T007)
- [X] T011 [P] Add `InMemoryInvoiceStore : IInvoiceStore` in `backend/tests/BillingCrm.Api.Tests/InMemoryInvoiceStore.cs` (LINQ-based, increasing numbers, newest-first paging with the customer name taken from the fake customer store) and register it in `ApiFactory` (`RemoveAll<IInvoiceStore>` then singleton) exactly as the product store is; the existing in-memory customer and product stores, `CreateAuthorizedClient(role)` and `TestTokens` are reused (depends on T007)
- [X] T012 [P] Extend `backend/tests/BillingCrm.Infrastructure.Tests/MigrationSmokeTests.cs`: the sequence `InvoiceNumbers` exists (`sys.sequences`, minimum 1, maximum 999999, not cycling), the tables `Invoices` and `InvoiceLines`, `IX_Invoices_Number` unique, the five check constraints, column types (`Quantity` decimal(9,2), `UnitPrice` decimal(18,2), `VatPercent` decimal(5,2), `DueDate` date) and that no column named like total, amount or balance exists (depends on T009)
- [X] T013 [P] Add `frontend/src/app/core/invoices/invoice.models.ts`: `InvoiceLineInput` (`id?`, `productId?`, `quantity`), `InvoiceInput` (`customerId`, `dueDate`, `notes`, `lines`), `Invoice`, `InvoiceLine`, `InvoiceListItem`, `InvoiceListQuery` (`page`, `pageSize`); reuse the existing `PagedResult<T>` type by import
- [X] T014 [P] Add `frontend/src/app/core/money/decimal-text.ts` and `decimal-text.spec.ts`: pure functions that check an amount typed as text without floating point: `decimalTextProblem(text, { max, allowZero })` returning `'required' | 'notNumber' | 'negative' | 'zero' | 'decimals' | 'tooBig' | null` (more than two decimals is a problem unless the extra digits are all zeros, so `1.500` is fine and `1.005` is not; `-0` is zero, not negative) and `decimalTextToNumber(text)`. Cover `1`, `1.5`, `1.50`, `1.500`, `0`, `0.01`, `-1`, `abc`, ``, `1,5`, `.5`, `1.005`, `999999.99`, `1000000` and Arabic-Indic digits (refused)
- [X] T015 [P] Extend `frontend/src/app/core/http/problem-details.ts` and `problem-details.spec.ts` so a 400 error key with brackets such as `lines[0].quantity` is resolved to the form control at `lines` > `0` > `quantity` (a private helper turns `a[0].b` into the path `['a', 0, 'b']` for `form.get`); every existing case still passes unchanged (customers and products), keys without brackets behave as before, an unresolvable bracket key becomes part of the top-level message, and a malformed key never throws (depends on nothing else)

**Checkpoint**: `dotnet build backend/BillingCrm.sln` succeeds; every existing back-end and front-end test still passes (customers and products unchanged); the invoice tables and the sequence exist in LocalDB; the migration smoke tests pass.

---

## Phase 3: User Story 1 - Create a Draft invoice with lines (Priority: P1) MVP

**Goal**: A staff member creates an invoice for an active customer with a due date, notes and lines from active products with
quantities, and it is saved as a **Draft** with the next invoice number and a saved copy of each product's name, SKU, unit, unit
price and VAT percent; an inactive or missing customer or product is refused; every quantity, date, notes and line-count rule is
enforced with named field errors; the details page shows what was saved.

**Independent Test**: Sign in, open `/invoices/new`, create an invoice for customer ABC with `CNS-001` quantity 2, save, and see the
details page: status Draft, a number like `INV-000001`, one line of quantity 2 with the product's saved values; change the product's
price and see the saved line unchanged.

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [X] T016 [P] [US1] Unit tests in `backend/tests/BillingCrm.Application.Tests/InvoiceNumberTests.cs` (`Format` for 1, 123, 999999 gives `INV-000001`, `INV-000123`, `INV-999999`; `TryParse` accepts exactly that shape and refuses `INV-1`, `inv-000001`, `INV-0000001`, ` INV-000001`, empty) and `InvoiceValidatorTests.cs`: customer id required and a valid id; due date required, exactly `yyyy-MM-dd`, a real date (`2026-02-30` refused), today accepted, yesterday refused, "today" taken at UTC+03:00 from a fixed `TimeProvider` (at 22:30 UTC the Saudi date is already the next day), an unchanged past date accepted when editing; notes trimmed, empty becomes null, 1,000 characters accepted and 1,001 refused, line breaks kept; lines required, 1 accepted, 100 accepted, 101 and 0 refused; each line quantity: `2`, `1.5`, `1.50`, `0.01`, `999999.99` accepted and `0`, `-1`, `0.001`, `1000000`, missing refused; each new line needs a valid `productId`, an existing line needs a valid `id`, both or neither is an error, an `id` is not allowed on create; duplicate line ids refused; several problems reported together with camelCase array-path keys (`customerId`, `dueDate`, `notes`, `lines`, `lines[0].quantity`, `lines[1].productId`)
- [X] T017 [P] [US1] `InvoiceService` create tests in `backend/tests/BillingCrm.Application.Tests/InvoiceServiceCreateTests.cs` (with `FakeInvoiceStore`, `FakeCustomerStore`, `FakeProductStore` and a fixed `TimeProvider`): creates a Draft with equal timestamps, the customer id, the trimmed notes and one line of quantity 2 whose name, SKU, unit, unit price and VAT percent equal the product's; a later change to the product object does not change the saved line; the same product twice gives two lines with positions 1 and 2; a price-0 product is accepted; the store gives the number and the result shows `INV-000001` then `INV-000002`; a missing, malformed, unknown or **inactive** customer and a missing, malformed, unknown or **inactive** product each give a `Validation` failure naming `customerId` / `lines[i].productId` and store nothing; all problems (shape, customer, products) are reported together; nothing is stored on any refusal; `GetAsync` returns the invoice with its lines in position order and the customer's current name and active flag, and an unknown id gives `NotFound` "Invoice not found"
- [X] T018 [P] [US1] Endpoint tests in `backend/tests/BillingCrm.Api.Tests/InvoiceCreateEndpointTests.cs` (contracts/invoices-api.md tests 1-3, 6, 8 for these routes): `POST /api/invoices` for customer ABC and `CNS-001` quantity 2 -> `201` with `Location`, `status` `Draft`, exactly one line of quantity 2, number `INV-000001`, and the next create `INV-000002`; a body with no `total`, `vat`, `amount` or `balance` member anywhere; the saved line keeps the old values after an Admin changes the product's name, price and VAT and then deactivates it through `PUT /api/products/...` (customers and products are seeded through their own in-memory stores); adding the same product again later shows the new values; each refusal in the list (inactive/unknown/malformed customer, inactive/unknown/malformed product, each quantity rule, due date missing/invalid/past, notes over 1,000, no lines, 101 lines, a line `id` on POST) is a `400` naming the field with array paths and creates nothing; several problems together; empty and malformed body and a text quantity are `400`; `status`, `number` and `isActive` in the body are ignored; `GET /api/invoices/{id}` 200 with the customer's current name and active flag, unknown and non-GUID ids 404; Arabic customer and product names and notes round-trip; both Admin and Agent tokens work; every operation without a token is `401`
- [X] T019 [P] [US1] Store tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreCreateTests.cs` (using `LocalDbFixture`; customers and products inserted through the existing EF stores): `AddAsync` persists the invoice and every line field including an Arabic name and multi-line notes exactly, fills a number of at least 1, and a reload shows the same values, lines ordered by `Position`; `UnitPrice`/`VatPercent`/`Quantity` round-trip exactly (`100.10`, `999999999.99`, `0.01`, `999999.99`, `0.00`, `12.50`); **fifty invoices added in parallel from separate contexts all get different numbers** and none is refused; the unique index rejects a duplicate number inserted with raw SQL; the check constraints reject a quantity of 0, a negative quantity, a quantity above 999999.99, a negative price, VAT 101 and an unknown unit or status inserted with raw SQL; the foreign keys reject an unknown customer and an unknown product; deleting a product or customer with an invoice is impossible (restrict); a line refers to its product by id but shows its own saved columns after the product row is changed with raw SQL
- [X] T020 [P] [US1] Angular specs: `frontend/src/app/core/invoices/invoice.service.spec.ts` (create posts the input with numbers untouched, get, id encoded), `frontend/src/app/features/invoices/pickers/customer-picker.component.spec.ts` and `product-picker.component.spec.ts` (typing waits for a pause then calls the existing list service with the text and a small page size; the customer picker asks for active customers only; choosing an option emits the id and shows the name (and SKU for products); no match shows "No customers/products match"; the text stays; a failed lookup shows a message), `invoice-form.component.spec.ts` in add mode (starts with one empty line; **Add line** adds one up to 100; **Remove** removes a line but the last one cannot be removed and says a Draft needs at least one line; quantity errors for empty, `0`, `-1`, `0.001`, `abc`; customer, due date and product required; due date in the past refused; nothing sent while invalid; success sends `customerId`, `dueDate`, trimmed `notes` and `lines` with `productId` and a numeric `quantity` as typed, then navigates to the new invoice's details; a 400 with `lines[0].quantity` and `customerId` puts each message on its control; every refusal keeps every typed value and the chosen customer and products; Save disabled while saving; a failure that belongs to no field shows a top message), `invoice-detail.component.spec.ts` (number, **Draft** badge, customer, due date, notes with line breaks, created and changed times, every line with name, SKU, unit, `SAR 100.00`, `15%` and quantity; **Inactive** mark for an inactive customer; unknown id shows "Invoice not found"; loading and error states) and the route guard/route checks for `/invoices/new` and `/invoices/:id` in `frontend/src/app/app.spec.ts`

### Implementation for User Story 1 (back end)

- [X] T021 [US1] Implement `InvoiceValidator` in `backend/src/BillingCrm.Application/Invoices/InvoiceValidator.cs`: a pure function taking the input, today's date (UTC+03:00 date from the `TimeProvider`), a flag for create versus edit and, for edit, the invoice's current due date; it trims text, parses `dueDate` with the exact format `yyyy-MM-dd` (`DateOnly.TryParseExact`, invariant culture), parses ids with `Guid.TryParse`, checks notes, the line count (1 to 100), each line's quantity (`> 0`, `<= 999999.99`, at most two decimals with `value != decimal.Round(value, 2)`, scale normalised to two decimals on success), the `id`/`productId` rules and duplicate line ids, and returns a validated value or a `Validation` `Error` with camelCase array-path keys (`lines[0].quantity`), every problem together, messages as in contracts/invoices-api.md (depends on T006)
- [X] T022 [US1] Implement `InvoiceService` (create and get) in `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs`: `CreateAsync` (validate the shape, then look up the customer with `ICustomerStore.FindByIdAsync` and each distinct product with `IProductStore.FindByIdAsync`; unknown or inactive customer -> `customerId` error "Choose an active customer."; unknown or inactive product -> `lines[i].productId` error "This product is inactive or does not exist. Choose an active product."; combine every error into one `Validation` failure; then `Invoice.Create`, `AddLine` for each line with the product, `AddAsync`, and return the `InvoiceDto` with the number formatted by `InvoiceNumber.Format`), `GetAsync` (`NotFound` "Invoice not found" / "No invoice exists with this id."; lines in position order; customer name and active flag from `ICustomerStore`), public `NotFound` error, "today" from `TimeProvider` at a fixed UTC+03:00; register the service in `backend/src/BillingCrm.Application/DependencyInjection.cs` (depends on T007, T021)
- [X] T023 [P] [US1] Implement `EfInvoiceStore` (find and add) in `backend/src/BillingCrm.Infrastructure/Invoices/EfInvoiceStore.cs`: `FindByIdAsync` (`Include(Lines)`, tracked, ordered when read), `AddAsync` (`Add` then `SaveChangesAsync`; the sequence fills `Number`, which is read back), `SaveChangesAsync` and a `SearchAsync` that throws `NotImplementedException` until US3; register it scoped in `backend/src/BillingCrm.Infrastructure/DependencyInjection.cs` (depends on T007, T008)
- [X] T024 [US1] Add `backend/src/BillingCrm.Api/Endpoints/InvoiceEndpoints.cs`: `MapGroup("/api/invoices").RequireAuthorization()`; `POST /` (201 with `Location: /api/invoices/{id}`, failures via `ToProblem()`) and `GET /{id}` (route value parsed as a GUID; a non-GUID is the same 404 problem); map it in `Program.cs` next to `MapProductEndpoints()` (depends on T022, T023)
- [X] T025 [US1] After T024, explain the invoice endpoints to the developer in 5 lines using MVC words (per `minimal-api-conventions` skill), including why the whole invoice arrives in one request; no file output

### Implementation for User Story 1 (front end)

- [X] T026 [US1] Add `frontend/src/app/core/invoices/invoice.service.ts` (HttpClient service per resource): `create(input)`, `get(id)` (id `encodeURIComponent`-ed) against `{apiBaseUrl}/api/invoices`; list and update arrive with US3 and US2 (depends on T013)
- [X] T027 [P] [US1] Add `frontend/src/app/features/invoices/pickers/customer-picker.component.ts`: a standalone Material autocomplete implementing `ControlValueAccessor` (value = customer id or null) that shows the chosen customer's name, searches by typed text after a pause of about 300 ms with the existing `CustomerService.list({ q, active: true, pageSize: 10 })`, ignores stale answers (`switchMap`), shows a "no customers match" option and an error message, uses `dir="auto"`, and takes an initial display name so the edit form can show the current customer (depends on T013)
- [X] T028 [P] [US1] Add `frontend/src/app/features/invoices/pickers/product-picker.component.ts`: the same shape for products using `ProductService.list({ q, pageSize: 10 })` (active products only: that is the default of the products API for both roles); options show the product name and SKU, choosing emits the whole product (`id`, `name`, `sku`, `unit`, `unitPrice`, `vatPercent`) so the line can display it; `dir="auto"` for the name, `dir="ltr"` for the SKU (depends on T013)
- [X] T029 [US1] Add `frontend/src/app/features/invoices/invoice-form.component.ts` in **add mode**: standalone Material reactive form (customer picker, native `<input type="date">` for the due date with `min` set to today, notes textarea `dir="auto"` limited to 1,000 characters) and a `FormArray` of lines (product picker, quantity text input `dir="ltr"` checked with `decimalTextProblem`, **Remove**), **Add line** (at most 100), the last line cannot be removed; per-field and per-line error messages ("Quantity must be a number greater than zero with at most two decimal places."), `applyProblemToForm(form, error, { subject: 'invoice' })` so `lines[i].…` errors reach the line controls, focus moves to the first invalid control, Save is disabled while saving, everything typed stays on any refusal, quantity sent as `Number(text)` and never re-rounded, navigates to `/invoices/:id` on success (`replaceUrl`), edit mode arrives with US2 (depends on T014, T015, T026, T027, T028)
- [X] T030 [US1] Add `frontend/src/app/features/invoices/invoice-detail.component.ts`: number, status badge, customer name with an **Inactive** mark when applicable, due date, notes (line breaks kept), created and changed times (local time), a table of lines (product name, SKU, unit, unit price as `SAR` with two decimals, VAT percent, quantity with two decimals; the saved copy exactly as returned), loading, error-with-retry and "Invoice not found" states; no totals column; Edit and the list link arrive with US2 and US3 (depends on T026)
- [X] T031 [US1] Add the routes `invoices/new` and `invoices/:id` (with `new` before `:id`) as lazy-loaded children of the signed-in shell in `frontend/src/app/app.routes.ts`; the list route and the shell link arrive with US3 (depends on T029, T030)
- [X] T032 [US1] Run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false` in `frontend/` (from PowerShell), then `ng build`; fix until T016-T020 pass, all earlier tests still pass and the production build is clean
- [X] T033 [US1] Manual check on the real database and API with curl or PowerShell: sign in as Admin and Agent, create a customer and a product if needed, `POST` an invoice for the customer and the product with quantity 2 (201, `Draft`, one line, `INV-0000NN`), a second one (next number), `GET` it back; change the product's price and VAT and deactivate it through the products API and `GET` the invoice again (old values); refusals: inactive customer, inactive product, unknown ids, quantity `0`/`0.001`, past due date, no lines, several problems at once (each `400` with array-path keys, nothing created, the sequence still increasing afterwards); no token (401); confirm in `sqlcmd` that `InvoiceLines` holds the copied columns and no total. Record results in `specs/005-invoice-create/research.md` (verification log) and delete the test invoices, customers and products you created (depends on T024). **Done without a running API** (memory pressure had stopped the servers; they must not be restarted unprompted): the same path was proven against real SQL Server by `InvoiceServiceRealDatabaseTests`, and a live curl run moves to T060

**Checkpoint**: US1 acceptance scenarios 1-9 pass via tests; the browser check is in T060.

---

## Phase 4: User Story 2 - Edit a Draft invoice and its lines (Priority: P2)

**Goal**: While an invoice is a Draft, change its due date, notes and customer, change a line's quantity, add lines and remove lines
(never the last one) in one atomic save; existing lines keep their saved copy; new lines copy the product as it is now; a Draft with
a since-deactivated product or customer stays editable within the rules; a refused save changes nothing.

**Independent Test**: Open the Draft from story 1, change a quantity, add a second product line, remove the first line, change the due
date and notes, save and reopen it: every change persisted and the kept line still shows its original saved price.

### Tests for User Story 2

- [X] T034 [P] [US2] Service tests in `backend/tests/BillingCrm.Application.Tests/InvoiceServiceUpdateTests.cs` (contracts test 4-5; research R7): a valid edit changes quantity, adds a line, removes a line, changes due date, notes and customer in one save and moves `UpdatedAtUtc` while the created time and number stay; a kept line's name, SKU, unit, price and VAT stay even after the product object changed; a new line copies the product's current values and gets the next position; removing the last line (an empty list) is refused with `lines`; more than 100 lines refused; an `id` that is not a line of this invoice, a duplicate `id`, an entry with both `id` and `productId` and an entry with neither are each `lines[i].id`/`lines[i].productId` errors; the same customer is accepted even when inactive, a different inactive or unknown customer is refused; a new line for an inactive product is refused while an existing line for a now-inactive product is kept, changed or removed; an unchanged past due date is accepted, a changed past date is refused; a save that changes nothing does not touch `UpdatedAtUtc` and does not call `SaveChangesAsync`; any refusal leaves the invoice exactly as it was (no partial changes) and reports all problems together; unknown id gives `NotFound`
- [X] T035 [P] [US2] Endpoint tests in `backend/tests/BillingCrm.Api.Tests/InvoiceUpdateEndpointTests.cs`: `PUT /api/invoices/{id}` with existing lines by `id` and new lines by `productId` -> `200` with the new body (lines in position order, kept saved values, new line with the current product values); every refusal above is a `400` with the array-path keys and the invoice unchanged on a following `GET`; `PUT` of an unknown or non-GUID id is `404` (checked before the body); a body with unknown properties is fine; a Draft with a now-inactive product and a now-inactive customer (deactivated through their own APIs) can still be edited within the rules; no token is `401`; `DELETE /api/invoices/{id}` is not allowed
- [X] T036 [P] [US2] Store tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreUpdateTests.cs`: on a tracked invoice loaded with `FindByIdAsync`, a **new line** added through the aggregate is inserted (not treated as an existing row), a **removed line** is deleted from the table, a **kept line's quantity** is updated and its snapshot columns are untouched, the header changes persist, all in one `SaveChangesAsync`; a save that fails a constraint (for example a quantity above the limit written directly) leaves nothing half-saved; `UpdatedAtUtc` persists; positions stay unique in practice (the next position is the highest plus one) and the order after reload follows `Position`
- [X] T037 [P] [US2] Angular specs: `frontend/src/app/features/invoices/invoice-form-edit.component.spec.ts` (loads the invoice and pre-fills the customer, due date, notes and lines; existing lines show their **saved** name, SKU, unit price and VAT read-only with an editable quantity and **Remove**; new lines use the product picker; saving sends existing lines as `{ id, quantity }` and new ones as `{ productId, quantity }` and omits removed ones; the last line cannot be removed; a 400 with `lines[1].quantity` lands on that line and keeps every value; unknown id shows "Invoice not found" with no form; a load error shows retry; an inactive customer already on the Draft is still shown and kept), `invoice.service.update.spec.ts` (`update` sends `PUT` to the encoded id with the input, numbers untouched), the **Edit** link check in `invoice-detail.component.spec.ts`, and the `/invoices/:id/edit` route in `frontend/src/app/app.spec.ts`

### Implementation for User Story 2

- [X] T038 [US2] Add `UpdateAsync` to `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs` (find -> `NotFound`; validate with the edit flag and the invoice's current due date; check the customer only when it differs from the current one; check the product of each **new** line only; existing ids must be lines of this invoice; build the changes on the aggregate: `UpdateDetails`, `SetLineQuantity`, `AddLine`, `RemoveLine` for omitted lines; on **any** problem return the combined `Validation` failure **before changing the aggregate** (validate everything first, then apply); if nothing changed return the invoice without saving; otherwise `SaveChangesAsync` and return the `InvoiceDto`) (depends on T022)
- [X] T039 [US2] Confirm and, if needed, adjust `EfInvoiceStore.SaveChangesAsync` in `backend/src/BillingCrm.Infrastructure/Invoices/EfInvoiceStore.cs` so a new line added to the tracked collection is inserted, removed lines are deleted (orphan delete) and kept lines are updated in one transaction; add a comment naming `ValueGeneratedNever` as the reason a new line is not mistaken for an existing one (depends on T023, T036)
- [X] T040 [US2] Add `PUT /{id}` to `backend/src/BillingCrm.Api/Endpoints/InvoiceEndpoints.cs` (same group, same access; the id is parsed as a GUID and a non-GUID is a 404 before the body is read into validation; no delete route is mapped) (depends on T024, T038)
- [X] T041 [US2] After T040, explain the `PUT` endpoint to the developer in 5 lines using MVC words, including the three kinds of line (kept, new, omitted); no file output
- [X] T042 [US2] Frontend: add `update(id, input)` to `invoice.service.ts`; add edit mode to `invoice-form.component.ts` (route `invoices/:id/edit`, loads the invoice, pre-fills, shows existing lines with their saved values read-only and an editable quantity, keeps their ids, sends `{ id, quantity }` for existing lines and `{ productId, quantity }` for new ones, omits removed lines, same error handling, loading, not-found and retry states); add the **Edit** link to `invoice-detail.component.ts`; add the `invoices/:id/edit` route in `app.routes.ts` (before `invoices/:id`) (depends on T029, T030, T031)
- [X] T043 [US2] Run all back-end and front-end tests and `ng build`; fix until T034-T037 pass
- [X] T044 [US2] Manual check on the real database and API: `PUT` to change a quantity, add a line, remove a line, change due date, notes and customer (200, kept lines keep their saved price, the new line has the current price); remove the last line (400); a foreign line id, a duplicated id, an entry with both keys (400); a Draft with a deactivated product edited within the rules and a new line for it refused; a Draft with a deactivated customer edited without changing the customer (200) and with a different inactive customer (400); a save with nothing changed leaves `updatedAtUtc`; unknown id (404); no token (401); `DELETE` rejected. Record results in `specs/005-invoice-create/research.md` and delete the test rows you created. **Done without a running API**, as for T033: proven on real SQL Server by `InvoiceServiceRealDatabaseTests` and `EfInvoiceStoreUpdateTests`

**Checkpoint**: US2 acceptance scenarios 1-10 pass via tests.

---

## Phase 5: User Story 3 - See my invoices and open one (Priority: P3)

**Goal**: The invoice list, newest first with number, customer, due date and status, paged, with loading, empty and error states;
details already exist from US1; the Invoices link in the shell.

**Independent Test**: Create three Drafts, open the invoice list and see them newest first with their numbers, customers, due dates
and status, open one and see every saved line detail.

### Tests for User Story 3

- [X] T045 [P] [US3] Query tests in `backend/tests/BillingCrm.Application.Tests/InvoiceServiceSearchTests.cs`: `page` below 1, not a number, too big for a 32-bit integer, `pageSize` 0 and 101 each give a `Validation` failure naming that field; valid values (including 100) pass; defaults are page 1 and size 20; results map to `InvoiceSummary` with the formatted number, the status text, the customer name and the `yyyy-MM-dd` due date; totals and pages are carried
- [X] T046 [P] [US3] List endpoint tests in `backend/tests/BillingCrm.Api.Tests/InvoiceListEndpointTests.cs` (contracts tests 7-8): newest first (the last created appears first, ties by number), the response shape `items/page/pageSize/totalCount/totalPages`, each item with number, status, customer id and name, due date and created time and **no lines and no totals**; no invoices -> `200` empty with zero totals; paging splits and keeps a stable order; a page beyond the last (including `2147483647`) -> `200` empty with the real total; `page=0`, `page=abc`, `page=99999999999`, `pageSize=0`, `pageSize=101` -> `400` naming the field; both roles; no token `401`; the customer name is the customer's current name
- [X] T047 [P] [US3] Store list tests against real SQL Server in `backend/tests/BillingCrm.Infrastructure.Tests/EfInvoiceStoreSearchTests.cs` and `EfInvoiceStorePerformanceTests.cs`: order by creation then number descending, stable paging with no duplicates or gaps, `totalCount`, a huge page number gives an empty page with the real total, the customer name joined, an Arabic customer name intact; **5,000 invoices (one line each) seeded in batches, the first page returns in under 1 second after one warm-up query** (SC-006), printing the timings
- [X] T048 [P] [US3] Angular specs: `frontend/src/app/features/invoices/invoice-list.component.spec.ts` (loading; a row per invoice with number, customer, due date and status linking to its details; total and paginator; empty state "No invoices yet" with a **Create invoice** link; error with retry; previous rows stay while the next page loads and a slow older response never wins; `page`/`pageSize` read from the address with unsafe values replaced by defaults; the paginator updates the address), `invoice.service.spec.ts` (list builds the query string), and the shell **Invoices** link for both roles in `frontend/src/app/core/layout/shell.component.spec.ts`; extend `frontend/src/app/app.spec.ts` for `/invoices` (signed in opens the list, signed out goes to sign-in)

### Implementation for User Story 3

- [X] T049 [US3] Add `SearchAsync` to `backend/src/BillingCrm.Application/Invoices/InvoiceService.cs`: parse and validate `page` and `pageSize` from `InvoiceQuery` exactly as `ProductService` does (`NumberStyles.None`, defaults 1 and 20, maximum 100), call the store, and map to `InvoiceSummary` (number via `InvoiceNumber.Format`) (depends on T022)
- [X] T050 [US3] Implement `EfInvoiceStore.SearchAsync` in `backend/src/BillingCrm.Infrastructure/Invoices/EfInvoiceStore.cs`: a projection joining `Customers` for the name, ordered by `CreatedAtUtc` then `Number` descending, long-arithmetic offset (a page past the last is an empty page with the real total without asking for rows), separate count, `AsNoTracking` (depends on T023, T047)
- [X] T051 [US3] Add `GET /` to `backend/src/BillingCrm.Api/Endpoints/InvoiceEndpoints.cs` binding `page` and `pageSize` as strings so a bad value is a named field error (depends on T040, T049)
- [X] T052 [US3] Frontend: add `list(query)` to `invoice.service.ts`; add `frontend/src/app/features/invoices/invoice-list.component.ts` (`mat-table` with number, customer, due date and status; `mat-paginator` driven by `page`/`pageSize` in the address with unsafe values clamped; **Create invoice** button; loading (previous rows kept), empty and error-with-retry states; one `switchMap` stream so a slow older response never wins; rows link to details), add the `invoices` route (first among the invoice routes) in `app.routes.ts`, add an **Invoices** link in `frontend/src/app/core/layout/shell.component.ts` shown to every signed-in user, add a "Back to invoices" link to `invoice-detail.component.ts`; update `shell.component.spec.ts` and `app.spec.ts` (depends on T031, T042, T048)
- [X] T053 [US3] Run all back-end and front-end tests and `ng build`; fix until T045-T048 pass
- [X] T054 [US3] Manual check on the real database and API: create several invoices, `GET /api/invoices` (newest first, totals, `pageSize=2` pages 1..n, a huge page empty, bad `page`/`pageSize` 400, no token 401); record the 5,000-invoice timing printed by the T047 test in `specs/005-invoice-create/research.md` and delete the test rows you created. **Done without a running API** (see T033): the list is proven on real SQL Server by the store tests and through the real service by the endpoint tests, and a live run moves to T060

**Checkpoint**: US3 acceptance scenarios 1-5 pass via tests.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T055 [P] Add `backend/tests/BillingCrm.Api.Tests/InvoiceAccessTests.cs`: exactly `GET /api/invoices`, `GET /api/invoices/{id}`, `POST /api/invoices` and `PUT /api/invoices/{id}` exist under `/api/invoices`, none is `DELETE`, every one requires a signed-in user, none is anonymous and none requires `AdminOnly` (read from the endpoint metadata as `ProductAccessTests` does); the existing "every endpoint declares access" test still passes
- [X] T056 [P] Update `README.md`: the `AddInvoices` migration step (tables and the number sequence), the Invoices screens and what to check (create a Draft for ABC with `CNS-001` quantity 2, the number, the saved price surviving a product change, edit, list), the invoices API sentence (any signed-in user; `POST`, list, get, and `PUT` saving the whole Draft; refusals are field errors with array paths; no delete, no totals), the "no invoice tables yet" wording removed, troubleshooting for "The invoices could not be loaded" (missing tables) and for a sequence that has reached 999,999
- [X] T057 [P] Update `CLAUDE.md` project line, layout and run commands: `Invoice`, `InvoiceLine`, `InvoiceNumber` in Domain, `Invoices/` in Application and Infrastructure, `/api/invoices` (any signed-in user; `PUT` saves the whole Draft: kept lines by `id` with quantity only, new by `productId`, omitted removed), the number rule (sequence `InvoiceNumbers`, `int` column, `INV-` + six digits, gaps allowed, never reused), the snapshot rule (copied once, never rewritten, no live join), "no totals in 005: later spec uses `invoice-money`", the routes and the shared helpers (`core/money/decimal-text.ts`, bracket paths in `applyProblemToForm`, the two pickers)
- [X] T058 [P] Secret and hygiene scan of everything committable: no connection-string literal or credential in the new files; no `float`/`double` on any invoice, line, price, VAT or quantity member (search `backend/src` and `frontend/src`); no `total`, `amount`, `balance`, `subtotal` or multiplication of quantity by price in invoice code or UI; no delete operation on invoices; no leftover throwaway databases (`BillingCrm_Tests_%`) and no test invoices, customers or products left in the developer's `BillingCrm` database
- [X] T059 Re-run `dotnet test backend/BillingCrm.sln` and `ng test --watch=false`; all pass (SC-008)
- [X] T060 Run the full quickstart validation (`specs/005-invoice-create/quickstart.md`) on the developer's machine, including all browser checks (create the ABC / `CNS-001` quantity 2 Draft and see status Draft and one line, numbers `INV-…` increasing, the saved price surviving a product price change, inactive customer and product not offered, quantity and date errors with values kept, edit with add/remove/quantity/due date/notes, last line refused, list newest first, not found, signed-out redirect); the developer confirms it is running (Constitution II)
- [X] T061 Run `api-reviewer`, `angular-reviewer` and `spec-auditor`; fix or explicitly record every finding in `specs/005-invoice-create/research.md` (Constitution workflow gate). Give `api-reviewer` the sequence and number guarantees, the atomic replace of lines, the snapshot rule and the "no totals" rule as focus areas
- [X] T062 Re-run `dotnet test backend/BillingCrm.sln`, `ng test --watch=false` and `ng build` after any review fixes; all pass
- [X] T063 Mark spec 005 done only after T060 and T061 are complete, and update `specs/005-invoice-create/spec.md` status; do not start spec 006 before then

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: T001 gates all code generation; T002 is independent.
- **Foundational**: T003 and T004 parallel; T005 needs both; T006 parallel with T003-T005; T007 needs T005 and T006; T008 needs T005; T009 needs T008; T010 and T011 need T007; T012 needs T009; T013, T014 and T015 are independent of the back end.
- **US1**: after Foundational. Back end (T021-T025) and front end (T026-T031) are two tracks; the pickers T027 and T028 are parallel; T033 needs T024.
- **US2**: after US1 (extends the same service, store, endpoint, form and detail component); its tests T034-T037 can be written as soon as US1 is done.
- **US3**: after US1; independent of US2 except that both edit `InvoiceService.cs`, `EfInvoiceStore.cs`, `InvoiceEndpoints.cs`, `invoice.service.ts` and `app.routes.ts` (do them in order: US2 then US3).
- **Polish**: after all stories. T060 and T061 are the done-ness gate.

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories.
- **US2 (P2)**: builds on US1's aggregate, form and details.
- **US3 (P3)**: builds on US1's store and service; shares files with US2.

### Within Each Story

Tests first and failing -> implementation -> run tests. Back end: validator -> service -> store -> endpoint. Front end: service -> pickers -> form and details -> routes.

### Parallel Opportunities

- Foundational: T003 with T004, T006, T013, T014, T015; T010, T011 and T012 once their dependencies are done.
- US1: tests `T016-T020` together; then `T021` and `T023` together, and `T026`, `T027`, `T028` alongside the back-end tasks.
- US2: tests `T034-T037` together.
- US3: tests `T045-T048` together.
- Polish: T055-T058.

---

## Parallel Example: User Story 1

```text
# Tests, all at once:
T016 InvoiceNumberTests + InvoiceValidatorTests   T017 InvoiceServiceCreateTests   T018 InvoiceCreateEndpointTests
T019 EfInvoiceStoreCreateTests (incl. parallel numbers)   T020 service/picker/form/detail specs

# Then two tracks:
Back end:   (T021 || T023) -> T022 -> T024 -> T025 -> T033
Front end:  T026 -> (T027 || T028) -> T029 -> T030 -> T031
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Setup (T001-T002) and Foundational (T003-T015).
2. US1 (T016-T033).
3. Stop and validate: create a Draft for ABC with `CNS-001` quantity 2 by API and at `/invoices/new`, see status Draft, the number and one saved line, and see the saved price survive a product change. This is the request's stated acceptance scenario.

### Incremental Delivery

1. Add US2 (T034-T044): edit a Draft and its lines.
2. Add US3 (T045-T054): the invoice list and the shell link.
3. Polish, three reviewers, developer verification (T055-T063).

---

## Notes

- T001 is a hard gate; do not generate code before the developer answers.
- Totals, VAT amounts, issuing and every other status belong to later specs; this spec only stores what they need (quantity, unit price
  and VAT percent per line, exactly).
- The three items deferred to 012-security in spec 004 (decimal JSON converter, `StaffOnly` policy, Development header leak) apply to
  this API too and stay deferred.
- Commit only when the developer asks; there are no commits yet.
