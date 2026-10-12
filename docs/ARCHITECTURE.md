# Architecture

## Starting shape

Use a **modular monolith**: one deployable desktop application and a set of well-separated modules in one solution. Modules communicate through public contracts and application services, not by modifying another module's tables or internal state.

Potential solution boundaries (names are provisional):

- `MG.BusinessPlatform.Desktop` — desktop shell, navigation, accessibility and RTL layout.
- `MG.BusinessPlatform.Application` — use cases, validation, authorization and transaction orchestration.
- `MG.BusinessPlatform.Domain` — core domain types and invariants.
- `MG.BusinessPlatform.Infrastructure` — database, files, printing adapters, clock, logging and external integrations.
- `Modules.*` — accounting, inventory, sales/POS, cafe, gaming, customers/loyalty and optional features.
- `MG.LicenseManager.*` — separate issuer/admin application, not referenced as a runtime feature by the main desktop app.
- `tests/*` — unit, integration and architecture tests.

The final project names and dependency graph are to be settled in the technology prototype.

## Dependency rules

1. Domain code must not depend on UI, database providers or external services.
2. Modules may use another module only through a versioned public contract or an explicitly defined application-level integration.
3. Infrastructure implements interfaces defined by the domain/application layers.
4. UI calls application use cases; it does not issue ad hoc SQL or post accounting entries directly.
5. Authorization is enforced in application services and, where applicable, API endpoints—not only in the UI.
6. Cross-module workflows use explicit orchestration and a single local transaction when the storage/transaction boundary supports it.
7. Integration events are for decoupling notifications and downstream work; they must not replace transactional invariants.

## Data and transaction rules

- Use stable identifiers, UTC timestamps for instants, and explicit business-local time zones for schedules.
- Represent money with decimal/fixed-precision values and explicit currency. Never use binary floating point for money.
- Store posted financial entries as immutable records; corrections use reversals/adjustments with a reason and actor.
- Posted journal entries must balance per currency/defined accounting rules.
- Stock changes are traceable stock movements; current quantity is derived or maintained with a reconciliation strategy.
- An invoice, payment, inventory movement and journal entry are distinct records linked through source references and idempotent posting rules.
- Define concurrency/uniqueness constraints for reservations, invoice numbers, payment references and inventory operations.
- Use versioned migrations and test upgrade paths from supported prior versions.

## Storage and deployment

- Single workstation: SQLite is a candidate for local storage.
- LAN multi-client: use a central service/API and evaluate PostgreSQL; do not place a SQLite database file on a network share for concurrent use.
- Online and multi-branch: ASP.NET Core API with PostgreSQL is the leading direction; offline synchronization requires explicit conflict and ownership rules.
- QR ordering: publish only through a secured HTTPS service or reverse proxy. Never expose database folders, secrets or arbitrary file access.
- Backup/restore: consistent backups, retention, integrity verification, and documented recovery steps are required.

## UI technology decision

WPF is the initial Windows-focused candidate due to mature Windows integration; Avalonia remains a candidate if cross-platform desktop support is an actual near-term requirement. Before final selection, prototype RTL rendering, Persian fonts, keyboard navigation, receipt printing, barcode scanners, packaging, accessibility and UI automation.

## Required architecture tests

- No dependency from Domain to UI/Infrastructure.
- No module writes another module's owned tables.
- Financial postings balance.
- Duplicate requests do not create duplicate invoices/payments/stock movements.
- Database migrations and backup/restore are tested.
