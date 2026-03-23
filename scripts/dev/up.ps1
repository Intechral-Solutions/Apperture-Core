<#
.SYNOPSIS
    Starts the Apperture platform in development mode (API + UI + SQL Server).
.DESCRIPTION
    Syncs User Secrets to .env, starts SQL Server (Docker), then launches Management.Host
    (API with EF Core, port 9000) and Management UI Server (port 9001) in separate windows.
    Use down.ps1 to stop all dev processes.
.EXAMPLE
    .\up.ps1
    .\up.ps1 -NoBrowser
#>
[CmdletBinding()]
param(
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$HostProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj"
$UiServerProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Ui.Server\Apperture.Management.Ui.Server.csproj"
$WorkerHostProject = Join-Path $RepoRoot "src\Apperture.RuntimeHost\Apperture.RuntimeHost.Windows\Apperture.RuntimeHost.Windows.WorkerHost\Apperture.RuntimeHost.Windows.WorkerHost.csproj"
$SyncScript = Join-Path $PSScriptRoot "Sync-EnvFromUserSecrets.ps1"

if (-not (Test-Path $HostProject)) {
    Write-Error "Management.Host project not found: $HostProject"
}
if (-not (Test-Path $UiServerProject)) {
    Write-Error "Management UI Server project not found: $UiServerProject"
}
if (-not (Test-Path $WorkerHostProject)) {
    Write-Error "Runtime WorkerHost project not found: $WorkerHostProject"
}

# global.json pins .NET 10; PATH often only has older SDKs. Prefer user-local install (%USERPROFILE%\dotnet).
$ResolveScript = Join-Path $PSScriptRoot "Get-DevDotNetSdk.ps1"
$sdk = & $ResolveScript -RepoRoot $RepoRoot
$env:DOTNET_ROOT = $sdk.DotNetRoot
if ($env:PATH -notlike "*$($sdk.DotNetRoot)*") {
    $env:PATH = "$($sdk.DotNetRoot);$env:PATH"
}
$dotnetExe = $sdk.DotNetExe
$env:APPERTURE_DEV_DOTNET = $dotnetExe
Write-Host "Using .NET SDK $($sdk.Version) ($dotnetExe)" -ForegroundColor DarkGray
Write-Host ""

Write-Host "Starting Apperture dev environment (SQL-backed)..." -ForegroundColor Cyan
Write-Host ""

# Sync User Secrets -> .env for Docker
$LASTEXITCODE = 0
& $SyncScript
if (-not $?) { throw "Failed to sync User Secrets to .env" }

# Start SQL Server
Write-Host "Starting SQL Server (Docker)..." -ForegroundColor Cyan
Push-Location $RepoRoot
try {
    docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed" }
} finally {
    Pop-Location
}
Write-Host "  SQL Server: localhost:1433" -ForegroundColor Gray
Write-Host "  API:        http://localhost:9000" -ForegroundColor Gray
Write-Host "  UI:         http://localhost:9001" -ForegroundColor Gray
Write-Host ""

# A still-running Management.Host locks bin\Debug outputs; the next `dotnet run` then fails with MSB3027
# and the new console window exits immediately. Clear listeners from any prior dev session first.
$DownScript = Join-Path $PSScriptRoot "down.ps1"
Write-Host "Stopping any prior dev API / UI / worker (ports 9000, 9001)..." -ForegroundColor Cyan
& $DownScript
Write-Host ""

# Ensure the Apperture database exists before bootstrapping the host.
# (Bootstrap validates opening the configured database; migrations run after startup.)
$envPath = Join-Path $RepoRoot ".env"
if (-not (Test-Path $envPath)) { throw ".env not found at repo root: $envPath" }
$envLine = (Get-Content -Path $envPath | Select-String -Pattern "^MSSQL_SA_PASSWORD=").Line
if (-not $envLine) { throw "MSSQL_SA_PASSWORD not found in .env" }
$saPassword = $envLine.Split("=", 2)[1]

Write-Host "Ensuring database 'Apperture' exists..." -ForegroundColor Cyan
docker exec apperture-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$saPassword" -C -Q "IF DB_ID(N'Apperture') IS NULL CREATE DATABASE [Apperture];" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to create Apperture database via sqlcmd." }

$hostArgs = @("run", "--project", $HostProject, "--launch-profile", "http")
$uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server")
if ($NoBrowser) {
    # Keep the Ui.Server launch profile so static web assets are wired correctly.
    # Still run headless (no browser auto-open) by overriding only the URL.
    $uiArgs = @("run", "--project", $UiServerProject, "--launch-profile", "Ui.Server", "--urls", "http://localhost:9001")
}

# Use the resolved dotnet.exe (full path). Start-Process "dotnet" can pick Program Files\dotnet and fail on net10.0
# even when this session's PATH was prepended with the correct SDK root.
Start-Process -FilePath $dotnetExe -ArgumentList $hostArgs -WorkingDirectory $RepoRoot -WindowStyle Normal
Start-Sleep -Seconds 2
Start-Process -FilePath $dotnetExe -ArgumentList $uiArgs -WorkingDirectory $RepoRoot -WindowStyle Normal
Start-Sleep -Seconds 2

# Start instance-plane worker so workflow runs actually execute steps and persist step instances.
$workerArgs = @("run", "--project", $WorkerHostProject)
$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:SqlServer__Password = $saPassword
Start-Process -FilePath $dotnetExe -ArgumentList $workerArgs -WorkingDirectory $RepoRoot -WindowStyle Normal

Write-Host "Dev servers started. Close their windows or run scripts\dev\down.ps1 to stop." -ForegroundColor Green
