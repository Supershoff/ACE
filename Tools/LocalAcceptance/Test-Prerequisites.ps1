#Requires -Version 7.0
<#
.SYNOPSIS
    Validates local-machine prerequisites for the disposable Phase 5 acceptance stack (issue #34) and
    reports every problem it finds -- it never silently skips a check. This covers tools, ports, and
    acceptance.settings.json's shape only; it deliberately does NOT check the ACE test world's
    liveness (see Test-AceWorldReadiness.ps1) because that check must run AFTER ace_cloud and its
    CloudShardBinding are prepared (Prepare-LocalAcceptanceCloudDatabase.ps1), not before -- a
    Cloud-enabled ACE process cannot become meaningfully ready until ace_cloud exists.

.DESCRIPTION
    Run directly for a quick diagnosis, or let Prepare-LocalAcceptanceCloudDatabase.ps1 /
    Start-LocalAcceptance.ps1 call it automatically.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$problems = New-Object System.Collections.Generic.List[string]

function Test-CommandAvailable {
    param([string]$Name, [string]$Hint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        $problems.Add("`"$Name`" was not found on PATH. $Hint")
        return $false
    }
    return $true
}

function Test-PortFree {
    param([int]$Port, [string]$UsedFor)
    $inUse = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($inUse) {
        $problems.Add("Port $Port (needed for $UsedFor) is already in use. Free it or change the port in acceptance.settings.json.")
    }
}

Write-Host "Checking local acceptance prerequisites..." -ForegroundColor Cyan

if (Test-CommandAvailable -Name "dotnet" -Hint "Install the .NET 10 SDK from https://dotnet.microsoft.com/download.") {
    $sdkList = dotnet --list-sdks 2>$null
    if (-not ($sdkList -match "^10\.")) {
        $problems.Add("No .NET 10 SDK was found (`"dotnet --list-sdks`" showed: $($sdkList -join '; ')). Install the .NET 10 SDK.")
    }
}

if (Test-CommandAvailable -Name "node" -Hint "Install Node.js 20+ from https://nodejs.org/.") {
    $nodeVersion = (node --version) -replace "^v", ""
    $majorVersion = [int]($nodeVersion.Split(".")[0])
    if ($majorVersion -lt 20) {
        $problems.Add("Node.js $nodeVersion was found, but Source/ACE.Cloud.Web requires Node.js 20 or newer.")
    }
}

Test-CommandAvailable -Name "npm" -Hint "npm ships with Node.js -- reinstall Node.js if it is missing." | Out-Null

$settingsPath = Join-Path $scriptRoot "acceptance.settings.json"
$examplePath = Join-Path $scriptRoot "acceptance.settings.example.json"
$settings = $null
if (-not (Test-Path $settingsPath)) {
    $problems.Add("acceptance.settings.json is missing. Copy $examplePath to $settingsPath and fill in your disposable, local-only values.")
} else {
    try {
        $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    } catch {
        $problems.Add("acceptance.settings.json is not valid JSON: $($_.Exception.Message)")
    }

    if ($settings) {
        $requiredFields = @(
            "webUiPort", "backendPort", "workerHealthPort", "authBridgePort",
            "dbUser", "dbPassword", "worldBoundaryHealthEndpoint",
            "aceAuthConnectionString", "aceShardConnectionString", "aceWorldConnectionString",
            "shardId", "activeServiceKeyId", "activeServiceKeySecret",
            "cloudAceExtensionVersion", "cloudContractProtocolVersion"
        )
        foreach ($field in $requiredFields) {
            $value = $settings.$field
            if ([string]::IsNullOrWhiteSpace([string]$value) -or ([string]$value).StartsWith("CHANGE_ME")) {
                $problems.Add("acceptance.settings.json is missing a real value for `"$field`".")
            }
        }
        foreach ($accountField in @("mainAccountName", "mainAccountPassword", "linkedAccountName", "linkedAccountPassword")) {
            $value = $settings.testAccounts.$accountField
            if ([string]::IsNullOrWhiteSpace([string]$value) -or ([string]$value).StartsWith("CHANGE_ME")) {
                $problems.Add("acceptance.settings.json is missing a real value for testAccounts.$accountField.")
            }
        }
        if ([string]$settings.dbUser -eq 'root') {
            $problems.Add('acceptance.settings.json dbUser must be a non-root runtime identity (for example, cloud_acceptance).')
        }
        # aceServerProjectPath is optional (blank means "you manage your own ACE process"); no check needed.
    }
}

if ($settings) {
    Test-PortFree -Port $settings.webUiPort -UsedFor "the web client's local proxy"
    Test-PortFree -Port $settings.backendPort -UsedFor "ACE.Cloud.Backend"
}

if ($problems.Count -eq 0) {
    Write-Host "All local prerequisites are satisfied." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "Local acceptance prerequisites are not satisfied:" -ForegroundColor Red
foreach ($problem in $problems) {
    Write-Host "  - $problem" -ForegroundColor Red
}
Write-Host ""
Write-Host "See Tools/LocalAcceptance/README.md for the full setup runbook." -ForegroundColor Yellow
exit 1
