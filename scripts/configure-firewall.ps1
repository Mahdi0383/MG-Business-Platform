param(
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ruleName = "MG Business Platform Web (TCP 80,443)"

function Get-ExactApplicationRules {
    $rules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
    foreach ($rule in $rules) {
        $portFilter = $rule | Get-NetFirewallPortFilter
        $ports = @($portFilter.LocalPort) -join ","
        $protocol = [string]$portFilter.Protocol
        $isOwnedShape = $rule.Direction -eq "Inbound"
            -and $rule.Action -eq "Allow"
            -and $protocol -eq "TCP"
            -and $ports -eq "80,443"
        if ($isOwnedShape) {
            $rule
        }
    }
}

if ($Remove) {
    $allRules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
    $exactRules = @(Get-ExactApplicationRules)
    foreach ($rule in $exactRules) {
        Remove-NetFirewallRule -InputObject $rule
    }

    $leftoverCount = $allRules.Count - $exactRules.Count
    if ($leftoverCount -gt 0) {
        throw "Some same-named firewall rules did not match the application's exact inbound TCP 80,443 rule and were left untouched."
    }

    Write-Output ("Removed {0} MG Business Platform firewall rule(s)." -f $exactRules.Count)
    exit 0
}

$existingRules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
if ($existingRules.Count -gt 0) {
    $exactRules = @(Get-ExactApplicationRules)
    if ($exactRules.Count -ne $existingRules.Count) {
        throw "A firewall rule already uses the application's display name but has different settings. Rename or review it manually; no rule was changed."
    }

    foreach ($rule in $exactRules) {
        Set-NetFirewallRule -InputObject $rule -Enabled True -Profile Any | Out-Null
    }
    Write-Output "The existing MG Business Platform inbound TCP 80,443 rule is enabled."
    exit 0
}

New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80,443 -Profile Any | Out-Null
Write-Output "Created the MG Business Platform inbound TCP 80,443 firewall rule."
