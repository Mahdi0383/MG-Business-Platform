# MG Business Web — Initial API Contract

This document defines the first server contract. It is a design baseline; endpoints are not considered implemented until backed by integration tests.

## Conventions

- Base path: `/api/v1`.
- JSON request/response; UTF-8; RFC 3339 timestamps in UTC.
- Money: decimal strings plus ISO 4217 currency code (example: `{"amount":"125000.00","currency":"IRR"}`); never accept client-calculated totals as authoritative.
- Pagination: cursor-based for large collections; bounded page size.
- Errors: RFC 9457 Problem Details with stable application error codes.
- Mutating requests: `Idempotency-Key` required for checkout, payment, order submission, inventory posting and journal posting.
- Every response includes a correlation ID; sensitive values are never echoed.

## Identity and workspace

- `GET /auth/session` — current identity and memberships.
- `POST /auth/login`, `POST /auth/logout`, `POST /auth/refresh` — secure session lifecycle.
- `POST /auth/mfa/challenge`, `POST /auth/mfa/verify` — MFA-ready flow.
- `GET /workspaces` — memberships accessible to the signed-in user.
- `POST /workspaces` — create tenant/workspace subject to platform policy.
- `GET /workspaces/{workspaceId}/branches` — branches within authorized workspace.
- `GET /workspaces/{workspaceId}/members` — members and roles.

Workspace identifiers in routes are selectors only; server authorization must verify membership and scope on every request.

## Core resources

- `GET|POST /workspaces/{workspaceId}/products`
- `GET|PATCH|DELETE /workspaces/{workspaceId}/products/{productId}` (delete means archive where referenced)
- `GET|POST /workspaces/{workspaceId}/customers`
- `GET|POST /workspaces/{workspaceId}/suppliers`
- `GET /workspaces/{workspaceId}/inventory/balances`
- `GET|POST /workspaces/{workspaceId}/inventory/movements`
- `GET|POST /workspaces/{workspaceId}/sales`
- `POST /workspaces/{workspaceId}/sales/{saleId}/payments`
- `GET|POST /workspaces/{workspaceId}/orders`
- `POST /workspaces/{workspaceId}/orders/{orderId}/transitions`
- `GET /workspaces/{workspaceId}/accounting/accounts`
- `GET|POST /workspaces/{workspaceId}/accounting/journals`
- `POST /workspaces/{workspaceId}/accounting/journals/{journalId}/post`
- `GET /workspaces/{workspaceId}/reports/{reportKey}`

## Transaction rules

Checkout is one server transaction: re-read active prices and tax rules, validate permissions and stock, reserve/decrement stock using concurrency control, create sale/order and line snapshots, record payment intent or payment, post configured accounting entries, append audit event, and persist idempotency result. External payment capture is coordinated through a payment state machine/outbox; never hold a database transaction open across a network call.

A journal can be posted only if debits equal credits and the period is open. Posted journals cannot be edited or deleted; use reversal and replacement entries.

Inventory movements are append-only. Stock counts create adjustment movements with reason and actor. Negative stock behavior is an explicit tenant policy, checked server-side.

## Security requirements

- Authentication and authorization are server-side; no role claims from localStorage.
- Tenant membership and branch access are checked for every request and every object reference.
- CSRF protection for cookie-authenticated mutations; strict CORS allowlist; secure cookies.
- Rate limits on authentication, public storefront, booking and checkout endpoints.
- Validate payload size, content type, schema, file type and ownership.
- Audit privileged changes and financial postings.
- No card PAN/CVV or terminal PIN stored or proxied through the browser.
- Public endpoints expose only explicitly published catalog and availability.
