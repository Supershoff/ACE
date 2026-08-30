#Requires -Version 7.0
<#
.SYNOPSIS
    Validates local prerequisites for the disposable Phase 5 acceptance stack (issue #34) and reports
    every problem it finds -- it never silently skips a check. Run directly for a quick diagnosis, or
    let Start-LocalAcceptance.ps1 call it before doing anything else.
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

if (-not (Test-CommandAvailable -Name "docker" -Hint "Install Docker Desktop (with the WSL2 backend) from https://www.docker.com/products/docker-desktop/.")) {
} else {
    try {
        docker info *> $null
        if ($LASTEXITCODE -ne 0) {
            $problems.Add("Docker is installed but the daemon is not running. Start Docker Desktop and try again.")
        }
    } catch {
        $problems.Add("Could not reach the Docker daemon: $($_.Exception.Message)")
    }

    try {
        docker compose version *> $null
        if ($LASTEXITCODE -ne 0) {
            $problems.Add("`"docker compose`" (the Compose v2 plugin) is not available. Update Docker Desktop.")
        }
    } catch {
        $problems.Add("Could not run `"docker compose version`": $($_.Exception.Message)")
    }
}

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
            "webUiPort", "backendPort", "workerHealthPort", "authBridgePort", "dbPort",
            "dbRootPassword", "dbUser", "dbPassword", "worldBoundaryHealthEndpoint",
            "aceAuthConnectionString", "shardId", "activeServiceKeyId", "activeServiceKeySecret"
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
    }
}

if ($settings) {
    Test-PortFree -Port $settings.webUiPort -UsedFor "the web client's local proxy"
    Test-PortFree -Port $settings.backendPort -UsedFor "ACE.Cloud.Backend"
    Test-PortFree -Port $settings.dbPort -UsedFor "the disposable acceptance MariaDB container"

    Write-Host "Checking the separately started ACE test world's health endpoint ($($settings.worldBoundaryHealthEndpoint))..." -ForegroundColor Cyan
    try {
        $response = Invoke-WebRequest -Uri $settings.worldBoundaryHealthEndpoint -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -ne 200) {
            $problems.Add("The ACE test world's health endpoint returned HTTP $($response.StatusCode). Start your test world (built from this PR branch) before running the acceptance launcher.")
        }
    } catch {
        $problems.Add("Could not reach the ACE test world's health endpoint at $($settings.worldBoundaryHealthEndpoint): $($_.Exception.Message). Start a separate ACE test world built from this PR branch first -- this launcher does not start or bootstrap ACE itself.")
    }
}

if ($problems.Count -eq 0) {
    Write-Host "All prerequisites are satisfied." -ForegroundColor Green
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
