# Product Vision

## Purpose

Build one dependable business platform with a shared business core and optional industry modules. A business should not need separate disconnected applications for customers, sales, stock, payments and reporting.

## Target users

Small and medium businesses, initially targeting Persian-speaking businesses and the Iranian market, including cafes, restaurants, game cafes, retail shops, supermarkets, clothing stores, repair shops, service businesses and wholesalers.

## Experience goals

1. A guided first-run wizard asks for business name, country/currency, language, business type, operating mode and initial administrator.
2. The chosen business type recommends a useful module set and workflows; the operator can enable other licensed modules later.
3. Routine local sales and operational workflows work without an internet connection.
4. Users only see actions permitted by both their role and the active license.
5. Financial reports reconcile to posted ledger entries and have traceable source documents.
6. Backups can be restored and verified, not merely created.
7. Persian RTL, localization, accessible keyboard workflows, barcode input and receipt printers are considered from the beginning.

## Product boundaries

The first release is a Windows desktop product for a single workstation. Architecture must leave a safe path to LAN multi-user and hosted multi-branch modes without pretending that a single SQLite file shared over a network is a supported database server.

Public QR ordering and remotely accessible services require a reachable service endpoint. A powered-off shop computer cannot serve public orders. Database files must never be exposed directly to the public internet.

## Quality attributes

- Correctness and auditability over premature feature count.
- Recoverability after application, device or power failure.
- Predictable performance with representative business data.
- Explicit data ownership and migration strategy.
- Secure defaults, minimal privileges and protected secrets.
- Testable module contracts and understandable error messages.

## Non-goals for the initial milestone

- Microservices before there is a demonstrated need.
- Claiming legal/tax compliance without a market-specific requirements review.
- Treating hidden UI controls as authorization.
- Advertising features as implemented before they are built and tested.
