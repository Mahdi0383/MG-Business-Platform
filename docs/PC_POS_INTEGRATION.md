# PC-POS Device Integration Architecture

Status: partial implementation. The desktop now includes local PC-POS profile configuration, validation, and a TCP socket reachability check. No vendor-specific payment protocol or real payment transaction is implemented or certified; see the limits below.

## Implemented profile settings (current branch)
- Persian RTL UI can save/edit/delete named profiles for manual, TCP/IP, RS-232, USB virtual COM, vendor local API, Windows service, and vendor SDK connection categories.
- Profiles capture vendor/model/driver/protocol identifiers, host/port, serial parameters, timeouts, currency, merchant/terminal identifiers, enabled/default flags, and an opaque authentication reference.
- Non-secret profile configuration is stored locally in `%LOCALAPPDATA%\\MGBusinessPlatform\\payment-terminal-profiles.json`; no passwords or payment credentials are stored in this file.
- The TCP test opens a socket only. It does not send a payment command, verify terminal protocol readiness, confirm approval, or mark an invoice paid. Other modes explicitly report that a vendor-specific driver/handshake is still required.
- Real terminal payments, status inquiries, reversals, refunds, settlement, and physical-device compatibility remain unimplemented until the exact vendor/model protocol or official SDK is available.

## Goal

Provide a settings area for configuring payment-terminal integrations without coupling checkout logic to one vendor. Support a pluggable driver model so new terminal models and connection protocols can be added without rewriting sales or accounting.

## Non-negotiable safety rules

- Never store card PINs, full track data, CVV, or other sensitive authentication data.
- Do not send arbitrary commands to a terminal from an untrusted storefront or customer-facing endpoint.
- Keep payment-terminal communications on the local trusted workstation/network; expose only the minimum required local service.
- Never mark an invoice paid because a socket connected or a terminal merely returned a generic success string. Require a validated, correlated transaction result.
- Use idempotency/correlation IDs and prevent duplicate payment posting after retries, timeouts, application restarts, or repeated terminal callbacks.
- A timeout is an unknown transaction outcome, not proof of failure. Provide a reconciliation/status inquiry workflow before retrying a potentially approved payment.
- Persist an audit trail of operator actions and sanitized transaction outcomes; never log secrets or sensitive cardholder data.
- All device configuration changes must be validated and saved separately from performing a live payment. A connection test must not initiate a charge.

## Supported integration categories

The UI should be driver-based and advertise only modes supported by the selected driver:

1. Vendor PC-POS / ECR protocol — vendor SDK, local service, or documented ECR protocol.
2. Serial / RS-232 — COM port, baud rate, data bits, parity, stop bits, read/write timeouts.
3. USB virtual COM — same serial settings, with optional device discovery.
4. TCP/IP LAN — host/IP, port, connection/read timeout, TLS capability where supported.
5. Vendor local API / SDK — endpoint, SDK version, authentication method, and callback/polling capability.
6. OS-mediated integration — vendor-installed Windows service or approved helper application.
7. Manual / external terminal — operator confirms the payment result and records the reference; safe fallback when no supported protocol is available.

USB HID, Bluetooth, Android terminals, and cloud APIs must be added only through a driver that documents its protocol and security model. A generic “supports every device” claim is not valid without vendor protocol details.

## Proposed boundaries

- PaymentIntegration.Contracts: stable interfaces and DTOs shared by the desktop app and drivers.
- PaymentIntegration.Application: configuration validation, orchestration, transaction state machine, idempotency and reconciliation.
- PaymentIntegration.Infrastructure: encrypted secret storage, driver discovery, local IPC, serial/network primitives and sanitized diagnostics.
- PaymentIntegration.Drivers.*: isolated vendor/protocol adapters; each driver declares supported connection types, capabilities and protocol version.
- Desktop.Settings.PaymentTerminals: Persian RTL UI for device profiles and diagnostics.
- Existing Sales/Payments/Accounting modules remain the source of truth for invoice balances and journal posting. Drivers must not write accounting tables directly.

These are proposed boundaries; align folder/project names with the current solution before implementation.

## Settings UI

Add Settings → Payment Terminals (PC-POS) with these sections:

### Device profiles
- Profile name, provider/vendor, terminal model, driver, protocol/version.
- Enabled/disabled state; default terminal; allowed currency and payment types.
- Environment mode if supported by the vendor (test vs production).
- Store/terminal identifiers as required by the documented protocol.
- Connection type and only the fields relevant to that type.
- Per-device connect/read/write timeouts and retry policy, with bounded safe defaults.
- Save configuration, Test connection, Refresh status, Set as default, and Disable actions.

