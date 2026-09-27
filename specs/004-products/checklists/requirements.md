# Specification Quality Checklist: Products

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

- "403" from the request appears as "refused as forbidden" (FR-002); the wire-level status is a planning
  detail. "Constitution Principle IV" and "floating-point" appear in FR-007 because the constitution fixes
  the money rule; nothing about how it is stored is prescribed.
- Defaulted decisions worth confirming in `/speckit-clarify` (none block planning):
  1. **"Deactivate to hide" means hidden from the default list and from Agents entirely** (FR-010/011): an
     Admin can reveal inactive products with a status filter. The alternative (as with customers) is to keep
     inactive products in the list with a marker for everyone.
  2. **No permanent delete** for products, as for customers (FR-012); "CRUD" is create/read/update plus
     deactivate.
  3. **SKU is stored in upper case** and limited to letters, digits, hyphens and underscores (FR-004/006).
  4. **A price of 0 is allowed; negative is not; the maximum is 999,999,999.99**; more than two decimals is
     refused rather than rounded (FR-006/007).
  5. **VAT is 0-100 with two decimals, 15 when left empty on create; an explicit 0 is kept** (FR-006).
  6. **An Agent who opens the add page sees "access denied"** (the existing role-guard pattern from spec
     002); the back end answers 403 to the same attempt.
  7. Name and SKU are the search fields; unit is the only filter besides status.
- The field limits (200 / 50 / price range) are proposed defaults; adjust in clarification if the business
  has other limits.
