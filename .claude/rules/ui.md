---
paths: "**/*.{ts,html,scss}"
description: BillingCRM Angular rules
---

- Standalone components only, no `NgModule`s. Use Angular Material for standard UI elements.
- Components never call `HttpClient` directly — a feature-scoped service owns API calls and
  typed models.
- Money is always SAR and always a decimal end-to-end; don't re-round a value client-side before
  sending it back to the server.
- Role-aware UI: gate Admin-only actions by role (roles are exactly `Admin`/`Agent`), understanding
  the API is the real enforcement point, not the UI alone.
- Surface `ProblemDetails` error responses to the user instead of swallowing them.
- No shop, cart, payment gateway, tickets, or WhatsApp functionality.
- Before introducing a new state-management approach or shared/base component, explain it to the
  developer first. See `.claude/skills/angular-ui-conventions/SKILL.md` for details.
