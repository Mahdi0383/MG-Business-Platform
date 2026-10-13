# MG Business Platform License Manager

The standalone Windows license manager issues offline, RSA-PSS signed licenses. The production desktop application embeds a pinned RSA public key and never trusts a public key supplied by a customer. The signing private key must remain offline and must never be committed or shipped with the app.

## Production trust anchor

- The trusted public key is embedded in `src/MG.BusinessPlatform.Licensing/TrustedLicensePublicKey.pem`.
- The desktop app and both license-manager interfaces verify against that embedded key.
- The GUI and CLI refuse to issue a production license unless the supplied private key matches that public key.
- Importing a different public key into the customer app is rejected. A local `license-public-key.pem` file is only a convenience copy and is not the trust source.
- The private signing key is not part of the repository, installer, portable ZIP, or source archive.
- The CLI `keygen` command creates a new key pair for a future trust-anchor rotation only. A new pair is not accepted by the current release until the public key is intentionally changed in source and the application is rebuilt and redistributed.

## 1. Read the installation serials

In the main app, open **Settings** and copy both the software serial and hardware serial. The hardware serial is derived from the Windows MachineGuid; reinstalling Windows or changing the machine identity may require reactivation.

## 2. Issue a signed license

Use the private key that matches the public key embedded in this release. Store it in an access-controlled, offline location; never place it in the application folder or source repository.

```powershell
.\\MG.BusinessPlatform.LicenseTool.exe issue "D:\\MG-License-Secrets\\license-private.pem" "D:\\MG-License-Out\\license.json" "MGS-PASTE-SOFTWARE-SERIAL" "MGH-PASTE-HARDWARE-SERIAL" "Customer name" "2027-10-13" "Core,Accounting,Inventory,POS,Storefront,GameCafe" 1 5
```

The expiry date is interpreted as the end of that date in UTC. Supported modules: Core, Accounting, Inventory, POS, Storefront, Cafe, GameCafe, Reservations, Reports, MultiBranch.

The GUI performs the same signing-key match check before issuing a license. Never share the private key with customers.

## 3. Verify a license before delivery

```powershell
.\\MG.BusinessPlatform.LicenseTool.exe verify "D:\\MG-License-Secrets\\license-public.pem" "D:\\MG-License-Out\\license.json" "MGS-PASTE-SOFTWARE-SERIAL" "MGH-PASTE-HARDWARE-SERIAL"
```

The supplied public key must match the embedded production trust anchor. Exit code 0 means valid; non-zero means verification failed.

## 4. Activate on the customer computer

In the main app, open **Settings** and choose **Import license file**. The app checks the signature, software serial, hardware serial, issue time and expiry using the public key embedded in the executable. A license that fails validation is not installed.

The key stored in `%LOCALAPPDATA%\\MGBusinessPlatform\\license-public-key.pem`, if present, is not trusted for validation; changing or deleting it cannot install an alternate signing key. The license document is stored at `%LOCALAPPDATA%\\MGBusinessPlatform\\license.json`.

## Security boundary and recovery

A missing or expired license disables licensed business modules but does not delete business data. This is local desktop software, not tamper-proof DRM; a local administrator can modify binaries or application data. Do not use desktop checks as the only authorization boundary for a remote multi-user service.

Back up the signing private key securely and separately from source control. If it is lost, the currently deployed public key cannot validate licenses signed by a replacement key; a trust-anchor rotation requires a coordinated app rebuild and redistribution. If the private key is compromised, rotate it deliberately and plan customer migration.
