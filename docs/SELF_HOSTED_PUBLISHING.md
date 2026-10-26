# Public domain publishing on the merchant's Windows computer

The desktop app can start the customer storefront, generate a Caddy reverse-proxy configuration, request/renew HTTPS certificates automatically, and check the public health endpoint. Caddy is bundled by the Windows release packaging script.

## Guided flow

1. Add products and verify the local storefront at `http://127.0.0.1:8080`.
2. Enter the hostname (for example, `shop.example.com`) in Store Settings and enable online orders.
3. Select **Diagnose domain and network**. The app reports the resolved A/AAAA records, the detected public IPv4 address, whether DNS matches that address, and whether local ports 80/443 are available.
4. If the domain's DNS zone is managed by ParsPack, optionally enter a ParsPack CDN DNS API token in Store Settings and use **Update DNS A record**. The token is stored in the current Windows user's Credential Manager, not the app settings file. Use a token limited to listing zones/records and creating/deleting DNS records. If the existing A record points elsewhere, the app asks before replacing it and refuses ambiguous/multiple A records or a conflicting CNAME. For other providers, configure the A record manually. Configure AAAA only if public IPv6 routing is actually available.
5. Use the app's firewall action to add Windows Firewall inbound rules for TCP ports 80 and 443. This asks for Windows administrator consent.
6. Optionally select **Configure router ports (UPnP)**. After an explicit confirmation, the app attempts to forward TCP 80 and 443 to this computer on compatible UPnP IGD routers. It records which mappings it created and only removes mappings whose description, internal IP and port still match. If UPnP is disabled/unsupported or the router refuses the mapping, forward the ports manually to the computer's reserved LAN IP address.
7. Select **Start public HTTPS hosting**. Caddy validates its config, listens on the public web ports, obtains/renews certificates automatically, and proxies requests to the loopback-only storefront.
8. Use **Check HTTPS** and test the URL from a phone using mobile data, not only from the same Wi-Fi.
9. After the public URL works externally, open **Store Settings → Link and QR**, generate the storefront QR, and save the PNG for printing. The QR contains the saved HTTPS hostname; it does not configure DNS or make an unreachable site public.

## Failure conditions

- A domain alone does not provide hosting or internet reachability.
- If the ISP uses CGNAT, blocks inbound ports, or the router cannot forward ports, direct hosting will not work. Use a separately configured outbound tunnel/relay or a public server. No application can bypass these network constraints merely by saving a domain.
- Certificate issuance can fail until DNS points to the correct public endpoint and inbound port 80 or 443 validation can reach Caddy.
- If the store PC is shut down, asleep, disconnected, or the application host is stopped, the public storefront is unavailable. During uninstall, the installer verifies PID files against the exact executable paths under the installation directory, stops only the owned Caddy/storefront processes, and preserves the business database and other user data.
- The app must not show a "published and verified" status until an HTTPS health check succeeds. A same-network check may fail because some routers do not support NAT loopback; confirm externally from mobile data.

## Security boundaries

The storefront listens on loopback port 8080 only. Caddy is the public TLS endpoint. The SQLite database file is never exposed over HTTP. Only customer storefront routes are public; management UI and direct database access are not public API endpoints. State-changing browser requests are rejected when their Origin does not match the effective storefront origin or Fetch Metadata marks them as cross-site; non-browser API clients that omit Origin remain supported. Public publication is opt-in; Windows firewall rules require UAC approval, and router TCP 80/443 forwarding requires a separate explicit confirmation. The app only removes UPnP mappings it can verify that it created. ParsPack DNS credentials are stored in Windows Credential Manager for the current Windows account; they are never written to application settings or logs.
