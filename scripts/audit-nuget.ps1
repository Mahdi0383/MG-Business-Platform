$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "MG.BusinessPlatform.sln"
if (-not (Test-Path $solution)) {
    throw "Solution file not found: $solution"
}

Write-Host "== Restore with NuGet auditing (all severities) ==" -ForegroundColor Cyan
dotnet restore $solution -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low
if ($LASTEXITCODE -ne 0) {
    throw "NuGet-audited restore failed with exit code $LASTEXITCODE."
}

$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
} else {
    $env:RUNNER_TEMP
}
$reportPath = Join-Path $tempRoot ("mg-nuget-audit-{0}.json" -f [Guid]::NewGuid().ToString("N"))

try {
    dotnet list $solution package --vulnerable --include-transitive --format json |
        Set-Content -Path $reportPath -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet list package vulnerability query failed with exit code $LASTEXITCODE."
    }

    $report = Get-Content -Path $reportPath -Raw | ConvertFrom-Json
    $findings = [System.Collections.Generic.List[string]]::new()

    function Find-Vulnerabilities($node, $path) {
        if ($null -eq $node) { return }

        if ($node -is [System.Collections.IDictionary]) {
            foreach ($key in $node.Keys) {
                $value = $node[$key]
                if ($key -eq "vulnerabilities" -and $null -ne $value -and @($value).Count -gt 0) {
                    $findings.Add("$path : $(@($value) | ConvertTo-Json -Compress -Depth 10)")
                } else {
                    Find-Vulnerabilities $value "$path/$key"
                }
            }
        } elseif ($node -is [System.Collections.IEnumerable] -and $node -isnot [string]) {
            $index = 0
            foreach ($item in $node) {
                Find-Vulnerabilities $item "$path[$index]"
                $index++
            }
        } elseif ($node -is [pscustomobject]) {
            foreach ($property in $node.PSObject.Properties) {
                if ($property.Name -eq "vulnerabilities" -and $null -ne $property.Value -and @($property.Value).Count -gt 0) {
                    $findings.Add("$path/$($property.Name) : $(@($property.Value) | ConvertTo-Json -Compress -Depth 10)")
                } else {
                    Find-Vulnerabilities $property.Value "$path/$($property.Name)"
                }
            }
        }
    }

    Find-Vulnerabilities $report "solution"
    if ($findings.Count -gt 0) {
        foreach ($finding in $findings) {
            Write-Host "::error::$finding"
        }
        throw "NuGet vulnerabilities were found in direct or transitive dependencies."
    }

    Write-Host "No NuGet vulnerabilities were reported for direct or transitive packages."
} finally {
    if (Test-Path $reportPath) {
        Remove-Item -Path $reportPath -Force
    }
}
