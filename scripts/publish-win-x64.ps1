$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "MG.BusinessPlatform.sln"
$artifactRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactRoot "publish"
$desktopOutput = Join-Path $publishRoot "desktop"
$storefrontOutput = Join-Path $desktopOutput "storefront"
$zipPath = Join-Path $artifactRoot "MG-Business-Platform-win-x64.zip"

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
dotnet publish $desktopProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $desktopOutput
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed with exit code $LASTEXITCODE." }

Write-Host "== Publish customer storefront ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $storefrontOutput -Force | Out-Null
$storefrontProject = Join-Path $repoRoot "src/MG.BusinessPlatform.Storefront/MG.BusinessPlatform.Storefront.csproj"
dotnet publish $storefrontProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $storefrontOutput
if ($LASTEXITCODE -ne 0) { throw "Storefront publish failed with exit code $LASTEXITCODE." }

if (-not (Test-Path (Join-Path $desktopOutput "MG.BusinessPlatform.Desktop.exe"))) {
    throw "Desktop executable was not produced."
}
if (-not (Test-Path (Join-Path $storefrontOutput "MG.BusinessPlatform.Storefront.exe"))) {
    throw "Storefront executable was not produced."
}

Copy-Item (Join-Path $PSScriptRoot "configure-firewall.ps1") (Join-Path $desktopOutput "configure-firewall.ps1") -Force

Write-Host "== Package Caddy HTTPS reverse proxy ==" -ForegroundColor Cyan
$caddyDirectory = Join-Path $desktopOutput "caddy"
New-Item -ItemType Directory -Path $caddyDirectory -Force | Out-Null
$caddyPath = Join-Path $caddyDirectory "caddy.exe"
$caddyUri = "https://caddyserver.com/api/download?os=windows&arch=amd64"
Invoke-WebRequest -Uri $caddyUri -OutFile $caddyPath -UseBasicParsing
if (-not (Test-Path $caddyPath) -or (Get-Item $caddyPath).Length -lt 1MB) {
    throw "Caddy download failed or returned an invalid file."
}
$caddyVersion = & $caddyPath version
if ($LASTEXITCODE -ne 0) { throw "Downloaded Caddy binary failed its version check." }
Set-Content -Path (Join-Path $caddyDirectory "VERSION.txt") -Value $caddyVersion -Encoding UTF8
Write-Host "Bundled Caddy: $caddyVersion"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $desktopOutput "*") -DestinationPath $zipPath -CompressionLevel Optimal

$sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Set-Content -Path "${zipPath}.sha256" -Value "$sha  $(Split-Path $zipPath -Leaf)" -Encoding ASCII

Write-Host "Build, tests and Windows publishing completed." -ForegroundColor Green
Write-Host "Package: $zipPath"
Write-Host "SHA-256: $sha"

$isccCandidates = @(
    (Join-Path $env:ProgramFiles "Inno Setup 6/ISCC.exe"),
    (Join-Path ([Environment]::GetEnvironmentVariable("ProgramFiles(x86)")) "Inno Setup 6/ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) }
if ($isccCandidates.Count -gt 0) {
    New-Item -ItemType Directory -Path (Join-Path $artifactRoot "installer") -Force | Out-Null
    $iscc = $isccCandidates[0]
    $installerScript = Join-Path $repoRoot "installer/MG-Business-Platform.iss"
    & $iscc $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup packaging failed with exit code $LASTEXITCODE." }
    Write-Host "Installer packaging completed." -ForegroundColor Green
} else {
    Write-Warning "Inno Setup is not installed. ZIP package is ready; installer EXE was not produced."
}
