$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "MG.BusinessPlatform.sln"
$artifactRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactRoot "publish"
$desktopOutput = Join-Path $publishRoot "desktop"
$storefrontOutput = Join-Path $desktopOutput "storefront"
$licenseToolOutput = Join-Path $publishRoot "license-tool"
$licenseManagerOutput = Join-Path $publishRoot "license-manager-gui"
$zipPath = Join-Path $artifactRoot "MG-Business-Platform-win-x64.zip"
$licenseToolZipPath = Join-Path $artifactRoot "MG-License-Manager-win-x64.zip"
$mainSourceZipPath = Join-Path $artifactRoot "MG-Business-Platform-SOURCE.zip"
$licenseSourceZipPath = Join-Path $artifactRoot "MG-License-Manager-SOURCE.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 10 is required. Install the .NET 10 SDK and rerun this script."
}
if (-not (Test-Path $solution)) {
    throw "Solution file not found: $solution"
}

Write-Host "== Restore ==" -ForegroundColor Cyan
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }

Write-Host "== Release build ==" -ForegroundColor Cyan
dotnet build $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }

Write-Host "== Automated tests ==" -ForegroundColor Cyan
dotnet test $solution --configuration Release --no-build --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "Automated tests failed with exit code $LASTEXITCODE." }

if (Test-Path $publishRoot) { Remove-Item $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Path $desktopOutput -Force | Out-Null

Write-Host "== Publish Windows desktop ==" -ForegroundColor Cyan
$desktopProject = Join-Path $repoRoot "src/MG.BusinessPlatform.Desktop/MG.BusinessPlatform.Desktop.csproj"
dotnet publish $desktopProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $desktopOutput
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish customer storefront ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $storefrontOutput -Force | Out-Null
$storefrontProject = Join-Path $repoRoot "src/MG.BusinessPlatform.Storefront/MG.BusinessPlatform.Storefront.csproj"
dotnet publish $storefrontProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $storefrontOutput
if ($LASTEXITCODE -ne 0) { throw "Storefront publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish separate license manager ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $licenseToolOutput -Force | Out-Null
$licenseToolProject = Join-Path $repoRoot "src/MG.BusinessPlatform.LicenseTool/MG.BusinessPlatform.LicenseTool.csproj"
dotnet publish $licenseToolProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $licenseToolOutput
if ($LASTEXITCODE -ne 0) { throw "License manager CLI publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish graphical license manager ==" -ForegroundColor Cyan
$licenseManagerProject = Join-Path $repoRoot "src/MG.BusinessPlatform.LicenseManager/MG.BusinessPlatform.LicenseManager.csproj"
New-Item -ItemType Directory -Path $licenseManagerOutput -Force | Out-Null
dotnet publish $licenseManagerProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $licenseManagerOutput
if ($LASTEXITCODE -ne 0) { throw "Graphical license manager publish failed with exit code $LASTEXITCODE." }
if (-not (Test-Path (Join-Path $licenseManagerOutput "MG.BusinessPlatform.LicenseManager.exe"))) { throw "Graphical license manager publish did not produce MG.BusinessPlatform.LicenseManager.exe." }
Copy-Item (Join-Path $licenseManagerOutput "*") $licenseToolOutput -Recurse -Force
Copy-Item (Join-Path $repoRoot "docs/LICENSE_MANAGER.md") (Join-Path $licenseToolOutput "LICENSE_MANAGER.md") -Force

if (-not (Test-Path (Join-Path $desktopOutput "MG.BusinessPlatform.Desktop.exe"))) {
    throw "Desktop executable was not produced."
}
if (-not (Test-Path (Join-Path $storefrontOutput "MG.BusinessPlatform.Storefront.exe"))) {
    throw "Storefront executable was not produced."
}
if (-not (Test-Path (Join-Path $licenseToolOutput "MG.BusinessPlatform.LicenseTool.exe"))) {
    throw "Separate license manager CLI executable was not produced."
}
if (-not (Test-Path (Join-Path $licenseToolOutput "MG.BusinessPlatform.LicenseManager.exe"))) {
    throw "Graphical license manager executable was not produced."
}

Copy-Item (Join-Path $PSScriptRoot "configure-firewall.ps1") (Join-Path $desktopOutput "configure-firewall.ps1") -Force

Write-Host "== Package Caddy HTTPS reverse proxy ==" -ForegroundColor Cyan
$caddyDirectory = Join-Path $desktopOutput "caddy"
New-Item -ItemType Directory -Path $caddyDirectory -Force | Out-Null
$caddyPath = Join-Path $caddyDirectory "caddy.exe"
Copy-Item (Join-Path $repoRoot "LICENSES/Caddy-Apache-2.0.txt") (Join-Path $caddyDirectory "LICENSE.txt") -Force
$caddyUri = "https://caddyserver.com/api/download?os=windows&arch=amd64"
$caddyDownloaded = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
        if (Test-Path $caddyPath) { Remove-Item $caddyPath -Force }
        Invoke-WebRequest -Uri $caddyUri -OutFile $caddyPath -UseBasicParsing -TimeoutSec 90
        if ((Test-Path $caddyPath) -and (Get-Item $caddyPath).Length -ge 1MB) {
            $caddyDownloaded = $true
            break
        }
        throw "Downloaded Caddy file is missing or smaller than 1 MB."
    } catch {
        if ($attempt -eq 3) {
            throw "Caddy download failed after $attempt attempts: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 5
    }
}
if (-not $caddyDownloaded) {
    throw "Caddy download failed; refusing to package a build without the HTTPS reverse proxy."
}
$caddyVersion = & $caddyPath version
if ($LASTEXITCODE -ne 0) { throw "Downloaded Caddy binary failed its version check." }
Set-Content -Path (Join-Path $caddyDirectory "VERSION.txt") -Value $caddyVersion -Encoding UTF8
Write-Host "Bundled Caddy: $caddyVersion"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $desktopOutput "*") -DestinationPath $zipPath -CompressionLevel Optimal

$sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Set-Content -Path "${zipPath}.sha256" -Value "$sha  $(Split-Path $zipPath -Leaf)" -Encoding ASCII

if (Test-Path $licenseToolZipPath) { Remove-Item $licenseToolZipPath -Force }
Compress-Archive -Path (Join-Path $licenseToolOutput "*") -DestinationPath $licenseToolZipPath -CompressionLevel Optimal
$licenseToolSha = (Get-FileHash $licenseToolZipPath -Algorithm SHA256).Hash
Set-Content -Path "${licenseToolZipPath}.sha256" -Value "$licenseToolSha  $(Split-Path $licenseToolZipPath -Leaf)" -Encoding ASCII

Write-Host "== Package source archives ==" -ForegroundColor Cyan
$mainSourceItems = @(
    "src", "tests", "docs", "scripts", "installer", "LICENSES",
    "MG.BusinessPlatform.sln", "MG-License-Manager.sln", "global.json",
    "README.md", "CONTRIBUTING.md", "THIRD_PARTY_NOTICES.md"
) | Where-Object { Test-Path (Join-Path $repoRoot $_) }
$licenseSourceItems = @(
    "src/MG.BusinessPlatform.Licensing",
    "src/MG.BusinessPlatform.LicenseTool",
    "src/MG.BusinessPlatform.LicenseManager",
    "MG-License-Manager.sln",
    "global.json",
    "docs/LICENSE_MANAGER.md",
    "installer/MG-License-Manager.iss",
    "LICENSES",
    "THIRD_PARTY_NOTICES.md"
) | Where-Object { Test-Path (Join-Path $repoRoot $_) }

Push-Location $repoRoot
try {
    if (Test-Path $mainSourceZipPath) { Remove-Item $mainSourceZipPath -Force }
    Compress-Archive -Path $mainSourceItems -DestinationPath $mainSourceZipPath -CompressionLevel Optimal
    if (Test-Path $licenseSourceZipPath) { Remove-Item $licenseSourceZipPath -Force }
    Compress-Archive -Path $licenseSourceItems -DestinationPath $licenseSourceZipPath -CompressionLevel Optimal
} finally {
    Pop-Location
}

$mainSourceSha = (Get-FileHash $mainSourceZipPath -Algorithm SHA256).Hash
Set-Content -Path "${mainSourceZipPath}.sha256" -Value "$mainSourceSha  $(Split-Path $mainSourceZipPath -Leaf)" -Encoding ASCII
$licenseSourceSha = (Get-FileHash $licenseSourceZipPath -Algorithm SHA256).Hash
Set-Content -Path "${licenseSourceZipPath}.sha256" -Value "$licenseSourceSha  $(Split-Path $licenseSourceZipPath -Leaf)" -Encoding ASCII

Write-Host "Build, tests and Windows publishing completed." -ForegroundColor Green
Write-Host "Package: $zipPath"
Write-Host "SHA-256: $sha"
Write-Host "License manager package: $licenseToolZipPath"
Write-Host "License manager SHA-256: $licenseToolSha"
Write-Host "Main source archive: $mainSourceZipPath (SHA-256: $mainSourceSha)"
Write-Host "License manager source archive: $licenseSourceZipPath (SHA-256: $licenseSourceSha)"

$isccCandidates = @(
    @(
        (Join-Path $env:ProgramFiles "Inno Setup 6/ISCC.exe"),
        (Join-Path ([Environment]::GetEnvironmentVariable("ProgramFiles(x86)")) "Inno Setup 6/ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path $_) }
)
if ($isccCandidates.Count -gt 0) {
    New-Item -ItemType Directory -Path (Join-Path $artifactRoot "installer") -Force | Out-Null
    $iscc = $isccCandidates[0]
    $installerScript = Join-Path $repoRoot "installer/MG-Business-Platform.iss"
    & $iscc $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Main application installer packaging failed with exit code $LASTEXITCODE." }
    $licenseInstallerScript = Join-Path $repoRoot "installer/MG-License-Manager.iss"
    & $iscc $licenseInstallerScript
    if ($LASTEXITCODE -ne 0) { throw "License manager installer packaging failed with exit code $LASTEXITCODE." }
    Write-Host "Main application and separate license manager installers completed." -ForegroundColor Green
} else {
    Write-Warning "Inno Setup is not installed. ZIP package is ready; installer EXE was not produced."
}
