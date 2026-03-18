<#
.SYNOPSIS
    Stops the Apperture dev environment (processes listening on ports 9000 and 9001).
.DESCRIPTION
    Terminates dotnet processes hosting the Management API and UI Server.
.EXAMPLE
    .\down.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$ports = @(9000, 9001)

function Get-NetTCPConnectionByPort {
    param([int]$Port)
    Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" }
}

function Stop-ProcessByPort {
    param([int]$Port)
    $conn = Get-NetTCPConnectionByPort -Port $Port
    if ($conn) {
        $pid = $conn.OwningProcess
        $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "Stopping process on port $Port (PID $pid): $($proc.ProcessName)" -ForegroundColor Yellow
            Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
        }
    }
}

foreach ($port in $ports) {
    Stop-ProcessByPort -Port $port
}

# Stop instance-plane worker host (no fixed port)
function Stop-WorkerHostByProject {
    param([string]$ProjectPath)
    if (-not (Test-Path $ProjectPath)) {
        return
    }

    $projectFileName = [System.IO.Path]::GetFileName($ProjectPath)
    $workerProcs = Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -eq "dotnet.exe" -and
            $_.CommandLine -like "*$projectFileName*"
        }

    foreach ($p in $workerProcs) {
        Write-Host "Stopping worker host: $projectFileName (PID $($p.ProcessId))" -ForegroundColor Yellow
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

$WorkerHostProject = Join-Path $RepoRoot "src\Apperture.RuntimeHost\Apperture.RuntimeHost.Windows\Apperture.RuntimeHost.Windows.WorkerHost\Apperture.RuntimeHost.Windows.WorkerHost.csproj"
Stop-WorkerHostByProject -ProjectPath $WorkerHostProject

Write-Host "Dev environment stopped." -ForegroundColor Green
