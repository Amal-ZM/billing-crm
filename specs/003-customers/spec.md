# Feature Specification: Customers

**Feature Branch**: `003-customers`

**Created**: 2026-09-24

**Status**: Done (verified running by the developer 2026-09-24)

**Depends on**: 002-staff-auth (verified running)

**Input**: User description: "Feature 003-customers. Depends on 002. Staff CRUD + search customers: name, type Company/Individual, unique email, phone, optional VAT number, address, active. Duplicate email → 409. Inactive stays listed but must be blocked later for new invoices. Given I am logged in, When I save two customers with the same email, Then the second fails with a clear message. Out of scope: invoices."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add a customer and see it in the list (Priority: P1)

A signed-in staff member adds a new customer by entering their name, whether they are a Company or
an Individual, an email address, a phone number, an optional VAT number, and an address. The
customer is saved, appears in the customer list, and can be opened to view all of its details. If
another customer already uses the same email address, the save is refused with a clear message and
nothing is created.

**Why this priority**: Customers are the first business record in BillingCRM and every later spec
(products, invoices) depends on being able to record who is being billed. Adding and viewing a
customer is the smallest slice that delivers value.

**Independent Test**: Sign in, add one customer and confirm it appears in the list and opens with
all details intact; then try to add a second customer with the same email and confirm it is
refused with a clear message and the list still shows only the first.

**Acceptance Scenarios**:

1. **Given** I am signed in, **When** I save a new customer with a name, a type, an email, a phone
   number and an address, **Then** the customer is saved, appears in the list, and is marked active.
2. **Given** I am signed in, **When** I save a new customer with an optional VAT number as well,
   **Then** the VAT number is stored and shown; **and when** I leave it empty, **Then** the customer
   saves without one.
3. **Given** a customer with the email `a@example.com` exists, **When** I save a second customer
   with the same email, **Then** the second save fails with a clear message saying that email is
   already used by another customer, and no second customer is created.
4. **Given** a customer with the email `a@example.com` exists, **When** I save another customer with
   `  A@Example.COM ` (different letter case and surrounding spaces), **Then** it is still refused
   as a duplicate.
5. **Given** I leave a required field empty or enter an invalid value (for example an email without
   an `@`), **When** I save, **Then** I am told which field is wrong and nothing is saved.
6. **Given** a save was refused for any reason, **When** the message appears, **Then** everything I
   typed is still on the screen so I can correct it.
7. **Given** I open a customer from the list, **When** the details appear, **Then** I see every
   field, whether the customer is active, and when it was created and last changed.

---

### User Story 2 - Find customers quickly (Priority: P2)

A staff member with many customers finds the one they need by typing part of a name, email, phone
number or VAT number, and can narrow the list by customer type and by active or inactive status.
The list is sorted and split into pages so it stays quick and readable with thousands of customers.

**Why this priority**: Adding customers is only useful if they can be found again; the list becomes
unusable once there are more than a few dozen without search.

**Independent Test**: With several customers of both types (some inactive), search by a fragment of
a name, an email, a phone number and a VAT number, then apply the type and status filters, and
confirm each result set is correct, including the case where nothing matches.

**Acceptance Scenarios**:

1. **Given** several customers exist, **When** I type a fragment of a customer's name, **Then** the
   list shows only customers whose name contains it, ignoring letter case.
2. **Given** several customers exist, **When** I type a fragment of an email, a phone number or a VAT
   number, **Then** the list shows the customers that match that field.
3. **Given** customers of both types exist, **When** I filter by Company or by Individual, **Then**
   only that type is listed, and combining the filter with a search applies both.
4. **Given** active and inactive customers exist, **When** I filter by Active or Inactive, **Then**
   only that group is listed; with no status filter, **both** are listed and inactive ones are
   clearly marked.
5. **Given** nothing matches my search or filters, **When** the list refreshes, **Then** I see a
   clear "no customers match" message rather than a blank screen.
6. **Given** more customers exist than fit on one page, **When** I view the list, **Then** I see the
   first page, the total number of matches, and can move to other pages.
7. **Given** customer names are written in Arabic or in English, **When** I search for them,
   **Then** both are found correctly.

---

### User Story 3 - Edit a customer and change its active state (Priority: P3)

A staff member corrects a customer's details, and marks a customer inactive when they should no
longer be billed (or active again when they return). An inactive customer stays in the list and
keeps all of its data; a later spec will use the inactive state to stop new invoices being raised
for that customer.

