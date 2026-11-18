# MG Business Web — Initial API Contract

This document defines the first server contract. It is a design baseline; endpoints are not considered implemented until backed by integration tests.

## Conventions

- Control-plane base: `/api/v1/platform`; business base: `/api/v1/businesses/{businessId}`; customer-public base: `/api/v1/storefront/{storefrontKey}`.
- JSON, UTF-8, RFC 3339 timestamps in UTC.
- Money is decimal strings plus ISO 4217 currency code; never accept client-calculated totals as authoritative.
- Cursor pagination for large collections with bounded page size.
- Errors use RFC 9457 Problem Details with stable application error codes.
- Critical mutations require an `Idempotency-Key`.
- Every response includes a correlation ID; sensitive values are never echoed.

## Core A — MG Creator / Control Plane

Platform-owner-only endpoints, protected by distinct creator roles and policies:
- `GET /platform/overview` — platform health and aggregate tenant/service metrics.
- `GET|POST /platform/businesses` — list and onboard businesses.
- `GET|PATCH /platform/businesses/{businessId}` — lifecycle and platform metadata.
- `POST /platform/businesses/{businessId}/suspend`, `/reactivate`, `/offboard`.
- `GET|POST /platform/plans`; `GET|PATCH /platform/plans/{planId}`.
- `GET|POST /platform/modules`; `GET|PATCH /platform/modules/{moduleId}`.
- `GET|PUT /platform/businesses/{businessId}/entitlements`.
- `GET /platform/businesses/{businessId}/database-status`.
- `POST /platform/businesses/{businessId}/provision`; `POST /platform/businesses/{businessId}/migrations`.
- `GET /platform/audit`; `GET /platform/security-events`.
- `POST /platform/support-access-grants` — create narrowly scoped, expiring, audited support access.

Creator APIs must never become a shortcut around business authorization. Tenant-content access is denied by default.

## Identity and business membership

- `GET /auth/session` — current identity and authorized memberships.
- `POST /auth/login`, `POST /auth/logout`, `POST /auth/refresh`.
- `POST /auth/mfa/challenge`, `POST /auth/mfa/verify`.
- `GET /businesses` — businesses accessible to the signed-in identity.
- `POST /businesses/{businessId}/accept-invitation`.
- `GET /businesses/{businessId}/branches`.
- `GET|POST /businesses/{businessId}/users`; `PATCH /businesses/{businessId}/users/{userId}/roles`.
- `GET|POST /businesses/{businessId}/invitations`.

Business IDs in routes are selectors only; server authorization must verify active membership and scope on every request. Customer identities and CRM records are separate concepts from staff membership.

## Core B — Business resources

- `GET|POST /businesses/{businessId}/products`
- `GET|PATCH /businesses/{businessId}/products/{productId}` (archive where referenced)
- `GET|POST /businesses/{businessId}/customers`
- `GET|POST /businesses/{businessId}/suppliers`
- `GET /businesses/{businessId}/inventory/balances`
- `GET|POST /businesses/{businessId}/inventory/movements`
- `GET|POST /businesses/{businessId}/sales`
- `POST /businesses/{businessId}/sales/{saleId}/payments`
- `GET|POST /businesses/{businessId}/orders`
- `POST /businesses/{businessId}/orders/{orderId}/transitions`
- `GET /businesses/{businessId}/accounting/accounts`
- `GET|POST /businesses/{businessId}/accounting/journals`
- `POST /businesses/{businessId}/accounting/journals/{journalId}/post`
- `GET /businesses/{businessId}/reports/{reportKey}`
- `GET|PUT /businesses/{businessId}/devices/{deviceId}/sync-state`
- `POST /businesses/{businessId}/sync/commands` — submit queued offline commands for server-side validation/replay.

## Customer storefront and portal

- `GET /storefront/{storefrontKey}/public-profile`
- `GET /storefront/{storefrontKey}/catalog`
- `GET /storefront/{storefrontKey}/availability`
- `POST /storefront/{storefrontKey}/orders` and `/bookings` (guest or authenticated according to business policy).
- `GET /storefront/{storefrontKey}/me/orders`, `/me/bookings`, `/me/loyalty` — authenticated customer may access only their own records.

Public storefront APIs expose only explicitly published content. Customer accounts never grant staff or creator permissions.

## Database routing and authority

- The MG central database stores platform registry, creator identities/roles, global identity references, plans, subscriptions, licenses, entitlements, tenant database secret references, provisioning/migration/health metadata and platform audit.
- Each business has its own authoritative business database for branches, memberships, catalog, inventory, sales, orders, CRM, accounting, industry data and business audit.
- Each device may maintain a local cache and durable offline command outbox. Local data is a replica, not an independent authority. Never trust locally stored totals, stock, role claims or entitlement state.
- Client-supplied business IDs never select a raw database connection. The server resolves authorized membership to a tenant database secret reference and applies role/permission/branch/module checks before executing the request.
- Database-per-business is the logical isolation boundary. Multiple isolated databases may share a PostgreSQL cluster initially; selected tenants can move to dedicated clusters.

## Transaction rules

Checkout is one server transaction: re-read active prices and tax rules, validate permissions and stock, reserve/decrement stock with concurrency control, create sale/order and line snapshots, record payment intent or payment, post configured accounting entries, append audit event, and persist idempotency result. External payment capture is coordinated through a payment state machine/outbox; never hold a database transaction open across a network call.

A journal can be posted only if debits equal credits and the period is open. Posted journals cannot be edited or deleted; use reversal and replacement entries. Inventory movements are append-only. Negative stock is an explicit business policy checked server-side.

## Security requirements

- Creator console and business APIs have separate authentication policies, roles, scopes and audit categories.
- Authentication and authorization are server-side; no role claims from localStorage.
- Membership, branch access, object ownership and module entitlement are checked for every request and referenced object.
- CSRF protection for cookie-authenticated mutations; strict CORS allowlist; secure cookies.
- Rate limits on authentication, public storefront, booking and checkout endpoints.
- Validate payload size, content type, schema, file type and ownership.
- Audit privileged changes, support grants, database lifecycle actions and financial postings.
- No card PAN/CVV or terminal PIN stored or proxied through the browser.
- Device revocation invalidates sessions and scoped cached data; offline commands are revalidated on replay.
