# Dev runtime state files under scripts/.runtime/dev/.
Set-StrictMode -Version Latest

function Get-DevRuntimeModePath {
    return (Join-Path (Get-AppertureDevRuntimeRoot) 'mode.json')
}

function Get-DevServicePidPath {
    param([Parameter(Mandatory)][string]$ServiceName)
    return (Join-Path (Get-AppertureDevRuntimeRoot) "$ServiceName.pid")
}

function Read-DevRuntimeMode {
    $path = Get-DevRuntimeModePath
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

function Write-DevRuntimeMode {
    param(
        [Parameter(Mandatory)][string]$Mode
    )

    $payload = [ordered]@{
        mode       = $Mode
        startedUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    ($payload | ConvertTo-Json -Depth 3) | Set-Content -Path (Get-DevRuntimeModePath) -Encoding UTF8
}

function Clear-DevRuntimeMode {
    $path = Get-DevRuntimeModePath
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

function Write-DevServicePid {
    param(
        [Parameter(Mandatory)][string]$ServiceName,
        [Parameter(Mandatory)][int]$ProcessId
    )
    Set-Content -Path (Get-DevServicePidPath -ServiceName $ServiceName) -Value $ProcessId -Encoding ASCII
}

function Read-DevServicePid {
    param([Parameter(Mandatory)][string]$ServiceName)
    $path = Get-DevServicePidPath -ServiceName $ServiceName
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $raw = (Get-Content -LiteralPath $path -Raw).Trim()
    if ($raw -match '^\d+$') { return [int]$raw }
    return $null
}

function Clear-DevServicePid {
    param([Parameter(Mandatory)][string]$ServiceName)
    $path = Get-DevServicePidPath -ServiceName $ServiceName
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

function Clear-AllDevServicePids {
    foreach ($name in @('api', 'ui', 'worker', 'aspire')) {
        Clear-DevServicePid -ServiceName $name
    }
}

function Test-DevRuntimeModeMismatch {
    param(
        [Parameter(Mandatory)][string]$ExpectedMode
    )

    $state = Read-DevRuntimeMode
    if ($null -eq $state) { return }
    if ($state.mode -and $state.mode -ne $ExpectedMode) {
        Write-Host "Warning: runtime mode.json says '$($state.mode)' but command uses '$ExpectedMode'. Using '$ExpectedMode'." -ForegroundColor Yellow
    }
}

function Get-DevWorkerStatePath {
    return (Join-Path (Get-AppertureDevLogDir) 'worker-runtime-state.json')
}
