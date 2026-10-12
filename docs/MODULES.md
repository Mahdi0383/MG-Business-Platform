# Module Boundaries and Contracts

## Shared core

| Capability | Owns | Does not own |
| --- | --- | --- |
| Business & Branch | Business identity, branches, local settings | User authentication secrets |
| Identity & Access | Users, roles, permissions, sessions | License entitlements |
| Catalog | Products, services, units, prices, tax classifications | Stock balances or posted sales |
| Customers & Suppliers | Party records and contact data | Invoice posting |
| Sales | Quotes/orders/invoices/returns and line snapshots | Payment settlement or stock ledger |
| Payments & Cash | Payment records, tenders, cash/bank movements | Invoice line definitions |
| Accounting | Accounts, journals, posting rules, ledger and periods | UI workflows from other modules |
| Inventory | Warehouses, stock movements, counts, valuation | Direct mutation by POS screens |
| Audit | Actor, action, timestamp, reason and correlation references | Secret/password payloads |
| Licensing | Local verified entitlement snapshot and feature access | Issuing private keys in main app |

## Industry modules

- **Cafe/Restaurant:** tables, kitchen tickets, recipes, modifiers, combos, wastage and service workflow.
- **Game Cafe:** devices, sessions, tariff rules, reservations, player allocation and session recovery.
- **Retail:** barcode/variant workflows, serial tracking and retail-specific returns.
- Additional modules must be justified by user needs and reuse the shared sales, payment, inventory and accounting contracts.

## Cross-module example

A bill may contain a drink for 80,000, a sandwich for 180,000 and a gaming session for 150,000, total 410,000. If 200,000 is paid, the remaining balance is 210,000.

The UI may present one combined bill, but the system keeps the sale lines, gaming session, payment, stock movement and accounting posting as distinct linked records. The orchestration must enforce a consistent commit/posting strategy, record source references, and safely recover or retry after interruption. No module should directly edit another module's tables.

## Contract requirements

Each module contract must define:

1. Owned entities and data migrations.
2. Public use cases and DTOs; internal entities stay internal.
3. Permission keys and license entitlement keys.
4. Transaction boundary, idempotency key and retry semantics.
5. Events emitted and whether they are transactional/outbox-backed.
6. Error codes and user-safe messages.
7. Audit requirements and sensitive fields.
8. Unit/integration/architecture tests.

## Feature access

A feature is available only when both conditions hold:
- the license grants the module/entitlement, and
- the signed-in user's permissions allow the requested action.

Both checks must be enforced in application services and server endpoints when present. Hiding a menu item is not authorization.
