#Requires -Version 7.0
<#
.SYNOPSIS
    Phase 2 ("continue") of the disposable, local-only Phase 5 inventory acceptance stack (issue #34):
    waits for your Cloud-enabled ACE test world to be live and deposit-ready, then starts ACE Auth
    Bridge, ACE.Cloud.Backend, ACE.Cloud.Worker, and the web client behind a same-origin local proxy.

.DESCRIPTION
    Runs Prepare-LocalAcceptanceCloudDatabase.ps1 first (idempotent -- safe to re-run): this prepares
    the disposable ace_cloud database and its CloudShardBinding BEFORE anything checks whether ACE
    itself is live, fixing the previous ordering where the prerequisite checker demanded an
    already-live ACE endpoint before ace_cloud (which a Cloud-enabled ACE process needs) could exist.

    If acceptance.settings.json's aceServerProjectPath is set, this script also starts/restarts that
    ACE.Server project as a managed background process (trusting its own Config.js is already
    configured with CloudMule.Enabled = true and MySql.Cloud pointing at the disposable ace_cloud
    database this script just prepared) -- it never edits that project's files. Leave
    aceServerProjectPath blank (the default) to manage your own separately started ACE test world
    instead; this script will then just wait for its liveness endpoint with clear instructions.

    Either way, this never starts, bootstraps, or modifies ACE itself beyond that one optional,
    explicitly opted-into managed-process restart, and never touches a non-disposable ACE installation
    or its ace_auth/ace_shard/ace_world databases.
#>

[CmdletBinding()]
param(
    # Skips `npm run build`, reusing the previous build in Source/ACE.Cloud.Web/dist.
    [switch]$SkipWebBuild,

    # Skips Prepare-LocalAcceptanceCloudDatabase.ps1 (use only if you already ran it this session and
    # know ace_cloud is still up -- otherwise ACE's own deposit-readiness checks will fail).
    [switch]$SkipPrepare,

    # How long to wait for ACE's liveness endpoint after (re)starting it.
    [int]$AceLivenessTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$runStateDir = Join-Path $scriptRoot ".local-run"
$pidFile = Join-Path $runStateDir "processes.json"

if (-not $SkipPrepare) {
    & (Join-Path $scriptRoot "Prepare-LocalAcceptanceCloudDatabase.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Aborting: preparing the disposable ace_cloud database failed." -ForegroundColor Red
        exit 1
    }
} else {
    & (Join-Path $scriptRoot "Test-Prerequisites.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Aborting: prerequisites failed. Fix the problems above and re-run." -ForegroundColor Red
        exit 1
    }
}

$settings = Get-Content (Join-Path $scriptRoot "acceptance.settings.json") -Raw | ConvertFrom-Json

New-Item -ItemType Directory -Path $runStateDir -Force | Out-Null
if (Test-Path $pidFile) {
    Write-Host "A previous run's process record was found. Run Stop-LocalAcceptance.ps1 first if that run is still active." -ForegroundColor Yellow
}

$cloudConnectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$cloudConnectionBuilder.ConnectionString = $settings.aceShardConnectionString
foreach ($key in @($cloudConnectionBuilder.Keys)) {
    if ([string]$key -match '^(Database|Initial Catalog|User Id|UserID|UID|Username|Password|Pwd)$') {
        $cloudConnectionBuilder.Remove([string]$key) | Out-Null
    }
}
$cloudConnectionBuilder['Database'] = 'ace_cloud'
$cloudConnectionBuilder['User Id'] = $settings.dbUser
$cloudConnectionBuilder['Password'] = $settings.dbPassword
$cloudConnectionString = $cloudConnectionBuilder.ConnectionString
$webUiOrigin = "http://127.0.0.1:$($settings.webUiPort)"
$backendOrigin = "http://127.0.0.1:$($settings.backendPort)"
$authBridgeOrigin = "http://127.0.0.1:$($settings.authBridgePort)"

$processRecords = @()
$logDir = Join-Path $runStateDir "logs"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

function Start-BackgroundDotnetProject {
    param([string]$ProjectPath, [string]$Name, [hashtable]$EnvironmentOverrides)

    foreach ($key in $EnvironmentOverrides.Keys) {
        Set-Item -Path "Env:$key" -Value ([string]$EnvironmentOverrides[$key])
    }

    $outLog = Join-Path $logDir "$Name.out.log"
    $errLog = Join-Path $logDir "$Name.err.log"
    $process = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $ProjectPath, "--configuration", "Release") `
        -PassThru -NoNewWindow -RedirectStandardOutput $outLog -RedirectStandardError $errLog

    foreach ($key in $EnvironmentOverrides.Keys) {
        Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue
    }

    Write-Host "Started $Name (pid $($process.Id)). Logs: $outLog / $errLog" -ForegroundColor DarkGray
    return [pscustomobject]@{ Name = $Name; Pid = $process.Id }
}

if (-not [string]::IsNullOrWhiteSpace($settings.aceServerProjectPath)) {
    Write-Host "Starting the managed ACE.Server process (aceServerProjectPath is set)..." -ForegroundColor Cyan
    Write-Host "This trusts $($settings.aceServerProjectPath)'s own Config.js is already configured with CloudMule.Enabled = true and MySql.Cloud pointing at ace_cloud -- it is not edited by this script." -ForegroundColor DarkGray
    $aceServerPath = Join-Path $repoRoot $settings.aceServerProjectPath
    $processRecords += Start-BackgroundDotnetProject -ProjectPath $aceServerPath -Name "AceServer" -EnvironmentOverrides @{}
} else {
    Write-Host "aceServerProjectPath is blank -- managing your own separately started ACE test world." -ForegroundColor Cyan
    Write-Host "If you have not already (re)started it with CloudMule.Enabled = true and MySql.Cloud pointing at the ace_cloud database Prepare-LocalAcceptanceCloudDatabase.ps1 just prepared, do that now." -ForegroundColor Yellow
}

& (Join-Path $scriptRoot "Test-AceWorldReadiness.ps1") -Settings $settings -TimeoutSeconds $AceLivenessTimeoutSeconds
if ($LASTEXITCODE -ne 0) {
    Write-Host "Aborting: ACE is not deposit-ready. Fix the problems above before starting the web stack." -ForegroundColor Red
    if ($processRecords.Count -gt 0) {
        $processRecords | ConvertTo-Json -AsArray | Set-Content -Path $pidFile
        Write-Host "The managed ACE.Server process is still running (pid recorded in $pidFile) -- run Stop-LocalAcceptance.ps1 to stop it." -ForegroundColor Yellow
    }
    exit 1
}

Write-Host "Starting the ACE Auth Bridge..." -ForegroundColor Cyan
$processRecords += Start-BackgroundDotnetProject -ProjectPath (Join-Path $repoRoot "Source/ACE.Cloud.AuthBridge") -Name "AuthBridge" -EnvironmentOverrides @{
    Urls = $authBridgeOrigin
    "AuthBridge__AceAuthConnectionString" = $settings.aceAuthConnectionString
    "AuthBridge__WorldBoundaryHealthEndpoint" = $settings.worldBoundaryHealthEndpoint
    "AuthBridge__ActiveServiceKeyId" = $settings.activeServiceKeyId
    "AuthBridge__ActiveServiceKeySecret" = $settings.activeServiceKeySecret
}

Write-Host "Starting ACE.Cloud.Backend..." -ForegroundColor Cyan
$processRecords += Start-BackgroundDotnetProject -ProjectPath (Join-Path $repoRoot "Source/ACE.Cloud.Backend") -Name "Backend" -EnvironmentOverrides @{
    ASPNETCORE_URLS = $backendOrigin
    "CloudBackend__CloudConnectionString" = $cloudConnectionString
    "CloudBackend__ShardId" = $settings.shardId
    "CloudBackend__AuthBridgeBaseAddress" = $authBridgeOrigin
    "CloudBackend__ActiveServiceKeyId" = $settings.activeServiceKeyId
    "CloudBackend__ActiveServiceKeySecret" = $settings.activeServiceKeySecret
    "CloudBackend__WorldBoundaryHealthEndpoint" = $settings.worldBoundaryHealthEndpoint
    "CloudBackend__AllowedOrigins__0" = $webUiOrigin
    "CloudBackend__ExpectedAceExtensionVersion" = $settings.cloudAceExtensionVersion
    "CloudBackend__ExpectedContractProtocolVersion" = $settings.cloudContractProtocolVersion
}

Write-Host "Starting ACE.Cloud.Worker..." -ForegroundColor Cyan
$processRecords += Start-BackgroundDotnetProject -ProjectPath (Join-Path $repoRoot "Source/ACE.Cloud.Worker") -Name "Worker" -EnvironmentOverrides @{
    ASPNETCORE_URLS = "http://127.0.0.1:$($settings.workerHealthPort)"
    "CloudWorker__CloudConnectionString" = $cloudConnectionString
    "CloudWorker__WorldBoundaryHealthEndpoint" = $settings.worldBoundaryHealthEndpoint
    "CloudWorker__ExpectedAceExtensionVersion" = $settings.cloudAceExtensionVersion
    "CloudWorker__ExpectedContractProtocolVersion" = $settings.cloudContractProtocolVersion
}

Write-Host "Waiting for the Backend health endpoint..." -ForegroundColor Cyan
$backendReady = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    try {
        $response = Invoke-WebRequest -Uri "$backendOrigin/health/ready" -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            $backendReady = $true
            break
        }
    } catch {
        # Expected while the process is still starting; retried until the loop's own limit.
    }
}
if (-not $backendReady) {
    Write-Host "Aborting: ACE.Cloud.Backend did not become ready at $backendOrigin/health/ready within 60s. Check logs in $runStateDir\logs." -ForegroundColor Red
    $processRecords | ConvertTo-Json -AsArray | Set-Content -Path $pidFile
    exit 1
}

if (-not $SkipWebBuild) {
    Write-Host "Building the web client..." -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot "Source/ACE.Cloud.Web")
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build failed."
        }
    } finally {
        Pop-Location
    }
}

Write-Host "Starting the same-origin local proxy..." -ForegroundColor Cyan
$env:ACE_CLOUD_ACCEPTANCE_WEB_UI_PORT = [string]$settings.webUiPort
$env:ACE_CLOUD_ACCEPTANCE_BACKEND_PORT = [string]$settings.backendPort
$proxyOutLog = Join-Path $logDir "SameOriginProxy.out.log"
$proxyErrLog = Join-Path $logDir "SameOriginProxy.err.log"
$proxyProcess = Start-Process -FilePath "node" -ArgumentList @((Join-Path $scriptRoot "same-origin-proxy.mjs")) `
    -PassThru -NoNewWindow -RedirectStandardOutput $proxyOutLog -RedirectStandardError $proxyErrLog
Remove-Item Env:ACE_CLOUD_ACCEPTANCE_WEB_UI_PORT, Env:ACE_CLOUD_ACCEPTANCE_BACKEND_PORT -ErrorAction SilentlyContinue
$processRecords += [pscustomobject]@{ Name = "SameOriginProxy"; Pid = $proxyProcess.Id }

Write-Host "Waiting for the same-origin proxy..." -ForegroundColor Cyan
$proxyReady = $false
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    try {
        $response = Invoke-WebRequest -Uri "$webUiOrigin/health/ready" -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            $proxyReady = $true
            break
        }
    } catch {
        # Expected while the proxy is still starting.
    }
}
if (-not $proxyReady) {
    Write-Host "Aborting: the same-origin proxy did not become ready at $webUiOrigin within 15s." -ForegroundColor Red
    $processRecords | ConvertTo-Json -AsArray | Set-Content -Path $pidFile
    exit 1
}

$processRecords | ConvertTo-Json -AsArray | Set-Content -Path $pidFile

Write-Host ""
Write-Host "Local acceptance stack is ready." -ForegroundColor Green
Write-Host "  Web UI:      $webUiOrigin" -ForegroundColor Green
Write-Host "  Backend:     $backendOrigin" -ForegroundColor DarkGray
Write-Host "  Auth Bridge: $authBridgeOrigin" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Run the human acceptance checklist in README.md, then Stop-LocalAcceptance.ps1 when done." -ForegroundColor Yellow
