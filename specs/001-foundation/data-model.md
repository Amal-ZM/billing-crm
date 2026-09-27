# Data Model: Foundation

**Feature**: 001-foundation | **Date**: 2026-09-24

## Entities

None. This spec deliberately creates no business entities or tables (FR-007). The database
contains only the migration-history table that EF Core creates on `database update`.

## Persistence scaffolding

| Element | Location | Notes |
|---------|----------|-------|
| `BillingDbContext` | Infrastructure | Empty (no `DbSet`s). Registered with SQL Server provider using `ConnectionStrings:DefaultConnection`. Migrations assembly = Infrastructure. |
| Migrations | Infrastructure/Migrations | **None yet.** Tooling only; first migration arrives with the first spec that adds a table (spec 003). |

## Configuration settings (not stored in the database)

| Key | Required | Committed to git? | Source on a dev machine |
|-----|----------|-------------------|-------------------------|
| `ConnectionStrings:DefaultConnection` | Yes | **No** | user-secrets or env var `ConnectionStrings__DefaultConnection` |
| `Cors:AllowedOrigins` | Yes (defaulted) | Yes (`["http://localhost:4200"]`) | `appsettings.json`, overridable by env var |

## Rules carried forward

- Money columns will be `decimal(18,2)` (SAR) when they first appear; nothing monetary exists
  yet.
- Never edit an applied migration; never drop the database (`sql-migrations` skill).
