<#
.SYNOPSIS
    Rebuilds and restarts only the Management UI, without restarting the API or worker.
.DESCRIPTION
    Docker mode (default): runs `docker compose build` and `docker compose up -d --no-deps`
    for a single compose service so dependent services (API, worker, SQL) are not recreated.

    Use -Dotnet when you run the UI with `dotnet run` (as in scripts\dev\up.ps1): builds
    Apperture.Management.Ui.Server, stops whatever is listening on port 9001, then starts
    the UI again. Does not stop port 9000 (API) or the worker host process.

    Set APPERTURE_UI_COMPOSE_SERVICE to override the default compose service name.
.PARAMETER ComposeService
    Compose service name for the Management UI image (Docker mode only).
.PARAMETER NoCache
    Passes --no-cache to `docker compose build` (Docker mode only).
.PARAMETER Dotnet
    Use dotnet build/run instead of Docker (local dev matching up.ps1).
.PARAMETER NoBrowser
    Dotnet mode only: bind http://localhost:9001 without opening a browser.
.EXAMPLE
    .\restart-ui.ps1
.EXAMPLE
    .\restart-ui.ps1 -ComposeService ui
.EXAMPLE
    .\restart-ui.ps1 -Dotnet
.EXAMPLE
    .\restart-ui.ps1 -Dotnet -NoBrowser
#>
[CmdletBinding()]
param(
    [string]$ComposeService = $(if ($env:APPERTURE_UI_COMPOSE_SERVICE) { $env:APPERTURE_UI_COMPOSE_SERVICE } else { "management-ui" }),
    [switch]$NoCache,
    [switch]$Dotnet,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$UiServerProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Ui.Server\Apperture.Management.Ui.Server.csproj"
$UiPort = 9001

function Stop-ProcessByPort {
    param([int]$Port)
    $conn = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" }
    if ($conn) {
        $owningProcessId = $conn.OwningProcess
        $proc = Get-Process -Id $owningProcessId -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "Stopping process on port $Port (PID $owningProcessId): $($proc.ProcessName)" -ForegroundColor Yellow
            Stop-Process -Id $owningProcessId -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Dotnet) {
    if (-not (Test-Path $UiServerProject)) {
        Write-Error "Management UI Server project not found: $UiServerProject"
    }

    Write-Host "Building Management UI (dotnet)..." -ForegroundColor Cyan
    & dotnet build $UiServerProject --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    Write-Host "Restarting UI on port $UiPort..." -ForegroundColor Cyan
    Stop-ProcessByPort -Port $UiPort

    $uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server")
    if ($NoBrowser) {
        $uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server", "--urls", "http://localhost:$UiPort")
    }

    Start-Process -FilePath "dotnet" -ArgumentList $uiArgs -WorkingDirectory $RepoRoot -WindowStyle Normal
    Write-Host "UI restarted at http://localhost:$UiPort" -ForegroundColor Green
    return
}

# Docker: rebuild only the UI service and recreate that container; do not start/stop other services.
Push-Location $RepoRoot
try {
    $definedServices = @(& docker compose config --services)
    if ($LASTEXITCODE -ne 0) { throw "docker compose config --services failed" }
    if ($ComposeService -notin $definedServices) {
        $known = ($definedServices | Sort-Object) -join ", "
        throw "Compose service '$ComposeService' is not defined. Known services: $known. Use -ComposeService <name> or run with -Dotnet for local Management.Ui.Server on port $UiPort."
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
