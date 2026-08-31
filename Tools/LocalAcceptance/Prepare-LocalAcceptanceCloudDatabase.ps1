#Requires -Version 7.0
<#
.SYNOPSIS
    Phase 1 ("prepare") of the disposable Phase 5 acceptance stack (issue #34): starts the isolated
    ace_cloud MariaDB container, applies Cloud schema migrations, and idempotently bootstraps the
    mandatory singleton CloudShardBinding row -- all BEFORE your Cloud-enabled ACE test world needs to
    become live. Also validates (read-only) that your already-existing ace_auth/ace_shard/ace_world
    databases are reachable, without ever creating, migrating, or purging them.

.DESCRIPTION
    Run this first. Once it succeeds, (re)start your separately managed ACE test world with
    CloudMule.Enabled = true and MySql.Cloud pointing at the ace_cloud database this script just
    prepared (or set acceptance.settings.json's aceServerProjectPath to have Start-LocalAcceptance.ps1
    manage that restart for you), then run Start-LocalAcceptance.ps1 (phase 2, "continue") to wait for
    ACE's liveness endpoint, validate deposit readiness, and start the rest of the stack.

    This never touches an existing, non-disposable ACE installation or its databases: only the
    ace-cloud-acceptance Compose project's own container/volume is created here.
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

Write-Host "Starting the disposable acceptance MariaDB container (ace_cloud only)..." -ForegroundColor Cyan
$composeFile = Join-Path $scriptRoot "docker-compose.acceptance.yml"
$env:ACE_CLOUD_ACCEPTANCE_DB_ROOT_PASSWORD = $settings.dbRootPassword
$env:ACE_CLOUD_ACCEPTANCE_DB_USER = $settings.dbUser
$env:ACE_CLOUD_ACCEPTANCE_DB_PASSWORD = $settings.dbPassword
$env:ACE_CLOUD_ACCEPTANCE_DB_PORT = $settings.dbPort
docker compose -p ace-cloud-acceptance -f $composeFile up -d --wait
if ($LASTEXITCODE -ne 0) {
    Write-Host "Aborting: the acceptance MariaDB container did not become healthy." -ForegroundColor Red
    exit 1
}

$cloudConnectionString = "Server=127.0.0.1;Port=$($settings.dbPort);Database=ace_cloud;User Id=$($settings.dbUser);Password=$($settings.dbPassword);"

Write-Host "Applying Cloud schema migrations and bootstrapping the CloudShardBinding row..." -ForegroundColor Cyan
$migratorExitCode = Invoke-Migrator -MigratorArgs @("migrate-and-bootstrap") -EnvironmentOverrides @{
    ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING          = $cloudConnectionString
    ACE_CLOUD_ACCEPTANCE_SHARD_ID                   = $settings.shardId
    ACE_CLOUD_ACCEPTANCE_ACE_EXTENSION_VERSION      = $settings.cloudAceExtensionVersion
    ACE_CLOUD_ACCEPTANCE_CONTRACT_PROTOCOL_VERSION  = $settings.cloudContractProtocolVersion
}
if ($migratorExitCode -ne 0) {
    Write-Host "Aborting: Cloud schema migration or CloudShardBinding bootstrap failed. See the output above -- a mismatched existing CloudShardBinding row is never overwritten." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "ace_cloud is prepared." -ForegroundColor Green
Write-Host ""
if ([string]::IsNullOrWhiteSpace($settings.aceServerProjectPath)) {
    Write-Host "Next: (re)start your separately managed ACE test world with CloudMule.Enabled = true, CloudMule.ShardId = `"$($settings.shardId)`", and MySql.Cloud pointing at this ace_cloud database (127.0.0.1:$($settings.dbPort), user $($settings.dbUser)). Then run Start-LocalAcceptance.ps1." -ForegroundColor Yellow
} else {
    Write-Host "Next: run Start-LocalAcceptance.ps1 -- acceptance.settings.json's aceServerProjectPath is set, so it will (re)start ACE.Server for you." -ForegroundColor Yellow
}
