# Specification Quality Checklist: Customers

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

- The "409" in the request appears as "conflict response" in FR-004; the wire-level status is a
  planning detail.
- Defaulted decisions worth confirming in `/speckit-clarify` (none block planning):
  1. **"Delete" means deactivate** (FR-009): no permanent deletion, because invoices will reference
     customers. This is the biggest interpretation of the request's "CRUD".
  2. **All of name, type, email, phone and address are required**; only the VAT number is optional
     (the request marks only the VAT number optional).
  3. **VAT number is free-form**, not validated as a 15-digit Saudi number; stricter validation
     would reject foreign customers' numbers.
  4. **Address is one free-text field**, not structured.
  5. **Last save wins** on simultaneous edits; no concurrency warning.
  6. Admin and Agent have identical customer permissions.
- The field limits in FR-005 (200 / 256 / 7-20 / 500 / 30) are proposed defaults, chosen to match
  common billing-form practice; adjust in clarification if the business has other limits.
