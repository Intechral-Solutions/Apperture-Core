<#
.SYNOPSIS
    Rebuilds and restarts only the Management API (Management.Host), without restarting the UI, worker, or SQL container.
.DESCRIPTION
    Stops whatever is listening on port 9000, builds Apperture.Management.Host, then starts it with the same
    launch profile as scripts\dev\up.ps1 (http, port 9000).
.EXAMPLE
    .\restart-host.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$HostProject = Join-Path $RepoRoot "src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj"
$ResolveScript = Join-Path $PSScriptRoot "Get-DevDotNetSdk.ps1"
$ApiPort = 9000

if (-not (Test-Path $HostProject)) {
    Write-Error "Management.Host project not found: $HostProject"
}

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

$sdk = & $ResolveScript -RepoRoot $RepoRoot
$dotnetExe = $sdk.DotNetExe
$env:DOTNET_ROOT = $sdk.DotNetRoot
if ($env:PATH -notlike "*$($sdk.DotNetRoot)*") {
    $env:PATH = "$($sdk.DotNetRoot);$env:PATH"
}

Write-Host "Building Management.Host..." -ForegroundColor Cyan
& $dotnetExe build $HostProject --no-incremental
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

Write-Host "Restarting API on port $ApiPort..." -ForegroundColor Cyan
Stop-ProcessByPort -Port $ApiPort

$hostArgs = @("run", "--project", $HostProject, "--launch-profile", "http")
Start-Process -FilePath $dotnetExe -ArgumentList $hostArgs -WorkingDirectory $RepoRoot -WindowStyle Normal
Write-Host "Management API restarted at http://localhost:$ApiPort" -ForegroundColor Green
