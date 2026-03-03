<#
.SYNOPSIS
    Shows status of dev servers and optional log output.
.DESCRIPTION
    When dev is started with up.ps1, servers run in separate windows. This script reports
    which processes are listening on dev ports (9000, 9001) and can tail a simple log file
    if you run the platform with logging to file (e.g. via environment or future script option).
.EXAMPLE
    .\logs.ps1
    .\logs.ps1 -Port 9000
#>
[CmdletBinding()]
param(
    [int[]]$Port = @(9000, 9001)
)

function Get-Listeners {
    param([int[]]$Ports)
    foreach ($p in $Ports) {
        $conn = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
        if ($conn) {
            $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
            [PSCustomObject]@{
                Port = $p
                PID = $conn.OwningProcess
                Process = $proc.ProcessName
            }
        } else {
            [PSCustomObject]@{ Port = $p; PID = $null; Process = "—" }
        }
    }
}

Write-Host "Apperture dev servers:" -ForegroundColor Cyan
Get-Listeners -Ports $Port | Format-Table -AutoSize
Write-Host "Logs are shown in the console windows started by up.ps1." -ForegroundColor Gray
