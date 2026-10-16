param(
    [Parameter(Mandatory = $true)][string]$AppDirectory,
    [Parameter(Mandatory = $true)][string]$DataDirectory
)

$ErrorActionPreference = "Stop"
$logPath = Join-Path $DataDirectory "uninstall-host-cleanup.log"

function Write-CleanupLog([string]$Message) {
    try {
        New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
        Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) $Message" -Encoding UTF8
    } catch {
        # Uninstall must not fail because diagnostic logging is unavailable.
    }
}

function Stop-OwnedProcess([string]$PidFile, [string[]]$ExpectedExecutables, [string]$Label) {
    if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) {
        return
    }

    $rawPid = [string](Get-Content -LiteralPath $PidFile -Raw -ErrorAction SilentlyContinue)
    $rawPid = $rawPid.Trim()
    $processId = 0
    if (-not [int]::TryParse($rawPid, [ref]$processId) -or $processId -le 4) {
        Write-CleanupLog "$Label PID file was invalid; removing stale file."
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    try {
        $process = Get-Process -Id $processId -ErrorAction Stop
    } catch {
        Write-CleanupLog "$Label process is already stopped; removing stale PID file."
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    $actualPath = $null
    try { $actualPath = $process.Path } catch { }
    if ([string]::IsNullOrWhiteSpace($actualPath)) {
        Write-CleanupLog "$Label process path could not be verified; refusing to stop PID $processId."
        return
    }

    $expected = @($ExpectedExecutables | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
    $actual = [System.IO.Path]::GetFullPath($actualPath)
    if (-not ($expected | Where-Object { [string]::Equals($_, $actual, [System.StringComparison]::OrdinalIgnoreCase) })) {
        Write-CleanupLog "$Label PID $processId belongs to a different executable; refusing to stop it and clearing the stale PID file."
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    try {
        Stop-Process -Id $processId -Force -ErrorAction Stop
        $process.WaitForExit(5000) | Out-Null
        Write-CleanupLog "$Label process $processId was stopped."
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
    } catch {
        Write-CleanupLog "$Label process $processId could not be stopped: $($_.Exception.Message)"
    } finally {
        $process.Dispose()
    }
}

try {
    $app = [System.IO.Path]::GetFullPath($AppDirectory)
    $data = [System.IO.Path]::GetFullPath($DataDirectory)
    // Stop the public TLS endpoint first so no new customer requests arrive,
    // then stop the loopback-only storefront process.
    Stop-OwnedProcess (Join-Path $data "caddy.pid") @(
        (Join-Path $app "caddy/caddy.exe"),
        (Join-Path $app "caddy.exe")
    ) "Caddy HTTPS proxy"
    Stop-OwnedProcess (Join-Path $data "storefront.pid") @(
        (Join-Path $app "storefront/MG.BusinessPlatform.Storefront.exe"),
        (Join-Path $app "MG.BusinessPlatform.Storefront.exe")
    ) "Storefront"
} catch {
    Write-CleanupLog "Cleanup error: $($_.Exception.Message)"
}
