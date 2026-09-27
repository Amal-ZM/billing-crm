---
name: minimal-api-conventions
description: Use when adding or changing ASP.NET Core Minimal API endpoints, Program.cs, filters, JWT, or Result mapping.
---
# Minimal API
- Endpoints in MapGroup per resource: /api/customers, /api/products, /api/invoices.
- Thin endpoints. Business rules in Application services.
- Expected failures → Result.Fail → ProblemDetails (400/401/403/404/409).
- Never throw for duplicate email, illegal status, inactive customer.
- Inject services. No static DbContext.
- After generating, explain the endpoint in 5 lines using MVC words (this is like a controller action).