$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ruleName = "MG Business Platform Web (TCP 80,443)"
$rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($null -eq $rule) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80,443 -Profile Any | Out-Null
} else {
    Set-NetFirewallRule -DisplayName $ruleName -Enabled True -Direction Inbound -Action Allow -Profile Any
}
