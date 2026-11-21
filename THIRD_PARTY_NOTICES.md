# Third-party notices

## Caddy

The Windows release package includes the unmodified Caddy 2.11.9 Windows amd64 executable from the official GitHub release asset `caddy_2.11.9_windows_amd64.zip`. The packaging script pins the asset SHA-256 to `ad93112d335ee687100c4c9f11bced16ca590377976b7fe7883882b9e797e472` and refuses to package the binary if the downloaded archive does not match. Caddy is distributed under the Apache License 2.0.

- Caddy source and releases: https://github.com/caddyserver/caddy
- Pinned Caddy 2.11.9 release: https://github.com/caddyserver/caddy/releases/tag/v2.11.9
- Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0

The release packaging workflow records the executable's reported version in `caddy/VERSION.txt`. The Caddy executable is not modified by this project.

## QRCoder

The desktop application uses QRCoder 1.6.0 to generate PNG QR codes for the public storefront URL. QRCoder is distributed under the MIT License.

- QRCoder source: https://github.com/codebude/QRCoder
- MIT License: https://opensource.org/license/mit
