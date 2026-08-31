#Requires -Version 7.0
<#
.SYNOPSIS
    Stages and activates an operator-supplied client_portal.dat for the disposable local acceptance
    stack (issue #34: "least-resistance local-acceptance/operator path to stage and activate
    client_portal.dat"), so deposited items get real reconstructed icons instead of the neutral
    fallback glyph.

.DESCRIPTION
    Drives ACE.Cloud.LocalAcceptanceMigrator's `activate-portal-dat` mode, which reuses the existing
    CloudAssetImportBoundary chunked-upload/finalize API end to end: it uploads your local DAT into
    this stack's own disposable protected storage (Start-LocalAcceptance.ps1's
    .local-run/protected-assets, never a permanent or shared location), waits for the already-running
    ACE.Cloud.Worker process's CloudAssetImportStagingWorker to extract it, and then activates the
    resulting manifest so the icon composition worker can start composing icons from it.

    Requires Start-LocalAcceptance.ps1 to already be running (ACE.Cloud.Worker must be up to actually
    process the staging step). Supports both a stock and a modified ACE-compatible client_portal.dat --
    this script never inspects or validates its contents beyond the checksum the upload protocol
    itself already verifies.

    Never uploads, commits, or logs the DAT itself, nor any absolute path beyond what you pass on your
    own command line; only synthetic/disposable local resources are touched.

.PARAMETER ClientPortalDatPath
    Path to your own local client_portal.dat (or a modified ACE-compatible equivalent). Never
    hard-code a personal path here -- always pass your own via this parameter.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ClientPortalDatPath
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$runStateDir = Join-Path $scriptRoot ".local-run"
$protectedAssetStorageRoot = Join-Path $runStateDir "protected-assets"

if (-not (Test-Path $ClientPortalDatPath -PathType Leaf)) {
    Write-Host "No file found at '$ClientPortalDatPath'." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $protectedAssetStorageRoot -PathType Container)) {
    Write-Host "$protectedAssetStorageRoot does not exist yet. Run Start-LocalAcceptance.ps1 first (it must still be running -- ACE.Cloud.Worker performs the actual DAT extraction)." -ForegroundColor Red
    exit 1
}

$settings = Get-Content (Join-Path $scriptRoot "acceptance.settings.json") -Raw | ConvertFrom-Json

$cloudConnectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$cloudConnectionBuilder.set_ConnectionString([string]$settings.aceShardConnectionString)
foreach ($key in @($cloudConnectionBuilder.Keys)) {
    if ([string]$key -match '^(Database|Initial Catalog|User Id|UserID|UID|Username|Password|Pwd)$') {
        $cloudConnectionBuilder.Remove([string]$key) | Out-Null
    }
}
$cloudConnectionBuilder['Database'] = 'ace_cloud'
$cloudConnectionBuilder['User Id'] = $settings.dbUser
$cloudConnectionBuilder['Password'] = $settings.dbPassword
$cloudConnectionString = $cloudConnectionBuilder.ConnectionString

$env:ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING = $cloudConnectionString
$env:ACE_CLOUD_ACCEPTANCE_SHARD_ID = [string]$settings.shardId
$env:ACE_CLOUD_ACCEPTANCE_ASSET_STORAGE_ROOT = $protectedAssetStorageRoot

try {
    Push-Location (Join-Path $repoRoot "Source/ACE.Cloud.LocalAcceptanceMigrator")
    dotnet run --no-launch-profile -- activate-portal-dat $ClientPortalDatPath
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
    Remove-Item Env:ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING, Env:ACE_CLOUD_ACCEPTANCE_SHARD_ID, Env:ACE_CLOUD_ACCEPTANCE_ASSET_STORAGE_ROOT -ErrorAction SilentlyContinue
}

if ($exitCode -ne 0) {
    Write-Host "Failed to activate '$ClientPortalDatPath'. See the output above." -ForegroundColor Red
    exit $exitCode
}

Write-Host "client_portal.dat is staged and active. The icon composition worker will begin composing icons for already-deposited items on its next poll." -ForegroundColor Green
