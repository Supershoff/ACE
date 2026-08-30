#Requires -Version 7.0
<#
.SYNOPSIS
    Starts the disposable, local-only Phase 5 inventory acceptance stack (issue #34): an isolated
    MariaDB container, Cloud schema migrations, ACE Auth Bridge, ACE.Cloud.Backend, ACE.Cloud.Worker,
    and the web client behind a same-origin local proxy. Connects to a separately started ACE test
    world through acceptance.settings.json -- it never starts, bootstraps, or modifies ACE itself.

.DESCRIPTION
    Run Test-Prerequisites.ps1 first (this script calls it automatically and stops on failure with an
    actionable diagnostic -- it never silently skips a step). See README.md for the full runbook.
#>

[CmdletBinding()]
param(
    # Skips `npm run build`, reusing the previous build in Source/ACE.Cloud.Web/dist.
    [switch]$SkipWebBuild
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$runStateDir = Join-Path $scriptRoot ".local-run"
$pidFile = Join-Path $runStateDir "processes.json"

& (Join-Path $scriptRoot "Test-Prerequisites.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "Aborting: prerequisites failed. Fix the problems above and re-run." -ForegroundColor Red
    exit 1
}

$settings = Get-Content (Join-Path $scriptRoot "acceptance.settings.json") -Raw | ConvertFrom-Json

New-Item -ItemType Directory -Path $runStateDir -Force | Out-Null
if (Test-Path $pidFile) {
    Write-Host "A previous run's process record was found. Run Stop-LocalAcceptance.ps1 first if that run is still active." -ForegroundColor Yellow
}

$cloudConnectionString = "Server=127.0.0.1;Port=$($settings.dbPort);Database=ace_cloud;User Id=$($settings.dbUser);Password=$($settings.dbPassword);"
$webUiOrigin = "http://127.0.0.1:$($settings.webUiPort)"
$backendOrigin = "http://127.0.0.1:$($settings.backendPort)"
$authBridgeOrigin = "http://127.0.0.1:$($settings.authBridgePort)"

Write-Host "Starting the disposable acceptance MariaDB container..." -ForegroundColor Cyan
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

Write-Host "Applying Cloud schema migrations..." -ForegroundColor Cyan
$env:ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING = $cloudConnectionString
dotnet run --project (Join-Path $repoRoot "Source/ACE.Cloud.LocalAcceptanceMigrator") --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Aborting: Cloud schema migrations failed." -ForegroundColor Red
    exit 1
}

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
}

Write-Host "Starting ACE.Cloud.Worker..." -ForegroundColor Cyan
$processRecords += Start-BackgroundDotnetProject -ProjectPath (Join-Path $repoRoot "Source/ACE.Cloud.Worker") -Name "Worker" -EnvironmentOverrides @{
    ASPNETCORE_URLS = "http://127.0.0.1:$($settings.workerHealthPort)"
    "CloudWorker__CloudConnectionString" = $cloudConnectionString
    "CloudWorker__WorldBoundaryHealthEndpoint" = $settings.worldBoundaryHealthEndpoint
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
