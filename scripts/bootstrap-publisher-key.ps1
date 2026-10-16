param(
    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE "MG-License-Secrets\license-private.pem"),
    [string]$PublicKeyPath = (Join-Path $env:USERPROFILE "MG-License-Secrets\license-public.pem")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not [Environment]::OSVersion.Platform.ToString().Equals("Win32NT", [StringComparison]::OrdinalIgnoreCase)) {
    throw "This publisher-key bootstrap script must run on Windows."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$privatePath = [System.IO.Path]::GetFullPath($PrivateKeyPath)
$publicPath = [System.IO.Path]::GetFullPath($PublicKeyPath)
$trustedPath = Join-Path $repoRoot "src/MG.BusinessPlatform.Licensing/TrustedLicensePublicKey.pem"
$keygenProject = Join-Path $repoRoot "src/MG.BusinessPlatform.LicenseTool/MG.BusinessPlatform.LicenseTool.csproj"
$publishScript = Join-Path $repoRoot "scripts/publish-win-x64.ps1"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 10 SDK is required. Install it and rerun this script."
}
if (-not (Test-Path $trustedPath) -or -not (Test-Path $keygenProject) -or -not (Test-Path $publishScript)) {
    throw "Run this script from the MG-Business-Platform source repository."
}
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if ($privatePath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The private signing key must be stored outside the repository."
}
if ([System.IO.Path]::GetFullPath($privatePath) -eq [System.IO.Path]::GetFullPath($publicPath)) {
    throw "Private and public key paths must differ."
}
if ((Test-Path $privatePath) -or (Test-Path $publicPath)) {
    throw "Refusing to overwrite existing key files. Back up and select new paths deliberately."
}

New-Item -ItemType Directory -Path (Split-Path -Parent $privatePath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $publicPath) -Force | Out-Null

Write-Host "Generating a new publisher key pair locally. The private key will not be copied into the repository."
& dotnet run --project $keygenProject --configuration Release -- keygen $privatePath $publicPath
if ($LASTEXITCODE -ne 0) {
    throw "Publisher key generation failed with exit code $LASTEXITCODE."
}

$principal = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $privatePath /inheritance:r /grant:r "${principal}:(F)" "SYSTEM:(F)" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not restrict private-key file permissions. Secure the key file ACL manually before continuing."
}

$previousPublicKey = [System.IO.File]::ReadAllBytes($trustedPath)
try {
    [System.IO.File]::Copy($publicPath, $trustedPath, $true)
    Write-Host "Running the full release pipeline with the newly embedded public key."
    & $publishScript
    if ($LASTEXITCODE -ne 0) {
        throw "The release pipeline failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "Publisher key bootstrap completed."
    Write-Host "Private signing key (keep offline and never share): $privatePath"
    Write-Host "Public verification key: $publicPath"
    Write-Host "New installers and source packages are in the artifacts folder."
    Write-Host "Only distribute the newly built application; existing releases trust the previous public key."
}
catch {
    [System.IO.File]::WriteAllBytes($trustedPath, $previousPublicKey)
    throw
}
