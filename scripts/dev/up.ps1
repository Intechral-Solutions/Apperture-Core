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

function Wait-DevTcpConnectable {
    <#
    .SYNOPSIS
        Blocks until something accepts TCP connections (Docker SQL Server readiness from the host).
    #>
    param(
        [string]$ComputerName = "127.0.0.1",
        [int]$Port = 1433,
        [int]$TimeoutSeconds = 120
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = New-Object System.Net.Sockets.TcpClient
            try {
                $client.Connect($ComputerName, $Port)
            }
            finally {
                $client.Close()
            }
            return
        }
        catch {
            Start-Sleep -Milliseconds 400
        }
    }
    throw "Timeout (${TimeoutSeconds}s) waiting for TCP ${ComputerName}:$Port (is SQL Server container running?)."
}

function Wait-DevListenPortsFree {
    <#
    .SYNOPSIS
        Blocks until no process is listening on the given local ports (avoids Kestrel bind races after Stop-Process).
    #>
    param(
        [int[]]$Ports = @(9000, 9001),
        [int]$TimeoutSeconds = 45
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $busy = $false
        foreach ($p in $Ports) {
            $listeners = @(Get-NetTCPConnection -LocalPort $p -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" })
            if ($listeners.Count -gt 0) {
                $busy = $true
                break
            }
        }
        if (-not $busy) {
            return
        }
        Start-Sleep -Milliseconds 350
    }
    throw "Timeout (${TimeoutSeconds}s) waiting for ports to be free: $($Ports -join ', ')."
}
$HostProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj"
$UiServerProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Ui.Server\Apperture.Management.Ui.Server.csproj"
$WorkerHostProject = Join-Path $RepoRoot "src\Apperture.RuntimeHost\Apperture.RuntimeHost.Windows\Apperture.RuntimeHost.Windows.WorkerHost\Apperture.RuntimeHost.Windows.WorkerHost.csproj"
$StartWorkerScript = Join-Path $PSScriptRoot "start-worker.ps1"
$SyncScript = Join-Path $PSScriptRoot "Sync-EnvFromUserSecrets.ps1"
$LogDir = Join-Path $RepoRoot "artifacts\devlogs"

if (-not (Test-Path $HostProject)) {
    Write-Error "Management.Host project not found: $HostProject"
}
if (-not (Test-Path $UiServerProject)) {
    Write-Error "Management UI Server project not found: $UiServerProject"
}
if (-not (Test-Path $WorkerHostProject)) {
    Write-Error "Runtime WorkerHost project not found: $WorkerHostProject"
}
if (-not (Test-Path $StartWorkerScript)) {
    Write-Error "Runtime worker start script not found: $StartWorkerScript"
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

Write-Host "Waiting for SQL Server to accept TCP connections (localhost:1433)..." -ForegroundColor Cyan
Wait-DevTcpConnectable -Port 1433
Write-Host "  SQL Server: localhost:1433" -ForegroundColor Gray
Write-Host "  API:        http://localhost:9000" -ForegroundColor Gray
Write-Host "  UI:         http://localhost:9001" -ForegroundColor Gray
Write-Host ""

# A still-running Management.Host locks bin\Debug outputs; the next `dotnet run` then fails with MSB3027
# and the new console window exits immediately. Clear listeners from any prior dev session first.
$DownScript = Join-Path $PSScriptRoot "down.ps1"
Write-Host "Stopping any prior dev API / UI / worker (ports 9000, 9001)..." -ForegroundColor Cyan
& $DownScript
Write-Host "Waiting for ports 9000/9001 to be released..." -ForegroundColor Cyan
Wait-DevListenPortsFree -Ports @(9000, 9001)
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

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

function Start-DevServiceLogged {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $safeName = $Name.ToLowerInvariant() -replace "[^a-z0-9]+", "-"
    $stdout = Join-Path $LogDir "$safeName-$stamp.out.log"
    $stderr = Join-Path $LogDir "$safeName-$stamp.err.log"

    Write-Host "Starting $Name..." -ForegroundColor Cyan
    Write-Host "  stdout: $stdout" -ForegroundColor DarkGray
    Write-Host "  stderr: $stderr" -ForegroundColor DarkGray

    $process = Start-Process -FilePath $dotnetExe -ArgumentList $Arguments -WorkingDirectory $RepoRoot -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

    [pscustomobject]@{
        Name = $Name
        Process = $process
        StdOut = $stdout
        StdErr = $stderr
    }
}

function Wait-DevServiceStarted {
    param(
        [Parameter(Mandatory)]$Launch,
        [Parameter(Mandatory)][int]$Port,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Launch.Process.HasExited) {
            throw "$($Launch.Name) exited during startup (exit code: $($Launch.Process.ExitCode)). See logs: $($Launch.StdOut) ; $($Launch.StdErr)"
        }

        $listeners = @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" })
        if ($listeners.Count -gt 0) {
            Write-Host "  $($Launch.Name): listening on http://localhost:$Port" -ForegroundColor Gray
            return
        }

        Start-Sleep -Milliseconds 500
    }

    throw "$($Launch.Name) did not listen on port $Port within ${TimeoutSeconds}s. See logs: $($Launch.StdOut) ; $($Launch.StdErr)"
}

# Build serially, then run with --no-build. Concurrent dotnet run builds can race on shared project outputs
# and make the first full dev startup flaky.
Write-Host "Building Management.Host..." -ForegroundColor Cyan
& $dotnetExe build $HostProject --no-incremental
if ($LASTEXITCODE -ne 0) { throw "Management.Host build failed" }

Write-Host "Building Management UI..." -ForegroundColor Cyan
& $dotnetExe build $UiServerProject --no-incremental
if ($LASTEXITCODE -ne 0) { throw "Management UI build failed" }

$hostArgs = @("run", "--no-build", "--project", $HostProject, "--launch-profile", "http")
$uiArgs = @("run", "--no-build", "--project", $UiServerProject, "--launch-profile", "Ui.Server")
if ($NoBrowser) {
    $uiArgs = @("run", "--no-build", "--project", $UiServerProject, "--launch-profile", "Ui.Server", "--urls", "http://localhost:9001")
}

# Use the resolved dotnet.exe (full path). Start-Process "dotnet" can pick Program Files\dotnet and fail on net10.0
# even when this session's PATH was prepended with the correct SDK root.
$hostLaunch = Start-DevServiceLogged -Name "Management Host" -Arguments $hostArgs
Wait-DevServiceStarted -Launch $hostLaunch -Port 9000
$uiLaunch = Start-DevServiceLogged -Name "Management UI" -Arguments $uiArgs
Wait-DevServiceStarted -Launch $uiLaunch -Port 9001

# Start instance-plane worker so workflow runs actually execute steps and persist step instances.
# Use dedicated script that records startup status and failure detail.
Write-Host "Starting testing worker runtime..." -ForegroundColor Cyan
& $StartWorkerScript
if (-not $?) { throw "Failed to start testing worker runtime." }

Write-Host "Dev servers started. Run scripts\dev\down.ps1 to stop them." -ForegroundColor Green
