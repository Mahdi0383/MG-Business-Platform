$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

$bootstrapScriptPath = Join-Path $PSScriptRoot "bootstrap-publisher-key.ps1"
if (Test-Path $bootstrapScriptPath) {
    $parseTokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($bootstrapScriptPath, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) { throw "Publisher key bootstrap script has PowerShell syntax errors." }
}

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

$stopHostScript = Join-Path $PSScriptRoot "stop-host.ps1"
$parseTokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($stopHostScript, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) {
    throw "stop-host.ps1 contains PowerShell syntax errors: $($parseErrors -join '; ')"
}

$firewallScript = Join-Path $PSScriptRoot "configure-firewall.ps1"
$parseTokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($firewallScript, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) {
    throw "configure-firewall.ps1 contains PowerShell syntax errors: $($parseErrors -join '; ')"
}

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
dotnet publish $desktopProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $desktopOutput
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish customer storefront ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $storefrontOutput -Force | Out-Null
$storefrontProject = Join-Path $repoRoot "src/MG.BusinessPlatform.Storefront/MG.BusinessPlatform.Storefront.csproj"
dotnet publish $storefrontProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $storefrontOutput
if ($LASTEXITCODE -ne 0) { throw "Storefront publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish separate license manager ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $licenseToolOutput -Force | Out-Null
$licenseToolProject = Join-Path $repoRoot "src/MG.BusinessPlatform.LicenseTool/MG.BusinessPlatform.LicenseTool.csproj"
dotnet publish $licenseToolProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $licenseToolOutput
if ($LASTEXITCODE -ne 0) { throw "License manager CLI publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish graphical license manager ==" -ForegroundColor Cyan
$licenseManagerProject = Join-Path $repoRoot "src/MG.BusinessPlatform.LicenseManager/MG.BusinessPlatform.LicenseManager.csproj"
New-Item -ItemType Directory -Path $licenseManagerOutput -Force | Out-Null
dotnet publish $licenseManagerProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $licenseManagerOutput
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
Copy-Item (Join-Path $PSScriptRoot "stop-host.ps1") (Join-Path $desktopOutput "stop-host.ps1") -Force
if (-not (Test-Path (Join-Path $desktopOutput "stop-host.ps1"))) { throw "Background host cleanup script was not packaged." }

Write-Host "== Package Caddy HTTPS reverse proxy ==" -ForegroundColor Cyan
$caddyDirectory = Join-Path $desktopOutput "caddy"
New-Item -ItemType Directory -Path $caddyDirectory -Force | Out-Null
$caddyPath = Join-Path $caddyDirectory "caddy.exe"
Copy-Item (Join-Path $repoRoot "LICENSES/Caddy-Apache-2.0.txt") (Join-Path $caddyDirectory "LICENSE.txt") -Force

# Use the official immutable GitHub release asset, not the best-effort interactive
# download service. Pin both version and SHA-256 so the package is reproducible.
$caddyVersion = "2.11.9"
$caddyArchiveName = "caddy_$($caddyVersion)_windows_amd64.zip"
$caddySha256 = "ad93112d335ee687100c4c9f11bced16ca590377976b7fe7883882b9e797e472"
$caddyUri = "https://github.com/caddyserver/caddy/releases/download/v$($caddyVersion)/$($caddyArchiveName)"
$caddyArchivePath = Join-Path $env:TEMP $caddyArchiveName
$caddyExtractDirectory = Join-Path $env:TEMP "mg-caddy-$($caddyVersion)-$PID"
$caddyDownloaded = $false

try {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            if (Test-Path $caddyArchivePath) { Remove-Item $caddyArchivePath -Force }
            Invoke-WebRequest -Uri $caddyUri -OutFile $caddyArchivePath -UseBasicParsing -TimeoutSec 120
            if (-not (Test-Path $caddyArchivePath) -or (Get-Item $caddyArchivePath).Length -lt 1MB) {
                throw "Downloaded Caddy archive is missing or smaller than 1 MB."
            }

            $actualCaddySha256 = (Get-FileHash $caddyArchivePath -Algorithm SHA256).Hash
            if (-not [string]::Equals($actualCaddySha256, $caddySha256, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Official Caddy archive SHA-256 mismatch. Expected $caddySha256, received $actualCaddySha256."
            }

            if (Test-Path $caddyExtractDirectory) { Remove-Item $caddyExtractDirectory -Recurse -Force }
            Expand-Archive -LiteralPath $caddyArchivePath -DestinationPath $caddyExtractDirectory -Force
            $extractedCaddyPath = Join-Path $caddyExtractDirectory "caddy.exe"
            if (-not (Test-Path $extractedCaddyPath)) {
                throw "Verified Caddy archive does not contain caddy.exe at its expected root."
            }
            Copy-Item $extractedCaddyPath $caddyPath -Force
            $caddyDownloaded = $true
            break
        } catch {
            if ($attempt -eq 3) {
                throw "Caddy download or verification failed after $attempt attempts: $($_.Exception.Message)"
            }
            Start-Sleep -Seconds 5
        }
    }
} finally {
    if (Test-Path $caddyArchivePath) { Remove-Item $caddyArchivePath -Force }
    if (Test-Path $caddyExtractDirectory) { Remove-Item $caddyExtractDirectory -Recurse -Force }
}

if (-not $caddyDownloaded -or -not (Test-Path $caddyPath)) {
    throw "Caddy download failed; refusing to package a build without the HTTPS reverse proxy."
}
$caddyBinaryVersion = & $caddyPath version
if ($LASTEXITCODE -ne 0) { throw "Downloaded Caddy binary failed its version check." }
Set-Content -Path (Join-Path $caddyDirectory "VERSION.txt") -Value $caddyBinaryVersion -Encoding UTF8
Write-Host "Bundled Caddy: $caddyBinaryVersion (SHA-256 verified)"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $desktopOutput "*") -DestinationPath $zipPath -CompressionLevel Optimal

$sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Set-Content -Path "${zipPath}.sha256" -Value "$sha  $(Split-Path $zipPath -Leaf)" -Encoding ASCII

if (Test-Path $licenseToolZipPath) { Remove-Item $licenseToolZipPath -Force }
Compress-Archive -Path (Join-Path $licenseToolOutput "*") -DestinationPath $licenseToolZipPath -CompressionLevel Optimal
$licenseToolSha = (Get-FileHash $licenseToolZipPath -Algorithm SHA256).Hash
Set-Content -Path "${licenseToolZipPath}.sha256" -Value "$licenseToolSha  $(Split-Path $licenseToolZipPath -Leaf)" -Encoding ASCII

function Copy-SourceTree {
    param([string]$SourcePath, [string]$DestinationPath)

    $excludedDirectoryNames = @("bin", "obj", ".vs", ".git", "TestResults", "artifacts", "node_modules", "coverage")
    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $SourcePath -Force) {
        if ($entry.PSIsContainer) {
            if ($excludedDirectoryNames -contains $entry.Name) { continue }
            Copy-SourceTree -SourcePath $entry.FullName -DestinationPath (Join-Path $DestinationPath $entry.Name)
        } else {
            # Exclude signing secrets and local credentials from source packages.
            if (
                $entry.Name -match '^(?i:license-private.*|secrets\.json|license\.json|\.env(?:\..*)?)$' -or
                $entry.Extension -match '^\.(?i:pfx|p12|snk|key)$'
            ) { continue }
            if ($entry.Extension -ieq ".pem" -and $entry.Name -cne "TrustedLicensePublicKey.pem") { continue }
            Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $DestinationPath $entry.Name) -Force
        }
    }
}
function New-SourceArchive {
    param(
        [Parameter(Mandatory = $true)][string[]]$RelativePaths,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $stagingRoot = Join-Path $env:TEMP ("mg-source-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    try {
        foreach ($relativePath in $RelativePaths) {
            $sourcePath = Join-Path $repoRoot $relativePath
            if (-not (Test-Path $sourcePath)) { continue }

            $stagedPath = Join-Path $stagingRoot $relativePath
            if (Test-Path $sourcePath -PathType Container) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $stagedPath) -Force | Out-Null
                Copy-SourceTree -SourcePath $sourcePath -DestinationPath $stagedPath
            } else {
                New-Item -ItemType Directory -Path (Split-Path -Parent $stagedPath) -Force | Out-Null
                Copy-Item $sourcePath $stagedPath -Force
            }
        }

        if (Test-Path $DestinationPath) { Remove-Item $DestinationPath -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $stagingRoot,
            $DestinationPath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
    } finally {
        if (Test-Path $stagingRoot) { Remove-Item $stagingRoot -Recurse -Force }
    }
}

Write-Host "== Package source archives ==" -ForegroundColor Cyan
$mainSourceItems = @(
    "src", "tests", "docs", "scripts", "installer", "LICENSES", ".github", ".gitignore", ".editorconfig",
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

New-SourceArchive -RelativePaths $mainSourceItems -DestinationPath $mainSourceZipPath
New-SourceArchive -RelativePaths $licenseSourceItems -DestinationPath $licenseSourceZipPath

# Defense in depth: verify generated archives contain no private keys, credentials or local license files.
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($sourceArchivePath in @($mainSourceZipPath, $licenseSourceZipPath)) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($sourceArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $fileName = [System.IO.Path]::GetFileName($entry.FullName)
            if (
                $fileName -match '^(?i:license-private.*|secrets\.json|license\.json|\.env(?:\..*)?)$' -or
                $fileName -match '(?i)\.(pfx|p12|snk|key)$' -or
                ($fileName -match '(?i)\.pem$' -and $fileName -cne "TrustedLicensePublicKey.pem")
            ) {
                throw "Source archive contains a sensitive file: $($entry.FullName)"
            }
        }
    } finally {
        $archive.Dispose()
    }
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

    $mainInstallerPath = Join-Path $artifactRoot "installer/MG-Business-Platform-Setup-win-x64.exe"
    $licenseInstallerPath = Join-Path $artifactRoot "installer/MG-License-Manager-Setup-win-x64.exe"
    foreach ($installerPath in @($mainInstallerPath, $licenseInstallerPath)) {
        if (-not (Test-Path $installerPath)) { throw "Expected installer was not created: $installerPath" }
        $installerHash = (Get-FileHash $installerPath -Algorithm SHA256).Hash
        Set-Content -Path "$installerPath.sha256" -Value "$installerHash  $(Split-Path $installerPath -Leaf)" -Encoding ASCII
        Write-Host "Installer SHA-256 ($([System.IO.Path]::GetFileName($installerPath))): $installerHash"
    }
    Write-Host "Main application and separate license manager installers completed." -ForegroundColor Green
} else {
    Write-Warning "Inno Setup is not installed. ZIP package is ready; installer EXE was not produced."
}
