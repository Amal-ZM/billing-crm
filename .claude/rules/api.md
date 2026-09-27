---
paths: "**/*.cs"
description: BillingCRM .NET/C# rules
---

- Expected business errors (validation, not-found, conflict, authorization denial) are returned
  as a `Result`/`Result<T>` mapped to `ProblemDetails` at the endpoint boundary — never thrown as
  exceptions. Exceptions are for genuine bugs/infra failures only.
- All money is `decimal`, mapped to `decimal(18,2)` in the database. Never `float`/`double`.
  Currency is always SAR.
- Roles are exactly `Admin` and `Agent`. Every endpoint has an explicit authorization
  requirement.
- No shop, cart, payment gateway, tickets, or WhatsApp functionality.
- No secrets (connection strings, keys) hardcoded in source — use local, gitignored config.
- Before introducing a new endpoint pattern or shared abstraction, explain it to the developer
  first. See `.claude/skills/minimal-api-conventions/SKILL.md` and
  `.claude/skills/sql-migrations/SKILL.md` for details.
