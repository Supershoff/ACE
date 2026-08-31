#Requires -Version 7.0
<#
.SYNOPSIS
    Phase 1 ("prepare") of the disposable Phase 5 acceptance stack (issue #34): creates ace_cloud
    beside the disposable test world's ace_shard schema, applies Cloud schema migrations, and idempotently bootstraps the
    mandatory singleton CloudShardBinding row -- all BEFORE your Cloud-enabled ACE test world needs to
    become live. Also validates (read-only) that your already-existing ace_auth/ace_shard/ace_world
    databases are reachable, without ever creating, migrating, or purging them.

.DESCRIPTION
    Run this first. Once it succeeds, (re)start your separately managed ACE test world with
    CloudMule.Enabled = true and MySql.Cloud pointing at the ace_cloud database this script just
    prepared (or set acceptance.settings.json's aceServerProjectPath to have Start-LocalAcceptance.ps1
    manage that restart for you), then run Start-LocalAcceptance.ps1 (phase 2, "continue") to wait for
    ACE's liveness endpoint, validate deposit readiness, and start the rest of the stack.

    This must only target the disposable ACE test world: custody invariants use cross-schema triggers,
    so ace_cloud and ace_shard cannot be split across separate database servers.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")

& (Join-Path $scriptRoot "Test-Prerequisites.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "Aborting: prerequisites failed. Fix the problems above and re-run." -ForegroundColor Red
    exit 1
}

$settings = Get-Content (Join-Path $scriptRoot "acceptance.settings.json") -Raw | ConvertFrom-Json

function Invoke-Migrator {
    param([string[]]$MigratorArgs, [hashtable]$EnvironmentOverrides = @{})

    foreach ($key in $EnvironmentOverrides.Keys) {
        Set-Item -Path "Env:$key" -Value ([string]$EnvironmentOverrides[$key])
    }

    try {
        # Keep the child process's informational output visible without emitting it into this
        # function's success stream. Callers assign this function's result to $exitCode; allowing
        # dotnet's stdout into that assignment produces an array (messages plus the integer exit
        # code), which makes every successful connectivity probe compare as non-zero.
        dotnet run --project (Join-Path $repoRoot "Source/ACE.Cloud.LocalAcceptanceMigrator") --configuration Release -- @MigratorArgs | Out-Host
        $exitCode = $LASTEXITCODE
        return $exitCode
    } finally {
        foreach ($key in $EnvironmentOverrides.Keys) {
            Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue
        }
    }
}

function New-CloudRuntimeConnectionString {
    param([string]$ShardConnectionString, [string]$RuntimeUser, [string]$RuntimePassword)

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.ConnectionString = $ShardConnectionString
    foreach ($key in @($builder.Keys)) {
        if ([string]$key -match '^(Database|Initial Catalog|User Id|UserID|UID|Username|Password|Pwd)$') {
            $builder.Remove([string]$key) | Out-Null
        }
    }
    $builder['Database'] = 'ace_cloud'
    $builder['User Id'] = $RuntimeUser
    $builder['Password'] = $RuntimePassword
    return $builder.ConnectionString
}

Write-Host "Validating (read-only) connectivity to your existing ace_auth/ace_shard/ace_world databases..." -ForegroundColor Cyan
Write-Host "This never creates, migrates, or purges them -- only a disposable ace_cloud database is managed by this launcher." -ForegroundColor DarkGray

$externalChecks = @(
    @{ Label = "ace_auth"; ConnectionString = $settings.aceAuthConnectionString },
    @{ Label = "ace_shard"; ConnectionString = $settings.aceShardConnectionString },
    @{ Label = "ace_world"; ConnectionString = $settings.aceWorldConnectionString }
)

$externalProblems = @()
foreach ($check in $externalChecks) {
    $exitCode = Invoke-Migrator -MigratorArgs @("validate-external-connection", $check.Label, $check.ConnectionString)
    if ($exitCode -ne 0) {
        $externalProblems += $check.Label
    }
}

if ($externalProblems.Count -gt 0) {
    Write-Host "Aborting: could not reach $($externalProblems -join ', '). Confirm the connection strings in acceptance.settings.json and that those databases are already running -- this launcher never creates them." -ForegroundColor Red
    exit 1
}

$cloudConnectionString = New-CloudRuntimeConnectionString `
    -ShardConnectionString $settings.aceShardConnectionString `
    -RuntimeUser $settings.dbUser `
    -RuntimePassword $settings.dbPassword

Write-Host "Preparing co-located ace_cloud, applying migrations, and bootstrapping CloudShardBinding..." -ForegroundColor Cyan
$migratorExitCode = Invoke-Migrator -MigratorArgs @("prepare-colocated") -EnvironmentOverrides @{
    ACE_CLOUD_ACCEPTANCE_ADMIN_CONNECTION_STRING    = $settings.aceShardConnectionString
    ACE_CLOUD_ACCEPTANCE_RUNTIME_CONNECTION_STRING  = $cloudConnectionString
    ACE_CLOUD_ACCEPTANCE_SHARD_ID                   = $settings.shardId
    ACE_CLOUD_ACCEPTANCE_ACE_EXTENSION_VERSION      = $settings.cloudAceExtensionVersion
    ACE_CLOUD_ACCEPTANCE_CONTRACT_PROTOCOL_VERSION  = $settings.cloudContractProtocolVersion
}
if ($migratorExitCode -ne 0) {
    Write-Host "Aborting: co-located Cloud schema preparation or CloudShardBinding bootstrap failed. See the output above -- a mismatched existing CloudShardBinding row is never overwritten." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "ace_cloud is prepared." -ForegroundColor Green
Write-Host ""
if ([string]::IsNullOrWhiteSpace($settings.aceServerProjectPath)) {
    Write-Host "Next: (re)start your separately managed ACE test world with CloudMule.Enabled = true, CloudMule.ShardId = `"$($settings.shardId)`", and MySql.Cloud pointing at this co-located ace_cloud database using an ACE world-boundary identity that can also read/write ace_shard. Then run Start-LocalAcceptance.ps1." -ForegroundColor Yellow
} else {
    Write-Host "Next: run Start-LocalAcceptance.ps1 -- acceptance.settings.json's aceServerProjectPath is set, so it will (re)start ACE.Server for you." -ForegroundColor Yellow
}
