#Requires -Version 7.0
<#
.SYNOPSIS
    Stops the disposable local acceptance stack started by Start-LocalAcceptance.ps1.

.DESCRIPTION
    Always stops the background AuthBridge/Backend/Worker/proxy processes it started. With -Purge,
    also removes only the disposable ace_cloud schema and its known ace_shard boundary triggers from
    the disposable test world's co-located database server.
#>

[CmdletBinding()]
param(
    [switch]$Purge
)

$scriptRoot = $PSScriptRoot
$runStateDir = Join-Path $scriptRoot ".local-run"
$pidFile = Join-Path $runStateDir "processes.json"

if (Test-Path $pidFile) {
    $processRecords = Get-Content $pidFile -Raw | ConvertFrom-Json
    foreach ($record in $processRecords) {
        $existing = Get-Process -Id $record.Pid -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Host "Stopping $($record.Name) (pid $($record.Pid))..." -ForegroundColor Cyan
            Stop-Process -Id $record.Pid -Force -ErrorAction SilentlyContinue
        } else {
            Write-Host "$($record.Name) (pid $($record.Pid)) was already stopped." -ForegroundColor DarkGray
        }
    }
    Remove-Item $pidFile -Force
} else {
    Write-Host "No process record found at $pidFile -- nothing to stop (or Start-LocalAcceptance.ps1 was never run)." -ForegroundColor Yellow
}

if ($Purge) {
    $settings = Get-Content (Join-Path $scriptRoot "acceptance.settings.json") -Raw | ConvertFrom-Json
    Write-Host "Removing the disposable ace_cloud schema and its known ace_shard boundary triggers..." -ForegroundColor Cyan
    $env:ACE_CLOUD_ACCEPTANCE_ADMIN_CONNECTION_STRING = $settings.aceShardConnectionString
    try {
        dotnet run --project (Join-Path $scriptRoot "../../Source/ACE.Cloud.LocalAcceptanceMigrator") --configuration Release -- purge-colocated | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "The disposable ace_cloud purge failed."
        }
    } finally {
        Remove-Item Env:ACE_CLOUD_ACCEPTANCE_ADMIN_CONNECTION_STRING -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "The disposable ace_cloud schema was left in place so re-running Start-LocalAcceptance.ps1 is fast. Pass -Purge to remove it and its cross-schema boundary triggers." -ForegroundColor Yellow
}

Write-Host "Stopped." -ForegroundColor Green
