---
name: sql-migrations
description: Use when changing entities, DbContext, or SQL Server schema.
---
# EF Core
- Add a migration. Never edit a migration that was already applied.
- decimal(18,2) for money. Invoice line snapshots are columns, not live product joins for price.
- Seed Admin/Agent and sample lookups in a safe HasData or startup seed if missing.
- Do not drop the database.