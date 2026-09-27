---
name: angular-ui-conventions
description: Use when adding Angular pages, services, interceptors, or forms for BillingCRM.
---
# Angular
- Standalone components. Material only (no extra CSS framework).
- HttpClient service per resource. Auth interceptor adds Bearer token.
- Reactive forms. No business totals in the template if the API already returns them.
- States: loading, empty, error on every list.
- Agent vs Admin: hide buttons the role cannot call; API still enforces 403.