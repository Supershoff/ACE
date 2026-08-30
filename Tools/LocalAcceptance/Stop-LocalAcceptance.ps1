#Requires -Version 7.0
<#
.SYNOPSIS
    Stops the disposable local acceptance stack started by Start-LocalAcceptance.ps1.

.DESCRIPTION
    Always stops the background AuthBridge/Backend/Worker/proxy processes it started. With -Purge,
    also removes the disposable acceptance MariaDB container and its named volume -- scoped strictly
    to the `ace-cloud-acceptance` Compose project, never touching an existing ACE installation's own
    `docker-compose.yml`/`ace-db`/`db-data`.
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
    Write-Host "Removing the disposable acceptance MariaDB container and its volume..." -ForegroundColor Cyan
    $composeFile = Join-Path $scriptRoot "docker-compose.acceptance.yml"
    docker compose -p ace-cloud-acceptance -f $composeFile down --volumes
} else {
    Write-Host "The acceptance MariaDB container was left running so re-running Start-LocalAcceptance.ps1 is fast. Pass -Purge to remove it and its data." -ForegroundColor Yellow
}

Write-Host "Stopped." -ForegroundColor Green
