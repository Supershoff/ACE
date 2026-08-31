#Requires -Version 7.0
<#
.SYNOPSIS
    Phase 2 prerequisite: waits for the real ACE liveness endpoint (worldBoundaryHealthEndpoint) and
    then validates every remaining prerequisite for an actual Cloud Custodian deposit -- matching
    ShardId, a reachable and matching CloudShardBinding, a resolvable Vendor-type Custodian base
    weenie, and at least one resolved Custodian location -- via ACE's own read-only
    /cloudmule/deposit-readiness diagnostic (issue #34's blocking defect #5). Never silently skips a
    check; reports every problem it finds before Start-LocalAcceptance.ps1 launches the web stack.

.DESCRIPTION
    Must be run AFTER Prepare-LocalAcceptanceCloudDatabase.ps1 (ace_cloud and its CloudShardBinding
    must already exist) and after your Cloud-enabled ACE test world is actually running -- otherwise
    every check here fails with an actionable "ACE is not reachable" diagnostic rather than hanging.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [pscustomobject]$Settings,

    # How long to wait for the liveness endpoint before giving up (the operator may still be
    # restarting their ACE process with CloudMule enabled).
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

$healthUri = [Uri]$Settings.worldBoundaryHealthEndpoint
$readinessUri = "$($healthUri.Scheme)://$($healthUri.Authority)/cloudmule/deposit-readiness"

Write-Host "Waiting for ACE's world-boundary liveness endpoint ($($Settings.worldBoundaryHealthEndpoint))..." -ForegroundColor Cyan
$live = $false
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest -Uri $Settings.worldBoundaryHealthEndpoint -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            $live = $true
            break
        }
    } catch {
        # Expected while ACE is still (re)starting; retried until $TimeoutSeconds elapses.
    }
    Start-Sleep -Seconds 2
}

if (-not $live) {
    Write-Host ""
    Write-Host "ACE's world-boundary liveness endpoint did not respond within ${TimeoutSeconds}s." -ForegroundColor Red
    Write-Host "  - Confirm your ACE test world is running, built from this PR branch." -ForegroundColor Red
    Write-Host "  - Confirm CloudMule.Enabled and CloudMule.HealthEndpoint.Enabled are both true in its Config.js." -ForegroundColor Red
    Write-Host "  - Confirm CloudMule.HealthEndpoint.BindAddress/Port match worldBoundaryHealthEndpoint in acceptance.settings.json." -ForegroundColor Red
    exit 1
}

Write-Host "ACE is live. Checking Cloud Mule deposit readiness ($readinessUri)..." -ForegroundColor Cyan

try {
    $readiness = Invoke-RestMethod -Uri $readinessUri -TimeoutSec 5
} catch {
    Write-Host "Aborting: could not reach $readinessUri even though $($Settings.worldBoundaryHealthEndpoint) is live: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$problems = New-Object System.Collections.Generic.List[string]

if (-not $readiness.cloudMuleEnabled) {
    $problems.Add("CloudMule.Enabled is false in ACE's Config.js. Set it to true and restart ACE.")
} else {
    if ($readiness.shardBindingStatus -ne "Matches") {
        $problems.Add("Shard identity is not ready: $($readiness.shardBindingDetail)")
    }
    if (-not $readiness.custodianWeenieConfigured) {
        $problems.Add("CloudMule.CustodianBaseWeenieClassId is not configured in ACE's Config.js.")
    } elseif (-not $readiness.custodianWeenieFound) {
        $problems.Add("WeenieClassId $($readiness.custodianWeenieClassId) (CloudMule.CustodianBaseWeenieClassId) was not found in ace_world.")
    } elseif (-not $readiness.custodianWeenieIsVendorType) {
        $problems.Add("WeenieClassId $($readiness.custodianWeenieClassId) exists in ace_world but is not a Vendor-type weenie.")
    }
    if ($readiness.resolvedCustodianLocationCount -eq 0) {
        $problems.Add("No Custodian location resolved -- enable Marketplace or Mansions (or add a custom position) via the admin Custodian configuration.")
    }
}

if ($problems.Count -eq 0 -and $readiness.ready) {
    Write-Host "Cloud Mule deposit readiness: all checks passed." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "Cloud Mule is not ready for an actual deposit:" -ForegroundColor Red
foreach ($problem in $problems) {
    Write-Host "  - $problem" -ForegroundColor Red
}
if ($problems.Count -eq 0) {
    # readiness.ready was false for a reason this script's own checks above did not already catch.
    Write-Host "  - $($readiness.reason)" -ForegroundColor Red
}
exit 1
