# MG Business Platform License Manager

The standalone installer opens the Persian RTL graphical license manager (`MG.BusinessPlatform.LicenseManager.exe`). Use it to generate a signing-key pair, issue a license from the customer's software/hardware serials, and verify an issued license. A command-line companion (`MG.BusinessPlatform.LicenseTool.exe`) is included for automation.

The license manager is a separate Windows executable. It signs licenses offline using an RSA private key; the main application must receive only the matching public key. Never commit or distribute the private key with the application.

## Commands

Open PowerShell in the installed License Manager directory.

### 1. Generate a signing key pair once

```powershell
.\MG.BusinessPlatform.LicenseTool.exe keygen "D:\MG-License-Secrets\license-private.pem" "D:\MG-License-Secrets\license-public.pem"
```

Create the secret directory first and restrict its Windows ACLs. Keep the private key offline or in an approved secret vault. Back it up securely. Do not regenerate keys for an already-deployed product unless you plan a coordinated public-key rotation.

### 2. Read the installation serials

In the main app, open **Settings** and copy both the software serial and hardware serial. The hardware serial is tied to the Windows MachineGuid; reinstalling Windows or changing the machine identity may require reactivation.

### 3. Issue a signed license

```powershell
.\MG.BusinessPlatform.LicenseTool.exe issue "D:\MG-License-Secrets\license-private.pem" "D:\MG-License-Out\license.json" "MGS-PASTE-SOFTWARE-SERIAL" "MGH-PASTE-HARDWARE-SERIAL" "Customer name" "2027-10-13" "Core,Accounting,Inventory,POS,Storefront" 1 5
```

The expiry date is interpreted as the end of that date in UTC. Supported modules:

- Core
- Accounting
- Inventory
- POS
- Storefront
- Cafe
- GameCafe
- Reservations
- Reports
- MultiBranch

### 4. Verify before delivery

```powershell
.\MG.BusinessPlatform.LicenseTool.exe verify "D:\MG-License-Secrets\license-public.pem" "D:\MG-License-Out\license.json" "MGS-PASTE-SOFTWARE-SERIAL" "MGH-PASTE-HARDWARE-SERIAL"
```

Exit code 0 means valid; non-zero means the document did not validate.

## Installation layout and important limitation

The desktop license reader currently looks for these files in:

`%LOCALAPPDATA%\MGBusinessPlatform\`

- `license-public-key.pem` — public verification key only
- `license.json` — signed license document

Copy the public key and signed license to that data directory using a controlled activation workflow. Never copy the private key there.

**Release warning:** signing and verification are implemented, but licensing is not considered production-enforced until module/expiry entitlements are checked by protected application operations and public-key provisioning is managed safely. Do not advertise this as complete DRM or use it as the only access-control boundary.
