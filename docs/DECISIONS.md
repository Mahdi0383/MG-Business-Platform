# Architecture Decision Records

This file records current directions. Decisions marked **Provisional** must be validated by a prototype or requirements review.

## ADR-001 — Modular monolith first

**Status:** Accepted.

Build a single deployable application with explicit module boundaries. Avoid microservices until scaling, team ownership or deployment requirements justify the operational cost.

## ADR-002 — Offline-first local operations

**Status:** Accepted.

Essential local sales and operational workflows must work without internet. Hosted QR ordering and remote access require an independently reachable service. A local machine that is offline or powered off cannot accept public requests.

## ADR-003 — Database deployment modes

**Status:** Provisional.

Evaluate SQLite for a single workstation. Use a central service and evaluate PostgreSQL for concurrent LAN or hosted deployments. Do not support concurrent multi-client access by sharing a SQLite database file over a network share.

## ADR-004 — Desktop UI framework

**Status:** Provisional.

Prototype WPF and consider Avalonia only if cross-platform desktop support is a real requirement. Evaluate Persian RTL, printing, barcode input, accessibility, packaging and UI automation before deciding.

## ADR-005 — Accounting source of truth

**Status:** Accepted.

Posted journal entries are the financial ledger source of truth. Business documents remain distinct records; their posting rules link them to payments, stock movements and accounting. Corrections use traceable reversals/adjustments rather than silent edits to posted history.

## ADR-006 — License signing keys

**Status:** Accepted.

The main application contains only the public verification key. The private signing key remains in a separately controlled License Manager or secure issuing service. Offline licenses cannot be guaranteed to be instantly revocable while the customer is disconnected; this limitation must be explicit.

## ADR-007 — Localization and target market

**Status:** Accepted.

Persian/Farsi and RTL are first-class requirements. Iranian tax/accounting obligations require a separate current legal and domain review; architecture alone does not establish compliance.

## ADR-008 — Technology version selection

**Status:** Open.

Select exact .NET SDK, desktop framework, database provider and package versions after compatibility checks. Pin supported versions and document upgrade policy; do not silently assume that the newest package is the safest choice.
