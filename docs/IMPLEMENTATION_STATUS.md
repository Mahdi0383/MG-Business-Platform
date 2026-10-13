# Implementation status

This file distinguishes committed code from validated behavior. "Implemented" means code exists in the repository; it does not imply that CI or manual verification has passed.

## Implemented in source

- .NET 10 solution with Domain, Infrastructure, Storefront, WPF Desktop, signed-license verification, and separate License Manager projects.
- Money value object with currency-safe arithmetic.
- Validated chart-of-accounts definitions, double-entry journal balancing, financial-period boundaries/closure metadata.
- Invoice line arithmetic, payment allocations, catalog item rules, stock movement direction rules, business/branch identities.
- SQLite persistence entities and versioned migrations for storefront products, store settings, web orders, customers, suppliers, costed stock movements, sales invoices, journal entries/lines, chart of accounts and payments.
- Initial versioned EF Core migration and model snapshot.
- Desktop UI for products and unit costs, product editing/availability, manual stock adjustments, inventory movement history, customer/supplier records, atomic single-item POS checkout, web order fulfillment, invoice issuance, cash/card/bank payment collection, trial balance, profit-and-loss and balance-sheet summaries, backup/restore, store settings, activation diagnostics and public HTTPS hosting.
- Public storefront UI and API with server-side price lookup, stock checks, transaction-bound order creation, idempotency keys, rate limits, host allow-listing, request limits, and basic security headers.
- Caddy-based TLS reverse proxy lifecycle management and opt-in Windows firewall rule setup.
- Unit, persistence, migration, backup/restore and storefront integration tests, plus sales accounting service tests for balanced journals, partial payments and overpayment rejection.
- Windows CI and packaging scripts that build the main app, storefront and separate license-manager executable; create the main app installer and standalone license-manager installer; and emit ZIP packages with SHA-256 checksums.
- Separate Persian RTL WPF License Manager GUI plus command-line tool, RSA-PSS signed offline license payload generation/verification, independent installer/ZIP packages, and automated tests for signatures, tampering, expiry, hardware binding and malformed documents.

## Not yet implemented to product scope

- Cash-flow statement, comprehensive AR/AP and supplier purchasing workflows, tax policies, bank reconciliation, inventory cost-layer/weighted-average integration with purchases, period close/reopen and closing entries.
- Desktop workflows for purchase invoices and supplier payments, multi-line POS carts/discounts, refunds/credit notes, barcode scanning, receipt printer setup and end-of-day close.
- Complete cafe/restaurant module: table management, kitchen queue, recipes/ingredients, modifiers, split bills, service charges, tips and fiscal/receipt rules.
- Game-cafe module: session timers, groups/players, per-player pricing, game-room availability, timer recovery and billing.
- Reservations, loyalty, returns/exchanges, repair jobs, wholesale, clothing/size/color variants, multi-warehouse and multi-branch synchronization.
- Robust authentication and role/permission management for multiple local users and remote administration.
- Disaster recovery drills, comprehensive audit event persistence, import/export and data retention controls.
- Provider-specific DNS API adapters and universal outbound tunnel/relay. Direct hosting requires router port forwarding; CGNAT/blocked inbound networks require an external relay or public server.
- Automated migration from prior MG Game Cafe databases, installer upgrade/rollback verification, signed update pipeline, license entitlement enforcement in protected operations, safe public-key provisioning and license activation UX.
- Accessibility, localization coverage, UX review, performance testing, penetration testing, and full Windows operator acceptance testing.

## Verification state

- A Windows GitHub Actions workflow is configured to restore, build, test, publish and package the app.
- Local .NET SDK execution was not available in the authoring environment.
- A previous Windows CI run built the solution but failed one POS stock-tracking assertion; a tracked-entity synchronization fix was committed afterwards. A subsequent Windows run is in progress and must pass build, tests, publishing, both installers, and artifact verification before release status changes.
- Current license code can issue and verify signatures, but the application does not yet enforce module/expiry entitlements across protected operations; licensing remains **NOT PRODUCTION-ENFORCED**.
- Public HTTPS must be tested from outside the store network; local HTTPS checks can be misleading where NAT loopback is unavailable.
- No production-readiness claim should be made until all mandatory [release gates](RELEASE_GATES.md) have evidence.
