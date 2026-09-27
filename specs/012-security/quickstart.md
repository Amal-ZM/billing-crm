# Quickstart: Security Hardening

**Feature**: 012-security | Validation guide; `SECURITY.md` at the repository root is the durable reference. Run from the repository
root. Builds on 001 to 011 (LocalDB, connection string, signing key in user-secrets).

## Prerequisites

Specs 001-011 working: you can sign in as `admin@crm.local` (Admin) and `agent1@crm.local` (Agent). One active customer and one active
product, so a write request has something valid to reference where needed.

## 1. Add the `Payments` trigger

```powershell
dotnet ef database update --project backend/src/BillingCrm.Infrastructure --startup-project backend/src/BillingCrm.Api
```

Applies the one new migration (a trigger only; no column, no new table).

## 2. Run

```powershell
dotnet run --project backend/src/BillingCrm.Api      # http://localhost:5080
cd frontend; ng serve                                 # http://localhost:4200
```

## 3. Validate directly against the API

```powershell
# The request's own worked example, no token at all:
curl.exe -i -X POST http://localhost:5080/api/invoices -H "Content-Type: application/json" -d "{}"

# Sign in for a token, then try a restricted action as the Agent:
curl.exe -i -X POST http://localhost:5080/api/products -H "Authorization: Bearer <AGENT_TOKEN>" -H "Content-Type: application/json" -d "{}"

# A decimal field with exponent notation and with too many digits:
curl.exe -i -X PUT http://localhost:5080/api/products/<ID> -H "Authorization: Bearer <ADMIN_TOKEN>" -H "Content-Type: application/json" -d "{\"unitPrice\": 1e5, ...}"

# Headers on any response:
curl.exe -i http://localhost:5080/health

# Eleven rapid login attempts:
for ($i = 1; $i -le 11; $i++) { curl.exe -i -X POST http://localhost:5080/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"nobody@crm.local\",\"password\":\"wrong\"}" }
```

| Check | Expected |
|-------|----------|
| `POST /api/invoices`, no token | `401`; no invoice created (confirm via the invoice list) |
| Every other write route (customers, products, invoices, send, cancel, payments), no token | `401` each time |
| An expired or tampered token on any write route | `401` |
| Agent: create/edit/(de)activate a product, or cancel any invoice | `403` each time; Admin succeeds |
| Looking for a delete route on any of the three resources | None exists (`404`/`405`, never a working delete) |
| A validation mistake, an unknown id, a duplicate value, an illegal status change (one each, any resource) | `400`, `404`, `409` respectively, naming the field where applicable |
| A decimal field given `1e5` or a 29-digit number | `400` naming the field; an ordinary number is still accepted |
| A note containing a right-to-left override character | `400` naming the field; the same note with only Arabic/English/emoji | Accepted |
| Any response | `X-Frame-Options: DENY` header present |
| Any `/api` response | `Cache-Control: no-store` header present, in addition |
| The 11th login attempt from one source within the window | `429` with a `Retry-After` header; the 12th from a different source is unaffected |
| A request body over 1 MiB | `413` |

## 4. Validate `Payments` immutability directly

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d <your database name> -Q "UPDATE Payments SET Amount = 1"
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -d <your database name> -Q "DELETE FROM Payments"
```

| Statement | Expected |
|-----------|----------|
| `UPDATE Payments SET Amount = 1` | Error 51000 "Payments cannot be changed or deleted."; nothing changed |
| `DELETE FROM Payments` | The same error; nothing deleted |
| Recording a payment through the app | Still works |

## 5. Read `SECURITY.md`

Open `SECURITY.md` at the repository root. Confirm it names, in a short checklist: authentication and sessions; a table of which role
each write needs; input validation; output encoding; secrets handling; and every previously-deferred item (from specs 004, 007, 008, 009,
010) with an explicit disposition — closed with a pointer, or still open with its one-line reason.

## 6. Automated

```powershell
dotnet test backend/tests/BillingCrm.Application.Tests
dotnet test backend/tests/BillingCrm.Api.Tests
dotnet test backend/tests/BillingCrm.Infrastructure.Tests      # needs LocalDB
cd frontend; ng test --watch=false; ng build
```

Run the three backend projects one at a time on a busy machine. All must pass.