### Connection fields
- Serial: COM port, baud rate, data bits, parity, stop bits, flow control.
- TCP/IP: host, port, connect timeout, response timeout, TLS/certificate settings where supported.
- Local API/service: base endpoint, service availability, API version, authentication reference.
- Vendor SDK/service: installation/status detection and driver-specific settings.
- Manual: operator workflow, required receipt/reference fields and confirmation policy.

### Payment behavior
- Supported operations advertised by driver: purchase, status inquiry, cancellation/reversal, refund, settlement, report, reprint, where actually supported.
- Require explicit confirmation for operations that can move money.
- Separate terminal approval from application invoice posting.
- Define timeout/unknown-result behavior and reconciliation instructions.
- Configure whether partial payment is permitted at the application layer; never assume the terminal itself supports it.

### Diagnostics and audit
- Connection status and last successful communication.
- Driver name/version and supported capabilities.
- Sanitized recent events, error codes, timestamps and correlation IDs.
- Export diagnostics without secrets.
- Clear warnings for unsupported models/protocols and missing vendor components.

## Driver contract

A driver should provide capabilities and typed operations rather than a generic raw-command UI. At minimum:
- GetCapabilities
- ValidateConfiguration
- TestConnection
- GetTerminalStatus
- StartPayment returning a correlation ID and current state
- QueryPaymentStatus
- CancelOrReverse only when supported by the protocol
- Refund only when supported and authorized
- ParseAndValidateResult

Every result should include a normalized state (Approved, Declined, Cancelled, Pending, Unknown, Error), terminal transaction reference when available, safe provider error code/message, amount/currency, correlation ID and timestamps. A driver must not report Approved unless the documented protocol's approval response has been validated.

## Payment state machine

Created → Sent → Pending/Unknown → Approved | Declined | Cancelled | Error

- Approved: atomically record the payment against the invoice and create required accounting entries exactly once.
- Declined: do not post a payment.
- Pending or Unknown: do not post or automatically retry a new charge; query the original transaction using its correlation/reference.
- Duplicate callbacks/results: return the already recorded outcome and never post twice.
- Application crash after terminal approval but before local persistence: recover through status inquiry/reconciliation; do not create a second charge by default.
- Reversal/refund: create a new linked compensating transaction and audit record; do not silently edit or delete the original payment.

## Configuration and secrets

- Store non-secret settings in versioned application configuration/database records.
- Store credentials/tokens in Windows Credential Manager or DPAPI-protected storage, referencing them by opaque ID.
- Do not put production credentials, certificates with private keys, or terminal secrets in source control, exported diagnostics, backups without protection, or logs.
- Validate hostnames, ports, endpoint schemes, certificate settings and all numeric ranges before saving.
- Restrict profile editing and payment operations through application authorization once identity/permissions are enforced.

## Compatibility and support matrix

Maintain a versioned matrix for each driver: vendor, exact model/firmware, transport, protocol version, required SDK/service, supported operations, currency/amount limits, certification requirements, known limitations and verification evidence. Do not advertise a model as supported until its protocol documentation and a real-device or vendor-simulator verification are available.

## Implementation milestones

1. Add domain contracts, connection types, typed configuration and capability models.
2. Add persistence/migrations for device profiles, with secrets stored by reference only.
3. Add the settings UI and validation without sending payments.
4. Implement manual/external-terminal workflow and tests for duplicate prevention.
5. Implement a vendor adapter only after selecting a specific model and obtaining its official protocol/SDK.
6. Add transaction state machine, status inquiry, reconciliation and crash-recovery tests.
7. Validate on Windows with a vendor simulator and physical terminal; publish a compatibility matrix and evidence.

## Acceptance criteria

- Profiles can be created, edited, validated, enabled/disabled and selected as default.
- Unsupported connection modes cannot be saved as if supported.
- Connection testing never charges money.
- Secrets are not exposed in logs or diagnostics.
- Timeout and duplicate-response tests prove no duplicate payment posting.
- Unknown transaction outcomes require status inquiry/reconciliation rather than blind retry.
- A payment is posted to invoice/accounting only after a validated approval and exactly once.
- UI and all errors are Persian RTL-friendly, accessible and do not rely on native unstyled message boxes.
- Hardware compatibility claims are backed by documented protocol support and test evidence.
