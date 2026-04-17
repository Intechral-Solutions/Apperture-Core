<#
.SYNOPSIS
    Rebuilds and restarts only the Management API (Management.Host) via local dotnet run.
.DESCRIPTION
    Development uses local .NET (pinned SDK via Get-DevDotNetSdk.ps1), not Docker, for the control-plane API.
    Stops listeners on port 9000 and any dotnet process running this Host project, waits for the port to be free,
    builds Apperture.Management.Host, then starts it in a new console (same launch profile as scripts\dev\up.ps1,
    http port 9000). Does not restart the UI, worker, or SQL container.
.EXAMPLE
    .\restart-host.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Dev-DotNetLaunch.ps1")
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$HostProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj"
$ResolveScript = Join-Path $PSScriptRoot "Get-DevDotNetSdk.ps1"
$ApiPort = 9000

if (-not (Test-Path $HostProject)) {
    Write-Error "Management.Host project not found: $HostProject"
}

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

function Stop-DotNetHostByProject {
    param([string]$ProjectPath)
    if (-not (Test-Path $ProjectPath)) { return }
    $projectFileName = [System.IO.Path]::GetFileName($ProjectPath)
    $hostProcs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq "dotnet.exe" -and $_.CommandLine -and $_.CommandLine -like "*$projectFileName*"
        }
    foreach ($p in $hostProcs) {
        Write-Host "Stopping API dotnet host: $projectFileName (PID $($p.ProcessId))" -ForegroundColor Yellow
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Wait-DevListenPortFree {
    param([int]$Port = 9000, [int]$TimeoutSeconds = 45)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $listeners = @(Get-NetTCPConnectionListen -Port $Port)
        if ($listeners.Count -eq 0) { return }
        Start-Sleep -Milliseconds 350
    }
    throw "Timeout (${TimeoutSeconds}s) waiting for port $Port to be free."
}

$sdk = & $ResolveScript -RepoRoot $RepoRoot
$dotnetExe = $sdk.DotNetExe
$env:DOTNET_ROOT = $sdk.DotNetRoot
if ($env:PATH -notlike "*$($sdk.DotNetRoot)*") {
    $env:PATH = "$($sdk.DotNetRoot);$env:PATH"
}

Write-Host "Stopping existing Management.Host (port $ApiPort + dotnet processes for this project)..." -ForegroundColor Cyan
Stop-ProcessByPort -Port $ApiPort
Stop-DotNetHostByProject -ProjectPath $HostProject
Write-Host "Waiting for port $ApiPort to be released..." -ForegroundColor Cyan
Wait-DevListenPortFree -Port $ApiPort

Write-Host "Building Management.Host..." -ForegroundColor Cyan
& $dotnetExe build $HostProject --no-incremental
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$hostArgs = @("run", "--project", $HostProject, "--launch-profile", "http")
Start-DevDotNetInNewTerminal -WindowTitle "Apperture Management API" -DotnetExe $dotnetExe -WorkingDirectory $RepoRoot -DotnetArgumentList $hostArgs
Write-Host "Management API launched in a new terminal at http://localhost:$ApiPort" -ForegroundColor Green