**Why this priority**: Corrections and deactivation matter for day-to-day upkeep, but the product
is usable for its first demonstration with add, view and search alone.

**Independent Test**: Edit a customer's phone and address and confirm the changes show; change its
email to one already used by another customer and confirm it is refused; mark it inactive and
confirm it remains in the list, clearly marked, and can be made active again.

**Acceptance Scenarios**:

1. **Given** an existing customer, **When** I change its name, type, phone, VAT number or address
   and save, **Then** the changes are saved and the "last changed" time is updated.
2. **Given** an existing customer, **When** I save it without changing its email, **Then** it is
   **not** treated as a duplicate of itself.
3. **Given** two customers exist, **When** I change one's email to the other's (in any letter case),
   **Then** the save fails with the same clear duplicate-email message and the customer keeps its
   old email.
4. **Given** an active customer, **When** I mark it inactive, **Then** it remains in the list, is
   clearly marked inactive, and all its details are unchanged.
5. **Given** an inactive customer, **When** I mark it active again, **Then** it is listed as active.
6. **Given** a customer that does not exist (for example it was opened from an old link), **When**
   I try to view or edit it, **Then** I see a clear "customer not found" message.

---

### Edge Cases

- Two staff members save customers with the same email at the same moment: exactly one succeeds
  and the other is refused with the duplicate-email message; a duplicate is never created.
- A customer keeps the email of an **inactive** customer: still refused; inactive customers keep
  their email reserved.
- An email or name with leading or trailing spaces is saved without them.
- A very long value in any field is refused with a message naming the field and its maximum length.
- Names, addresses and search text in Arabic, English, or a mix are stored and shown exactly as
  entered, and are searchable.
- A search fragment made only of spaces is treated as no search.
- Signed-out visitors, or someone whose session has expired, cannot see or change any customer
  (handled by 002: they are sent to sign-in).
- Two staff edit the same customer at the same time: the last save wins (see Assumptions).
- A VAT number is left empty on an Individual and on a Company: both are allowed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Any signed-in staff member (Admin or Agent) MUST be able to add, view, edit, search,
  and change the active state of customers. Anyone not signed in MUST be refused.
- **FR-002**: A customer MUST have a name, a type (exactly `Company` or `Individual`), an email
  address, a phone number and an address; a VAT number is optional; a customer is either active or
  inactive and is active when first created.
- **FR-003**: The email address MUST be unique across all customers, active and inactive,
  compared without regard to letter case and ignoring leading or trailing spaces.
- **FR-004**: Saving a customer whose email is already used by a different customer, whether adding
  or editing, MUST be refused with a conflict response and a clear message that the email is
  already used; nothing MUST be created or changed.
- **FR-005**: Saving a customer with a missing required field or an invalid value MUST be refused
  with a validation response that names each offending field; the rules are: name required, at
  most 200 characters; type required and one of the two allowed values; email required, at most 256
  characters and shaped like an email address; phone required, 7 to 20 characters made of digits,
  spaces, `+`, `-` and parentheses; address required, at most 500 characters; VAT number, when
  given, at most 30 characters made of letters, digits, spaces and hyphens.
- **FR-006**: Leading and trailing spaces MUST be removed from every text field before it is saved
  or compared; an empty VAT number MUST be stored as "no VAT number".
- **FR-007**: Editing a customer MUST NOT count the customer's own current email as a duplicate.
- **FR-008**: Staff MUST be able to mark a customer inactive and active again. An inactive customer
  MUST stay in the customer list and in search results, keep all its data and its reserved email,
  and MUST be clearly marked inactive wherever it is shown.
- **FR-009**: The system MUST NOT offer permanent deletion of a customer in this spec; making a
  customer inactive is how a customer is retired.
- **FR-010**: The customer's active state MUST be readable by later features (specifically, the
  invoice spec, which will refuse new invoices for inactive customers). This spec itself does not
  create invoices and does not enforce that rule.
- **FR-011**: The customer list MUST support a text search matching a fragment of the name, the
  email, the phone number or the VAT number, ignoring letter case; a filter by type; and a filter by
  active status (active, inactive, or both, with both as the default). Search and filters MUST
  combine.
- **FR-012**: The list MUST be sorted by name, split into pages with a default page size of 20 (a
  maximum of 100), and MUST report the total number of matching customers and the current page.
