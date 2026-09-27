# Contract: Auth and Admin API

**Feature**: 002-staff-auth | Base address in development: `http://localhost:5080`

Every operation declares its access requirement. The fallback policy is "authenticated user", so
an undeclared endpoint is closed. Anonymous operations are exactly `GET /health` (001) and
`POST /api/auth/login`.

| Operation | Access |
|-----------|--------|
| `GET /health` | Anonymous (explicit) |
| `POST /api/auth/login` | Anonymous (explicit) |
| `GET /api/auth/me` | Any signed-in user (Admin or Agent) |
| `GET /api/admin/status` | Role `Admin` only (policy `AdminOnly`) |

## POST /api/auth/login

Request (`application/json`):

```json
{ "email": "someone@example.com", "password": "..." }
```

- `email`: required, non-empty after trimming; compared case-insensitively.
- `password`: required, non-empty. Never logged, never echoed.

### 200 OK

```json
{
  "accessToken": "<signed token>",
  "expiresAtUtc": "2026-09-24T12:00:00Z",
  "email": "admin@crm.local",
  "role": "Admin"
}
```

### 400 Bad Request — missing or empty field (`application/problem+json`)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "email": ["Email is required."], "password": ["Password is required."] }
}
```

### 401 Unauthorized — wrong password **or** unknown email (`application/problem+json`)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Invalid credentials",
  "status": 401,
  "detail": "Email or password is incorrect."
}
```

- Identical body and status for a wrong password and an unknown email (spec FR-003). No token is
  issued. Response time does not depend on whether the email exists (dummy hash verification).

## GET /api/auth/me

Requires `Authorization: Bearer <token>`.

### 200 OK

```json
{ "email": "agent1@crm.local", "role": "Agent" }
```

## GET /api/admin/status

Requires `Authorization: Bearer <token>` with role `Admin`. Placeholder with no business content.

### 200 OK

```json
{ "area": "admin", "status": "ok" }
```

## Refusals that apply to every protected operation

| Situation | Status | Body |
|-----------|--------|------|
| No `Authorization` header | 401 | `application/problem+json`, title "Unauthorized" |
| Malformed, tampered, wrong-issuer/audience, or expired token | 401 | same as above |
| Valid token, insufficient role (Agent on `/api/admin/*`) | 403 | `application/problem+json`, title "Forbidden" |

Bodies never contain the token, the signing key, password material, or exception text.

## Token claims

`sub`, `email`, `role` (`Admin` or `Agent`), `jti`, `iat`, `nbf`, `exp`, `iss`, `aud`. Lifetime
`Jwt:ExpiryMinutes` (default 60), zero clock skew. See [data-model.md](../data-model.md).

## UI contract (Angular routes)

| Route | Access | Behaviour |
|-------|--------|-----------|
| `/login` | Public | Email + password form. Query `reason=expired` shows "Your session expired. Sign in again."; query `returnUrl` is honoured after sign-in. A signed-in user who opens it is redirected to `/` |
| `/` (shell + home) | Signed in | Shell shows email, role, **Sign out**. Anyone else → `/login?returnUrl=...` |
| `/admin` | Signed in **and** role Admin | Placeholder page. Agent → `/access-denied` |
| `/access-denied` | Signed in | "You do not have access to this area." inside the shell |
| Any request returning 401 | — | Session cleared, → `/login?reason=expired&returnUrl=...` |

The shell shows the **Administration** link only when the role is Admin. Hiding it is a
convenience; the API is the enforcement point.

## Contract tests (backend)

1. Login as seeded-equivalent Admin and Agent → 200, correct `role`, token accepted by `/api/auth/me`.
2. Wrong password and unknown email → 401 with identical bodies, no `accessToken`.
3. Empty email or password → 400 problem details naming the field.
4. `/api/auth/me` with no token → 401; with a tampered token → 401; with a token whose `exp` is
   in the past → 401.
5. `/api/admin/status`: Admin → 200; Agent → 403; no token → 401.
6. `/health` with no token → still 200 (anonymous).
7. Missing or short `Jwt:SigningKey` → start-up fails with a message naming `Jwt:SigningKey`
   and containing no value.
