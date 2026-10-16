# MG Business Platform License Manager

The standalone Windows license manager issues offline, RSA-PSS signed licenses. The production desktop application embeds a pinned RSA public key and never trusts a public key supplied by a customer. The signing private key must remain offline and must never be committed or shipped with the app.

## Production trust anchor

- The trusted public key is embedded in `src/MG.BusinessPlatform.Licensing/TrustedLicensePublicKey.pem`.
- The desktop app and both license-manager interfaces verify against that embedded key.
- The GUI and CLI refuse to issue a production license unless the supplied private key matches that public key.
- Importing a different public key into the customer app is rejected. A local `license-public-key.pem` file is only a convenience copy and is not the trust source.
- The private signing key is not part of the repository, installer, portable ZIP, or source archive.
- The CLI `keygen` command creates a new key pair for a future trust-anchor rotation only. A new pair is not accepted by the current release until the public key is intentionally changed in source and the application is rebuilt and redistributed.

## Publisher-key bootstrap

A private signing key is intentionally not included in source or release packages. If you do not already possess the private key corresponding to the embedded public key in your build, that build cannot issue customer licenses. Do not try to recover a private key from the public key.

For a first controlled publisher setup, run this on the trusted Windows computer that will own the signing key:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\bootstrap-publisher-key.ps1
```

The script generates a new key pair outside the repository, restricts the private-key file ACL to the current Windows account and SYSTEM, embeds the public key, and runs the full build/test/package pipeline. It restores the previous embedded public key if packaging fails. Keep the private key offline and separately backed up. Never upload it to GitHub, place it in a source archive, or distribute it to customers. The script is included in the full MG Business Platform source archive; it is not included in the standalone License Manager source archive, because it requires the full solution and release pipeline.

**Important:** this changes the trust anchor. Existing installations built with the old public key will not accept licenses signed by the new private key, and licenses signed by the old key will not validate in the rebuilt app. Before using the new build for customers, commit the new public PEM to the source repository and distribute only the matching newly built application and license manager. Treat later key changes as a planned customer migration, not a routine rebuild.

## 1. Read the installation serials

In the main app, open **Settings** and copy both the software serial and hardware serial. The hardware serial is derived from the Windows MachineGuid; reinstalling Windows or changing the machine identity may require reactivation.

## 2. Issue a signed license

Use the private key that matches the public key embedded in this release. Store it in an access-controlled, offline location; never place it in the application folder or source repository.

```powershell
.\\MG.BusinessPlatform.LicenseTool.exe issue "D:\\MG-License-Secrets\\license-private.pem" "D:\\MG-License-Out\\license.json" "MGS-PASTE-SOFTWARE-SERIAL" "MGH-PASTE-HARDWARE-SERIAL" "Customer name" "2027-10-13" "Core,Accounting,Inventory,POS,Storefront,GameCafe" 1 5
```

The expiry date is interpreted as the end of that date in UTC. Supported modules: Core, Accounting, Inventory, POS, Storefront, Cafe, GameCafe, Reservations, Reports, MultiBranch. Dependency rules are validated at issuance and verification: every license includes Core; POS requires both Accounting and Inventory; Reservations requires Storefront.

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
