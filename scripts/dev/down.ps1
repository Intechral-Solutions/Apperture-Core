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

Write-Host "Dev environment stopped." -ForegroundColor Green
