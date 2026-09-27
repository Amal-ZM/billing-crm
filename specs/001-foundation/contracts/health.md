# Contract: `GET /health`

**Feature**: 001-foundation | **Auth**: none (anonymous, and stays anonymous when auth arrives)

Not under `/api/...` on purpose: it is an operational probe, not a resource group.

## Request

`GET /health` — no body, no required headers. Browser callers from an allowed origin receive the
CORS headers described below.

## Responses

### 200 OK — API up and database reachable

```json
{
  "status": "Healthy",
  "checks": { "database": "Healthy" }
}
```

### 503 Service Unavailable — database unreachable

```json
{
  "status": "Unhealthy",
  "checks": { "database": "Unhealthy" }
}
```

- The body never contains connection strings, server names, or exception text.
- Same shape for 200 and 503; only the values differ. `Content-Type: application/json`.
- Not a `ProblemDetails` response: this is an infrastructure state, not an expected business
  error.

## CORS behaviour (applies to every endpoint)

| Request `Origin` | Result |
|------------------|--------|
| In `Cors:AllowedOrigins` (default `http://localhost:4200`) | Response carries `Access-Control-Allow-Origin: <that origin>` |
| Anything else | No `Access-Control-Allow-Origin` header; browser blocks the call |

## Contract tests (backend)

1. Healthy fake database check → 200, `status` = `Healthy`, `checks.database` = `Healthy`.
2. Unhealthy fake database check → 503, `checks.database` = `Unhealthy`, body contains no
   connection-string text.
3. Request without any `Authorization` header succeeds.
4. `Origin: http://localhost:4200` → allow-origin header echoed; `Origin: http://evil.example` →
   no allow-origin header.
