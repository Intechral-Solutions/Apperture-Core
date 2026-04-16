<#
.SYNOPSIS
    Rebuilds and restarts only the Management UI (local dotnet by default).
.DESCRIPTION
    Default (matches scripts\dev\up.ps1): builds Apperture.Management.Ui.Server with the pinned SDK and dotnet,
    stops whatever is listening on port 9001, then starts the UI again. Does not stop port 9000 (API) or the
    worker host process. The API and worker are also local dotnet in dev, not Docker.

    Use -Docker when the UI is a compose service (e.g. full stack deploy). The repo root compose file used for
    local dev typically only includes sqlserver; in that case Docker UI mode is not available unless you add a
    service and pass -ComposeService.

    Set APPERTURE_UI_COMPOSE_SERVICE to override the default compose service name (Docker mode only).
.PARAMETER ComposeService
    Compose service name for the Management UI image (Docker mode only).
.PARAMETER NoCache
    Passes --no-cache to `docker compose build` (Docker mode only).
.PARAMETER Docker
    Rebuild/recreate the UI via docker compose instead of local dotnet run.
.PARAMETER NoBrowser
    Bind http://localhost:9001 without opening a browser (dotnet mode only).
.EXAMPLE
    .\restart-ui.ps1
.EXAMPLE
    .\restart-ui.ps1 -NoBrowser
.EXAMPLE
    .\restart-ui.ps1 -Docker -ComposeService management-ui
#>
[CmdletBinding()]
param(
    [string]$ComposeService = $(if ($env:APPERTURE_UI_COMPOSE_SERVICE) { $env:APPERTURE_UI_COMPOSE_SERVICE } else { "management-ui" }),
    [switch]$NoCache,
    [switch]$Docker,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$UiServerProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Ui.Server\Apperture.Management.Ui.Server.csproj"
$ResolveScript = Join-Path $PSScriptRoot "Get-DevDotNetSdk.ps1"
$UiPort = 9001

function Get-NetTCPConnectionListen {
    param([int]$Port)
    @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" })
}

function Stop-ProcessByPort {
    param([int]$Port)
    $conns = @(Get-NetTCPConnectionListen -Port $Port)
    if ($conns.Count -eq 0) { return }
    $pids = $conns | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($owningProcessId in $pids) {
        $proc = Get-Process -Id $owningProcessId -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "Stopping process on port $Port (PID $owningProcessId): $($proc.ProcessName)" -ForegroundColor Yellow
            Stop-Process -Id $owningProcessId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Stop-DotNetUiByProject {
    param([string]$ProjectPath)
    if (-not (Test-Path $ProjectPath)) { return }
    $projectFileName = [System.IO.Path]::GetFileName($ProjectPath)
    $uiProcs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq "dotnet.exe" -and $_.CommandLine -and $_.CommandLine -like "*$projectFileName*"
        }
    foreach ($p in $uiProcs) {
        Write-Host "Stopping UI dotnet host: $projectFileName (PID $($p.ProcessId))" -ForegroundColor Yellow
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Wait-DevListenPortFree {
    param([int]$Port = 9001, [int]$TimeoutSeconds = 45)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $listeners = @(Get-NetTCPConnectionListen -Port $Port)
        if ($listeners.Count -eq 0) { return }
        Start-Sleep -Milliseconds 350
    }
    throw "Timeout (${TimeoutSeconds}s) waiting for port $Port to be free."
}

if (-not $Docker) {
    if (-not (Test-Path $UiServerProject)) {
        Write-Error "Management UI Server project not found: $UiServerProject"
    }

    $sdk = & $ResolveScript -RepoRoot $RepoRoot
    $dotnetExe = $sdk.DotNetExe
    $env:DOTNET_ROOT = $sdk.DotNetRoot
    if ($env:PATH -notlike "*$($sdk.DotNetRoot)*") {
        $env:PATH = "$($sdk.DotNetRoot);$env:PATH"
    }

    Write-Host "Building Management UI (dotnet)..." -ForegroundColor Cyan
    & $dotnetExe build $UiServerProject --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    Write-Host "Stopping existing UI (port $UiPort + dotnet processes for this project)..." -ForegroundColor Cyan
    Stop-ProcessByPort -Port $UiPort
    Stop-DotNetUiByProject -ProjectPath $UiServerProject
    Write-Host "Waiting for port $UiPort to be released..." -ForegroundColor Cyan
    Wait-DevListenPortFree -Port $UiPort

    $uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server")
    if ($NoBrowser) {
        $uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server", "--urls", "http://localhost:$UiPort")
    }

    # Match scripts\dev\up.ps1: new console window for dotnet (not inline in the calling shell).
    Start-Process -FilePath $dotnetExe -ArgumentList $uiArgs -WorkingDirectory $RepoRoot -WindowStyle Normal
    Write-Host "UI started in a new window at http://localhost:$UiPort" -ForegroundColor Green
    return
}

# Docker: rebuild only the UI service and recreate that container; do not start/stop other services.
Push-Location $RepoRoot
try {
    $definedServices = @(& docker compose config --services)
    if ($LASTEXITCODE -ne 0) { throw "docker compose config --services failed" }
    if ($ComposeService -notin $definedServices) {
        $known = ($definedServices | Sort-Object) -join ", "
        throw "Compose service '$ComposeService' is not defined. Known services: $known. Use -ComposeService <name>, or omit -Docker for local Management.Ui.Server on port $UiPort."
    }
}
finally {
    Pop-Location
}

Write-Host "Rebuilding compose service '$ComposeService' (no-deps)..." -ForegroundColor Cyan
Push-Location $RepoRoot
try {
    $buildArgs = @("compose", "build")
    if ($NoCache) { $buildArgs += "--no-cache" }
    $buildArgs += $ComposeService

    & docker @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "docker compose build failed for service '$ComposeService'" }

    $upArgs = @("compose", "up", "-d", "--no-deps", "--force-recreate", $ComposeService)
    & docker @upArgs
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed for service '$ComposeService'" }
}
finally {
    Pop-Location
}

Write-Host "UI container '$ComposeService' rebuilt and restarted." -ForegroundColor Green
