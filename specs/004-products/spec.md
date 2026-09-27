# Feature Specification: Products

**Feature Branch**: `004-products`

**Created**: 2026-09-24

**Status**: Done (verified running by the developer 2026-09-24)

**Depends on**: 002-staff-auth (verified running); follows 003-customers (verified running)

**Input**: User description: "Feature 004-products. Depends on 002. Admin CRUD products. Agent can list/view only. name, unique SKU, unit Hour/Item/Service, unit price, VAT percent default 15, active. Deactivate to hide. No inventory. Given Admin creates SKU CNS-001 price 100 VAT 15, When I refresh the list, Then it appears. Given Agent opens create product, Then 403."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin adds a product and sees it in the list (Priority: P1)

An Admin adds a product to the price list by entering its name, a unique SKU code, its unit (Hour,
Item or Service), its unit price in Saudi riyals, and its VAT percentage (15 unless changed). After
saving, the product appears in the product list. An Agent who tries to add a product is refused. If the
SKU is already used by another product, the save is refused with a clear message.

**Why this priority**: Invoices (a later spec) need something to bill for. A product with a price and
a VAT rate is the smallest slice that delivers value, and the Admin-only rule for changing the price
list is the reason this spec exists.

**Independent Test**: Sign in as the Admin, add a product with SKU `CNS-001`, price 100 and VAT 15, refresh
the product list and confirm it appears; then sign in as the Agent and confirm adding a product is refused.

**Acceptance Scenarios**:

1. **Given** I am signed in as an Admin, **When** I create a product with SKU `CNS-001`, price 100 and
   VAT 15 (with a name and unit), **and** I refresh the product list, **Then** the product appears
   with its name, SKU, unit, price `100.00` SAR and VAT 15%.
2. **Given** I am signed in as an Admin, **When** I create a product and leave the VAT percentage
   empty, **Then** it is saved with VAT 15%.
3. **Given** I am signed in as an Admin, **When** I create a product with VAT 0, **Then** it is saved
   with VAT 0% (0 is a real value, not "empty").
4. **Given** a product with SKU `CNS-001` exists, **When** an Admin saves another product with SKU
   `CNS-001`, `cns-001` or ` CNS-001 `, **Then** the save fails with a clear message that the SKU is
   already used, and no second product is created.
5. **Given** I am signed in as an Agent, **When** I open the "add product" page or try to create a
   product by any other means, **Then** I am refused (access denied in the screens; "forbidden" from the
   back end) and nothing is created.
6. **Given** I am signed in as an Admin, **When** I leave a required field empty or enter an invalid
   value (a negative price, a price with three decimal places, a VAT above 100, an unknown unit),
   **Then** I am told which field is wrong and nothing is saved.
7. **Given** a save was refused for any reason, **When** the message appears, **Then** everything I
   typed is still on the screen so I can correct it.

---

### User Story 2 - Everyone can browse and search the price list (Priority: P2)

Any signed-in staff member, Admin or Agent, opens the product list, searches it by name or SKU, narrows
it by unit, and opens a product to see all of its details. Products that have been deactivated are hidden
from this list. Agents can only look; they see no controls to add, edit or deactivate.

**Why this priority**: The price list is only useful if staff can find a product quickly, and Agents need
to read prices without being able to change them.

**Independent Test**: With several products (some deactivated), sign in as the Agent, search by a name
fragment and a SKU fragment, filter by unit, open a product, and confirm no change controls appear; then
sign in as the Admin and confirm deactivated products can be revealed.

**Acceptance Scenarios**:

1. **Given** active and deactivated products exist, **When** I open the product list, **Then** only
   active products are listed.
2. **Given** I am an Admin, **When** I choose to show deactivated products (or both), **Then** the list
   shows them, clearly marked as inactive.
3. **Given** several products exist, **When** I type a fragment of a name or of a SKU, **Then** the list
   shows only products that contain it, ignoring letter case.
4. **Given** products of several units exist, **When** I filter by Hour, Item or Service, **Then** only
   that unit is listed, and combining the filter with a search applies both.
5. **Given** nothing matches my search or filters, **When** the list refreshes, **Then** I see a clear
   "no products match" message rather than a blank screen.
