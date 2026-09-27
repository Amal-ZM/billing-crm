# Contract: Security Hardening (cross-cutting; no new resource route)

**Feature**: 012-security | Nothing here adds or removes a resource route. Every existing route (customers, products, invoices and their
actions) keeps its own request/response shape exactly as its own spec documents it; this contract only pins the cross-cutting behaviour
that already applies, or now applies, to all of them at once.

## Authentication and authorization (no change in behaviour, made explicit and total)

| Situation | Answer |
|-----------|--------|
| Any write request (`POST`/`PUT` under `/api/customers`, `/api/products`, `/api/invoices`, including send/cancel/payments), no token | `401` |
| The same, with an expired token | `401` |
| The same, with a token whose signature has been altered | `401` |
| An Agent: create/edit/activate/deactivate a product, or cancel any invoice | `403`, before the body is read |
| An Admin: the same four actions | Succeeds, subject to the usual validation and conflict rules |
| Any role: delete a customer, a product, or an invoice of any status | No such route exists; `404` (unknown route) |

## Expected-problem shape (no change in behaviour, made explicit and total)

| Situation | Answer |
|-----------|--------|
| A field fails validation | `400`, naming the field (or `lines[i].field` for an invoice line) |
| An id does not exist | `404` |
| A unique value would be duplicated, or a status change is illegal | `409` |
| Two changes to the same record race (including a database-level conflict) | `409`, never an unhandled `500` |

## New: the decimal converter

| Situation | Answer |
|-----------|--------|
| A `decimal`-bound field (product price/VAT, invoice quantity/discount, payment amount) given as `1e-30` or other exponent notation | `400` naming the field |
| The same, given with more than 28 significant digits | `400` naming the field |
| The same, given as an ordinary number within 28 significant digits, no exponent | Accepted, exactly as before |

## New: free-text control-character guard

| Situation | Answer |
|-----------|--------|
| A free-text field (customer name/address, product name, invoice notes, payment note) containing a bidi override or another disallowed control character | `400` naming the field |
| The same field, containing ordinary long, mixed-language or emoji text within its existing length limit | Accepted, exactly as before |

## New: response headers

| Response | Headers |
|----------|---------|
| Any response | `X-Frame-Options: DENY` |
| Any response under `/api` | `Cache-Control: no-store` (in addition to the above) |

## New: request body size limit

| Situation | Answer |
|-----------|--------|
| A request body over the configured limit (1 MiB) | `413 Payload Too Large`, before any endpoint or validator runs |
| A request body within the limit | Unaffected |

## New: login rate limiting

| Situation | Answer |
|-----------|--------|
| More than the configured number of `POST /api/auth/login` attempts from one source within the window | `429 Too Many Requests`, with `Retry-After` |
| A successful login within the limit | Unaffected |
| A different source's attempts | Not counted against another source's limit |

## New: `Payments` immutability

| Situation | Answer |
|-----------|--------|
| A raw `UPDATE` or `DELETE` against `Payments`, however attempted (raw SQL, `ExecuteUpdate`/`ExecuteDelete`, a tracked entity edit) | Refused by the database (error 51000), nothing changed |
| Recording a new payment (`INSERT`, via `Invoice.RecordPayment` and the normal save) | Unaffected |

## `SECURITY.md`

At the repository root. Sections: Authentication & Sessions; Authorization (a table: operation category -> role); Input Validation;
Output Encoding; Secrets; Known Gaps. Every closed item points at the code or test that enforces it; every open item carries its one-line
reason (HTTPS/HSTS, payment idempotency, history redaction, stale-role window, and `TRUNCATE`'s trigger bypass).

## Acceptance checks (used by the tests and the quickstart)

1. `POST /api/invoices` with no token: `401`, no invoice created (the request's own worked example).
2. Every other documented write route, no token: `401`. The same routes, an expired or tampered token: `401`.
3. An Agent tries to create a product, edit a product, (de)activate a product, or cancel any invoice: `403` each time, before anything
   changes; an Admin succeeds.
4. No delete route exists for a customer, a product or an invoice of any status.
5. One 400/404/409 example per resource, each naming the right field or answering the documented code.
6. `?`/body value `1e5` and a 29-digit number on a decimal field: both `400` naming the field; a normal number still works.
7. A note containing a right-to-left override character: `400` naming the field; the same note with only Arabic/English/emoji: accepted.
8. Any response carries `X-Frame-Options: DENY`; an `/api` response also carries `Cache-Control: no-store`.
9. A body over 1 MiB: `413`. Eleven rapid login attempts from one source: the eleventh is `429` with `Retry-After`; a twelfth from a
   different source succeeds if the credentials are right.
10. A raw `UPDATE`/`DELETE` against `Payments`: refused with error 51000; a normal payment `INSERT` still works.
11. `SECURITY.md` exists at the repository root and every previously-deferred item (004, 007, 008, 009, 010) has an explicit line.
