# Specification Quality Checklist: Create Draft Invoices

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-24
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- The request's own scenario ("Given ABC customer and CNS-001, When I add qty 2 and save, Then status Draft and one
  line qty 2") is US1 scenario 1 and part of FR-020. `invoice-money` is named only as the later owner of totals
  (FR-013), as the request says; nothing here calculates money.
- Defaulted decisions worth confirming in `/speckit-clarify` (none block planning):
  1. **Both Admin and Agent** may create and edit Drafts and view invoices (nothing Admin-only), FR-001.
  2. **"Draft lines can be edited"** includes add, remove and change quantity, and also the due date, notes and
     customer of the Draft; a Draft must keep **at least one line** (FR-008, FR-011).
  3. **No totals at all in this spec**: not shown and not stored (FR-013); the invoice screens show quantity, unit
     price and VAT percentage only. A later spec adds totals per `invoice-money`.
  4. **Line snapshot also copies SKU and unit** (the request names name, unit price and VAT percentage).
  5. **Invoice number** is assigned on creation, counts up by one forever, may have gaps but never duplicates,
     six digits (FR-006).
  6. **Quantity** may have up to two decimals (1.5 hours), 0.01 to 999,999.99 (FR-007); **at most 100 lines**
     (FR-008); **notes up to 1,000 characters** (FR-010).
  7. **Due date** may not be in the past on creation (Asia/Riyadh calendar date); an unchanged past due date is
     still accepted when editing (FR-009).
  8. **A Draft with a since-deactivated product or customer** stays editable: existing lines and the unchanged
     customer are kept, but new lines or a new customer must be active (FR-011).
  9. **A minimal list** (newest first, paged) and a details page are included so a Draft can be found and
     re-opened; search and filters are left for a later spec (FR-014, FR-021).
- The limits (100 lines, 999,999.99 quantity, 1,000-character notes, six-digit number) are proposed defaults;
  adjust in clarification if the business has other limits.