- **FR-013**: A customer MUST record when it was created and when it was last changed, and both
  MUST be shown in its details.
- **FR-014**: Viewing or editing a customer that does not exist MUST produce a clear "not found"
  outcome; an unknown customer MUST NOT cause a crash or a blank screen.
- **FR-015**: When a save is refused for any reason, the person's entered values MUST remain on the
  screen; loading, empty and error states MUST be shown for the list and for the details.
- **FR-016**: Refusals (validation, conflict, not found, not signed in) MUST be returned as
  structured error responses, not as unhandled failures (Constitution Principle III).
- **FR-017**: Names, addresses and search text in Arabic and English MUST be stored, shown and
  searched correctly.
- **FR-018**: The repository MUST include automated tests covering: add and view, duplicate email on
  add (including different case and spaces), duplicate email on edit, editing without changing the
  email, each validation rule, search by each field, both filters and their combination, paging,
  deactivate and reactivate, not found, and refusal of signed-out requests.
- **FR-019**: This feature MUST NOT include invoices, permanent deletion, import or export of
  customers, multiple contacts per customer, notes or attachments, merging of duplicates, or any
  shop, cart, payment, ticket or WhatsApp functionality.

### Key Entities

- **Customer**: A person or company that BillingCRM bills. Has a name, a type (Company or
  Individual), a unique email address, a phone number, an optional VAT number, an address, an
  active/inactive state, and the times it was created and last changed. It is the record later
  invoices will refer to.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in staff member can add a new customer in under 1 minute from opening the
  customer list.
- **SC-002**: 100% of attempts to save a second customer with an email already in use, including
  attempts that differ only in letter case or surrounding spaces, are refused with the clear
  duplicate-email message and create nothing, whether adding or editing.
- **SC-003**: With 10,000 customers stored, a search or filter returns its first page in under 1
  second.
- **SC-004**: 100% of inactive customers remain visible in the unfiltered list and in searches, and
  are visibly distinguishable from active ones.
- **SC-005**: 100% of refused saves leave the previously stored data unchanged and keep the entered
  values on screen.
- **SC-006**: 100% of customer operations attempted without a valid sign-in are refused.
- **SC-007**: All automated tests in the repository pass on the developer's machine, and the
  developer can add, search, edit, deactivate and reactivate customers in a browser, and see the
  duplicate-email message.

## Assumptions

- 002-staff-auth is complete and verified: staff can sign in, every back-end operation requires a
  signed-in user unless it says otherwise, and sessions expire.
- Admin and Agent have the same customer permissions. Roles matter later (for example for
  Admin-only settings); nothing about customers is Admin-only in this spec.
- "Delete" from the request is realised as making a customer inactive. Permanent deletion is left
  out because invoices will later refer to customers; removing a customer with billing history would
  break those records. If real deletion is wanted, a later spec can add it with rules for customers
  that have no invoices.
- The email address is the only uniqueness rule. Two customers may share a name or a phone number.
- The VAT number is free-form (letters, digits, spaces, hyphens) and is not checked against any tax
  authority's registry or a country-specific format; a stricter rule can be added later.
- The address is one free-text field (which may span several lines); splitting it into street, city
  and postal code is left for a later spec if invoices need it.
- Phone numbers are stored as typed (after trimming); no country-code normalisation is applied.
- When two people edit the same customer at once, the last save wins; no "someone else changed this"
  warning exists in this spec.
- The expected volume is up to about 10,000 customers.
- Customers are listed alphabetically by name; the sort order is not user-selectable in this spec.
- Search matches text by substring only. Arabic letter variants (for example أ and ا) are different
  characters to it and are not folded together; digits typed as Arabic-Indic numerals are accepted in a
  phone number and stored as typed, so a search for the same number in ASCII digits will not find them.
- Search text longer than 100 characters is refused with a message (it cannot match anything useful). A
  page number beyond the last page is not an error: it shows an empty page with the real total.
- If the session expires while a customer is being saved, the person is sent to sign-in (spec 002) and
  what they had typed is lost; every other refusal keeps the typed values.
- The customer type is accepted in any letter case ("company") and always shown as `Company` or
  `Individual`.
- Shop, cart, payment gateway, support tickets, and WhatsApp integration remain out of scope for the
  whole product (Constitution Principle I).
