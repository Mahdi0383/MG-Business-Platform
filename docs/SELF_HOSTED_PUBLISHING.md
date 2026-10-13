# Public domain publishing on the merchant's Windows computer

The desktop app can start the customer storefront, generate a Caddy reverse-proxy configuration, request/renew HTTPS certificates automatically, and check the public health endpoint. Caddy is bundled by the Windows release packaging script.

## Guided flow

1. Add products and verify the local storefront at `http://127.0.0.1:8080`.
2. Enter the hostname (for example, `shop.example.com`) in Store Settings and enable online orders.
3. At the domain registrar/DNS provider, configure an A record to the public IPv4 address of the store's internet connection; configure AAAA only if public IPv6 routing is actually available.
4. Use the app's firewall action to add Windows Firewall inbound rules for TCP ports 80 and 443. This asks for Windows administrator consent.
5. In the router, forward TCP 80 and 443 to the Windows computer's reserved LAN IP address. The exact steps differ by router and are not safely automatable for every router model.
6. Select **Start public HTTPS hosting**. Caddy validates its config, listens on the public web ports, obtains/renews certificates automatically, and proxies requests to the loopback-only storefront.
7. Use **Check HTTPS** and test the URL from a phone using mobile data, not only from the same Wi-Fi.

## Failure conditions

- A domain alone does not provide hosting or internet reachability.
- If the ISP uses CGNAT, blocks inbound ports, or the router cannot forward ports, direct hosting will not work. Use a separately configured outbound tunnel/relay or a public server. No application can bypass these network constraints merely by saving a domain.
- Certificate issuance can fail until DNS points to the correct public endpoint and inbound port 80 or 443 validation can reach Caddy.
- If the store PC is shut down, asleep, disconnected, or the application host is stopped, the public storefront is unavailable.
- The app must not show a "published and verified" status until an HTTPS health check succeeds. A same-network check may fail because some routers do not support NAT loopback; confirm externally from mobile data.

## Security boundaries

The storefront listens on loopback port 8080 only. Caddy is the public TLS endpoint. The SQLite database file is never exposed over HTTP. Only customer storefront routes are public; management UI and direct database access are not public API endpoints. Public publication is opt-in and the Windows firewall rule is only created after the user explicitly invokes the firewall action and approves UAC.
