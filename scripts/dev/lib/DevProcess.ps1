# Process and port management for native dev services.
Set-StrictMode -Version Latest

function Get-DevTcpListeners {
    param([Parameter(Mandatory)][int]$Port)
    @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq 'Listen' })
}

function Stop-DevProcessByPort {
    param([Parameter(Mandatory)][int]$Port)

    $conns = @(Get-DevTcpListeners -Port $Port)
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

function Stop-DevDotNetByProject {
    param([Parameter(Mandatory)][string]$ProjectPath)

    if (-not (Test-Path -LiteralPath $ProjectPath)) { return }

    $projectFileName = [System.IO.Path]::GetFileName($ProjectPath)
    $procs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine -like "*$projectFileName*"
        }

    foreach ($p in $procs) {
        Write-Host "Stopping dotnet host: $projectFileName (PID $($p.ProcessId))" -ForegroundColor Yellow
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Stop-DevProcessByPidFile {
    param([Parameter(Mandatory)][string]$ServiceName)

    $pidValue = Read-DevServicePid -ServiceName $ServiceName
    if ($null -eq $pidValue) { return }

    $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "Stopping $ServiceName from PID file (PID $pidValue): $($proc.ProcessName)" -ForegroundColor Yellow
        Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue
    }
}

function Wait-DevTcpConnectable {
    param(
        [string]$ComputerName = '127.0.0.1',
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
    throw "Timeout (${TimeoutSeconds}s) waiting for TCP ${ComputerName}:$Port."
}

function Wait-DevListenPortsFree {
    param(
        [int[]]$Ports = @(9000, 9001),
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $busy = $false
        foreach ($p in $Ports) {
            if (@(Get-DevTcpListeners -Port $p).Count -gt 0) {
                $busy = $true
                break
            }
        }
        if (-not $busy) { return }
        Start-Sleep -Milliseconds 350
    }
    throw "Timeout (${TimeoutSeconds}s) waiting for ports to be free: $($Ports -join ', ')."
}

function Wait-DevServiceListening {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$ServiceName,
        [int]$TimeoutSeconds = 60,
        [scriptblock]$ExitCheck = $null
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($ExitCheck) {
            & $ExitCheck
        }
        if (@(Get-DevTcpListeners -Port $Port).Count -gt 0) {
            Write-Host "  ${ServiceName}: listening on http://localhost:$Port" -ForegroundColor Gray
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "$ServiceName did not listen on port $Port within ${TimeoutSeconds}s."
}

function Stop-DevAspireProcesses {
    Stop-DevProcessByPidFile -ServiceName 'aspire'

    $appHostFile = 'Apperture.AppHost.csproj'
    $procs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine -like "*$appHostFile*") -or
            ($_.Name -eq 'dcp.exe')
        }

    foreach ($p in $procs) {
        Write-Host "Stopping Aspire-related process (PID $($p.ProcessId)): $($p.Name)" -ForegroundColor Yellow
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Get-DevPortStatus {
    param([Parameter(Mandatory)][int[]]$Ports)

    foreach ($p in $Ports) {
        $conn = Get-DevTcpListeners -Port $p | Select-Object -First 1
        if ($conn) {
            $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
            [PSCustomObject]@{
                Port    = $p
                PID     = $conn.OwningProcess
                Process = if ($proc) { $proc.ProcessName } else { '?' }
                State   = 'Listen'
            }
        }
        else {
            [PSCustomObject]@{
                Port    = $p
                PID     = $null
                Process = '-'
                State   = 'Free'
            }
        }
    }
}