6. **Given** more products exist than fit on one page, **When** I view the list, **Then** I see the first
   page, the total number of matches, and can move to other pages.
7. **Given** I open an active product, **When** the details appear, **Then** I see its name, SKU, unit,
   unit price in SAR, VAT percentage, whether it is active, and when it was created and last changed.
8. **Given** I am an Agent, **When** I browse the list and open a product, **Then** I see no add, edit or
   deactivate controls, and I never see deactivated products (opening one by its address shows "product
   not found").
9. **Given** a product that does not exist, **When** I open it (for example from an old link), **Then** I
   see a clear "product not found" message.
10. **Given** product names are written in Arabic or in English, **When** I search for them, **Then**
    both are found correctly.

---

### User Story 3 - Admin edits a product and deactivates it to hide it (Priority: P3)

An Admin corrects a product's name, unit, price or VAT percentage, and deactivates a product that should no
longer be offered (or reactivates it later). A deactivated product disappears from the everyday list and
from the Agents' view, but keeps all of its data so it can be brought back.

**Why this priority**: Price lists change all the time and products retire, but the list is usable for a
first demonstration with add, browse and search alone.

**Independent Test**: As the Admin, edit a product's price and VAT and confirm the changes show; try to
change its SKU to another product's and confirm it is refused; deactivate it and confirm it leaves the
everyday list but can be revealed and reactivated; then confirm an Agent cannot do any of these.

**Acceptance Scenarios**:

1. **Given** an existing product, **When** an Admin changes its name, unit, price or VAT percentage and
   saves, **Then** the changes are saved and the "last changed" time is updated.
2. **Given** an existing product, **When** an Admin saves it without changing its SKU, **Then** it is
   **not** treated as a duplicate of itself.
3. **Given** two products exist, **When** an Admin changes one's SKU to the other's (in any letter case),
   **Then** the save fails with the same clear duplicate-SKU message and the product keeps its old SKU.
4. **Given** an active product, **When** an Admin deactivates it, **Then** it leaves the default product
   list and the Agents' view, and all of its details are unchanged.
5. **Given** a deactivated product, **When** an Admin reactivates it, **Then** it appears in the default
   list again.
6. **Given** a deactivated product, **When** anyone tries to create another product with its SKU, **Then**
   it is refused: a deactivated product keeps its SKU reserved.
7. **Given** I am an Agent, **When** I try to edit, deactivate or reactivate a product by any means,
   **Then** I am refused as forbidden and nothing changes.
8. **Given** a product that does not exist, **When** an Admin tries to edit or deactivate it, **Then** they
   see a clear "product not found" message.

---

### Edge Cases

- Two Admins save products with the same SKU at the same moment: exactly one succeeds and the other is
  refused with the duplicate-SKU message; a duplicate is never created.
- A SKU typed in lower case or with surrounding spaces is saved in upper case without the spaces, so
  `  cns-001 ` is stored as `CNS-001`.
- A price with more than two decimal places (for example `10.005`) is refused with a message; it is never
  silently rounded. `100`, `100.5` and `100.50` are accepted and shown as `100.00` and `100.50`.
- A negative price, or a price above 999,999,999.99, is refused. A price of exactly 0 is allowed.
- A VAT percentage below 0 or above 100, or with more than two decimal places, is refused. A VAT of exactly
  0 is allowed and is kept (it is not replaced by the 15% default).
- A very long name or SKU is refused with a message naming the field and its maximum length.
- Names in Arabic, English, or a mix are stored and shown exactly as entered, and are searchable.
- A search fragment made only of spaces is treated as no search.
- Changing a product's price or VAT never changes anything already issued to a customer: this spec creates
  no invoices, and later invoices will keep their own copy of the price and VAT they were raised with.
- Signed-out visitors, or someone whose session has expired, cannot see or change any product (handled by
  002: they are sent to sign-in).
- An Agent's session token used directly against the back end to create, edit or deactivate a product is
  refused as forbidden, exactly as the screens refuse it.
- Two Admins edit the same product at the same time: the last save wins (see Assumptions).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Any signed-in staff member (Admin or Agent) MUST be able to list, search and view products.
  Only an Admin MUST be able to add, edit, deactivate or reactivate a product. Anyone not signed in MUST be
  refused everything.
- **FR-002**: A staff member with the Agent role who attempts any change to a product, through the screens
  or directly against the back end, MUST be refused as forbidden and nothing MUST be created or changed.
  The screens MUST hide the add, edit and deactivate controls from Agents and MUST show an access-denied
  message if an Agent opens an add or edit address.
- **FR-003**: A product MUST have a name, a SKU, a unit (exactly `Hour`, `Item` or `Service`), a unit price
  in Saudi riyals, a VAT percentage, and an active/inactive state; it is active when first created.
- **FR-004**: The SKU MUST be unique across all products, active and inactive, compared without regard to
  letter case and ignoring leading and trailing spaces. It is saved in upper case.
- **FR-005**: Saving a product whose SKU is already used by a different product, whether adding or editing,
  MUST be refused with a conflict response and a clear message that the SKU is already used; nothing MUST be
  created or changed.
- **FR-006**: Saving a product with a missing required field or an invalid value MUST be refused with a
  validation response that names each offending field. The rules are: name required, at most 200
  characters; SKU required, at most 50 characters, made only of letters, digits, hyphens and underscores;
  unit required and one of the three allowed values; unit price required, from 0.00 to 999,999,999.99
  with at most two decimal places; VAT percentage from 0 to 100 with at most two decimal places, and 15
  when left empty on a new product.
- **FR-007**: Money MUST be exact: unit prices are held and shown in SAR with exactly two decimal places, and
  no value is ever rounded or altered by the system without the person seeing it (a value with too many
  decimal places is refused, not rounded). Money MUST NOT be handled with approximate (floating-point)
  numbers anywhere (Constitution Principle IV).
- **FR-008**: Leading and trailing spaces MUST be removed from every text field before it is saved or
  compared.
- **FR-009**: Editing a product MUST NOT count the product's own current SKU as a duplicate.
- **FR-010**: An Admin MUST be able to deactivate and reactivate a product. A deactivated product MUST NOT
  appear in the default product list or anywhere an Agent can see it, MUST keep all its data and its
  reserved SKU, and MUST be readable by later features (specifically, the invoice spec, which will only
  offer active products). This spec itself does not create invoices.
- **FR-011**: The default product list MUST show only active products. An Admin MUST be able to switch the
  list to show only inactive products or both, and inactive products MUST be clearly marked wherever an
  Admin sees them. Agents MUST always see only active products; a deactivated product opened by an Agent
  MUST be reported as not found.
- **FR-012**: The system MUST NOT offer permanent deletion of a product in this spec; deactivating is how a
  product is retired.
- **FR-013**: The list MUST support a text search matching a fragment of the name or the SKU, ignoring
  letter case, and a filter by unit; search and filter MUST combine. It MUST be sorted by name, split into
  pages with a default page size of 20 (a maximum of 100), and MUST report the total number of matching
  products and the current page. A page number beyond the last page MUST give an empty page with the real
  total, not an error.
- **FR-014**: A product MUST record when it was created and when it was last changed, and both MUST be shown
  in its details.
- **FR-015**: Opening, editing or deactivating a product that does not exist MUST produce a clear "not
  found" outcome, not a crash or a blank screen.
- **FR-016**: When a save is refused for any reason, the person's entered values MUST remain on the screen;
  loading, empty and error states MUST be shown for the list and for the details.
- **FR-017**: Refusals (validation, conflict, not found, forbidden, not signed in) MUST be returned as
  structured error responses, not as unhandled failures (Constitution Principle III).
- **FR-018**: Names and search text in Arabic and English MUST be stored, shown and searched correctly.
- **FR-019**: The repository MUST include automated tests covering: an Admin adding `CNS-001` at price 100
  and VAT 15 and seeing it listed; the default VAT of 15 and an explicit 0; duplicate SKU on add (including
  different case and spaces) and on edit; editing without changing the SKU; each validation rule including
  the price and VAT limits and decimal places; search by name and SKU; the unit filter and its combination
  with search; paging; deactivate and reactivate; the Agent being refused (403) on add, edit and
  deactivate, and unable to see inactive products; not found; and refusal of signed-out requests.
- **FR-020**: This feature MUST NOT include inventory or stock of any kind (quantities, stock levels,
  warehouses), invoices, discounts, price history, product categories, images, import or export of
  products, permanent deletion, or any shop, cart, payment, ticket or WhatsApp functionality.

### Key Entities

- **Product**: Something BillingCRM can bill for. Has a name, a unique SKU code, a unit (Hour, Item or
  Service), a unit price in SAR, a VAT percentage, an active/inactive state, and the times it was created
  and last changed. It is the record later invoice lines will be built from. It is a price-list entry, not
  a stocked good: there is no quantity on hand.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Admin can add a product in under 1 minute from opening the product list, and it appears in
  the list on the next refresh.
- **SC-002**: 100% of attempts to save a second product with a SKU already in use, including attempts that
  differ only in letter case or surrounding spaces, are refused with the clear duplicate-SKU message and
  create nothing, whether adding or editing.
- **SC-003**: 100% of attempts by an Agent to add, edit, deactivate or reactivate a product are refused, both
  through the screens and directly against the back end; 100% of the same attempts by an Admin succeed
  (for valid input).
- **SC-004**: 100% of deactivated products are absent from the default list and from every Agent's view, and
  100% remain available to an Admin who asks to see them.
- **SC-005**: Every price and VAT value shown on screen equals the value that was saved, with prices showing
  exactly two decimal places; 0 saved values are changed by rounding.
- **SC-006**: With 5,000 products stored, a search or filter returns its first page in under 1 second.
- **SC-007**: 100% of product operations attempted without a valid sign-in are refused.
- **SC-008**: All automated tests in the repository pass on the developer's machine, and the developer can,
  in a browser as Admin, add, search, edit and deactivate a product, and as Agent, browse it read-only and
  see the add page refused.

## Assumptions

- 002-staff-auth is complete and verified: staff can sign in with the roles Admin and Agent, the Admin-only
  rule is enforced by the back end, and sessions expire. 003-customers is complete and verified, and its
  patterns (duplicate-value refusal, paging, search, deactivate instead of delete) are reused.
- "CRUD" from the request is realised as create, read, update, and deactivate/reactivate; there is no
  permanent deletion, in line with customers, because later invoices will refer to products. Deactivating is
  the "hide" the request asks for.
- "Hide" means a deactivated product is left out of the default list and out of the Agents' view. It is not
  deleted, and an Admin can reveal it and reactivate it.
- The SKU is a short code of letters (A to Z, as in English), digits, hyphens and underscores (for example
  `CNS-001`); it is compared ignoring letter case and is stored in upper case. Arabic letters are not allowed
  in a SKU (names may be Arabic).
- Prices are always in Saudi riyals (SAR); there is no currency field. A price of 0 is allowed (for example a
  free item); negative prices are not.
- The VAT percentage is per product (15 by default, 0 to 100 with up to two decimals). This spec only
  stores it; calculating VAT amounts belongs to the invoice spec, which will round line VAT to two
  decimals.
- The unit is one of exactly three values; adding a unit later is a change to this specification.
- A product has no description, category, image or price history in this spec.
- When two Admins edit the same product at once, the last save wins; no "someone else changed this" warning
  exists.
- The expected volume is up to about 5,000 products, listed alphabetically by name.
- A page number too large for a 32-bit whole number is refused with a message that names the page field, as is a page
  size outside 1 to 100; any page number that fits, however large, gives an empty page with the real total.
- The unit is accepted in any letter case (`hour`, `HOUR`) and is always stored and shown as Hour, Item or Service.
- Deactivating or reactivating a product leaves all of its data unchanged but does move its "last changed" time.
- A price such as `100.500` is accepted (same value as `100.50`); only a real third decimal digit is refused.
- If the session expires while a product is being saved, the person is sent to sign-in (spec 002) and what
  they had typed is lost; every other refusal keeps the typed values.
- Shop, cart, payment gateway, support tickets and WhatsApp integration remain out of scope for the whole
  product (Constitution Principle I). The product list is a price list for invoicing, not a shop.
