# Customer Web Storefront and Domain Hosting

## Product requirement

A merchant must be able to run the customer-facing web ordering/storefront service on the merchant's own Windows computer, register a domain in the desktop application's settings, and use an in-app guided setup to publish a customer URL. The application must not require the merchant to manually edit configuration files or expose the database folder.

## Important network constraint

A domain name alone cannot make a computer reachable from the public internet. Publishing from a local computer requires all of the following:
- a domain and control of its DNS records;
- a stable public endpoint or a supported relay/tunnel;
- a network/router/firewall path to the local service, or an outbound-only tunnel agent;
- HTTPS with a valid certificate;
- a running and patched host computer and web service;
- an ISP/router that permits the chosen connectivity method.

CGNAT, blocked inbound ports, changing IP addresses, restrictive ISP policies, and power/internet outages can prevent direct hosting. The product must detect and explain these conditions rather than promise that DNS alone will work.

## Supported publication modes

1. **Direct self-hosting**: configure DNS A/AAAA records to the public endpoint; detect public IP changes; optionally configure supported router port mapping with explicit user consent; allow inbound 80/443 only where feasible; reverse-proxy to loopback-only Kestrel. Never expose the app's internal service port directly.
2. **Outbound tunnel/relay**: optional provider adapter where the host opens an outbound authenticated tunnel to a relay. This is the preferred fallback for CGNAT and networks that block inbound connections. Clearly disclose that the relay is a third-party dependency and may incur cost. Cloudflare must not be a mandatory dependency.
3. **External hosted mode**: optional for merchants who do not want their own computer to be always on. This is a distinct deployment choice, not falsely described as local hosting.

## In-app wizard

The desktop wizard must:
1. Verify the domain format and require the merchant to confirm control of it.
2. Ask for the desired hostname (for example, shop.example.com) and hosting mode.
3. Inspect local port conflicts, host service health, Windows firewall, network type, likely CGNAT, and public endpoint where available.
4. Provide provider-specific DNS instructions or integrate an explicitly authorized DNS API. Never ask for or store domain-provider passwords in plaintext.
5. Provision/renew HTTPS automatically using ACME with safe storage for certificate private keys; offer a clear failure state when public validation cannot succeed.
6. Configure the reverse proxy and bind the app service to loopback/private interfaces only.
7. Create a health check and end-to-end external verification step before declaring publication complete.
8. Show the public URL, QR code, last health-check time, certificate expiry, current mode, and actionable diagnostics.
9. Provide one-click pause/unpublish and safe recovery after Windows restarts, updates, IP changes, and temporary outages.

## Security requirements

- Customer web endpoints must never read SQLite/database files directly over HTTP.
- Storefront calls a narrow authenticated application API.
- Admin and customer surfaces use separate authorization policies and route groups.
- Never expose administrative APIs, diagnostics, local file paths, backups, license private keys, or internal host data publicly.
- Use TLS, secure cookies, CSRF protection where applicable, strict input validation, request size limits, rate limits, security headers, and safe logging that redacts secrets.
- Store secrets using Windows DPAPI/credential storage where applicable, not source control or plaintext configuration.
- Apply explicit origin/host validation, prevent DNS rebinding and host-header attacks, and restrict reverse-proxy forwarding headers to trusted proxies.
- Public orders must be idempotent and transactionally reserve stock where supported; prices are recalculated server-side and never trusted from the browser.
- Provide abuse controls, order throttling, maintenance mode, and an auditable publication configuration.

## Architecture direction

Use a versioned ASP.NET Core web host/API for the customer storefront and admin integration, plus a Windows background host/service for startup/recovery. Keep business rules in shared application/domain libraries. The desktop UI is the configuration client; it must not be the public web server itself. Use provider-neutral interfaces such as IDomainDnsProvider, IPublicationTransport, ICertificateManager, IHostHealthProbe, and IPublicationOrchestrator. Implement direct hosting first, then one tunnel provider adapter after security review.

## Acceptance criteria

- With a routable public endpoint and valid DNS, a clean Windows install can complete the guided setup, provision HTTPS, and serve the storefront on the registered hostname.
- With CGNAT or blocked inbound traffic, the wizard detects likely failure and offers an available tunnel/relay mode instead of reporting success.
- Reboot recovery works without a logged-in desktop session where Windows service installation is authorized.
- HTTPS is verified externally before showing "Published".
- Certificate renewal, IP changes, network loss, rollback, and unpublish have tests.
- Port 8080 or any internal app port is never opened to the whole internet by default.
- No database file or administrative endpoint is publicly exposed.
